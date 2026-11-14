'use strict';

const express = require('express');
const { hashPassword, verifyPassword, burnPasswordCheck, newToken, hashToken, isEmail, safeNext } = require('../security');
const { requireRegion } = require('./site');

const MIN_PASSWORD = 10;
const VERIFY_HOURS = 24;
const RESET_HOURS = 1;

module.exports = function auth({ db, mailer, geo, log, authLimiter }) {
  const router = express.Router();

  async function issueToken(userId, kind, hours) {
    const token = newToken();
    await db.query(
      `INSERT INTO email_tokens (user_id, kind, token_hash, expires_at) VALUES ($1, $2, $3, now() + make_interval(hours => $4))`,
      [userId, kind, hashToken(token), hours]
    );
    return token;
  }

  // Marks a token used and returns its account, or null if it is unknown, used or expired.
  async function redeemToken(token, kind) {
    const { rows } = await db.query(
      `UPDATE email_tokens SET used_at = now()
        WHERE token_hash = $1 AND kind = $2 AND used_at IS NULL AND expires_at > now()
        RETURNING user_id`,
      [hashToken(token || ''), kind]
    );
    if (!rows[0]) return null;
    return (await db.query('SELECT * FROM users WHERE id = $1', [rows[0].user_id])).rows[0] || null;
  }

  // A fresh session on sign-in, so a session ID from before can't be reused.
  function signIn(req, user) {
    const returnTo = req.session.returnTo;
    return new Promise((resolve, reject) => {
      req.session.regenerate((err) => {
        if (err) return reject(err);
        req.session.userId = user.id;
        resolve(safeNext(returnTo) || '/account');
      });
    });
  }

  const sendMail = (promise) => promise.catch((err) => log.error('[mail] send failed:', err.message));

  function rememberNext(req) {
    const next = safeNext(req.query.next);
    if (next) req.session.returnTo = next;
  }

  // ---- Create an account ----

  router.get('/signup', requireRegion, (req, res) => {
    if (req.user) return res.redirect('/account');
    rememberNext(req);
    res.render('signup', { error: null, email: '' });
  });

  router.post('/signup', authLimiter, requireRegion, async (req, res) => {
    const email = String(req.body.email || '').trim();
    const password = String(req.body.password || '');
    if (!isEmail(email)) return res.status(400).render('signup', { error: 'Enter a valid email address.', email });
    if (password.length < MIN_PASSWORD) {
      return res.status(400).render('signup', { error: `Choose a password of at least ${MIN_PASSWORD} characters.`, email });
    }
    // Where PointerPath is restricted to some countries, the person says they are in one.
    if (geo.restricted && req.body.in_region !== 'yes') {
      return res.status(400).render('signup', { error: `Tick the box to confirm you are in ${geo.where}.`, email });
    }

    const hash = await hashPassword(password);
    const { rows } = await db.query(
      `INSERT INTO users (email, password_hash, signup_country, region_declared_at)
       VALUES ($1, $2, $3, CASE WHEN $4 THEN now() END)
       ON CONFLICT (lower(email)) DO NOTHING RETURNING id`,
      [email, hash, req.region.country, geo.restricted]
    );
    // The page says the same thing either way, so it can't be used to find out who has an account.
    if (rows[0]) await sendMail(mailer.sendVerification(email, await issueToken(rows[0].id, 'verify', VERIFY_HOURS)));
    else await sendMail(mailer.sendAlreadyRegistered(email));

    res.locals.pageEvent = 'Signup';
    res.render('message', {
      title: 'Check your email',
      body: `We sent a link to ${email}. Open it to confirm your address and finish creating your account.`,
    });
  });

  router.get('/verify', async (req, res) => {
    const user = await redeemToken(req.query.token, 'verify');
    if (!user) {
      return res.status(400).render('message', {
        title: 'That link has expired',
        body: 'Sign in and we will send you a new confirmation link.',
        action: { href: '/login', label: 'Sign in' },
      });
    }
    await db.query('UPDATE users SET email_verified_at = COALESCE(email_verified_at, now()) WHERE id = $1', [user.id]);
    const next = await signIn(req, user);
    req.session.flash = 'Your email address is confirmed.';
    res.redirect(next);
  });

  router.post('/verify/resend', authLimiter, async (req, res) => {
    if (!req.user) return res.redirect('/login');
    if (!req.user.email_verified_at) {
      await sendMail(mailer.sendVerification(req.user.email, await issueToken(req.user.id, 'verify', VERIFY_HOURS)));
      req.session.flash = `We sent a new confirmation link to ${req.user.email}.`;
    }
    res.redirect('/account');
  });

  // ---- Sign in and out ----

  router.get('/login', (req, res) => {
    if (req.user) return res.redirect('/account');
    rememberNext(req);
    res.render('login', { error: null, email: '' });
  });

  router.post('/login', authLimiter, async (req, res) => {
    const email = String(req.body.email || '').trim();
    const password = String(req.body.password || '');
    const user = (await db.query('SELECT * FROM users WHERE lower(email) = lower($1)', [email])).rows[0];
    let ok = false;
    if (user) ok = await verifyPassword(password, user.password_hash);
    else await burnPasswordCheck(password);
    if (!ok) return res.status(401).render('login', { error: 'That email and password do not match an account.', email });
    res.redirect(await signIn(req, user));
  });

  router.post('/logout', (req, res, next) => {
    req.session.destroy((err) => {
      if (err) return next(err);
      res.clearCookie('pt.sid');
      res.redirect('/');
    });
  });

  // ---- Forgotten password ----

  router.get('/forgot', (req, res) => res.render('forgot', { error: null }));

  router.post('/forgot', authLimiter, async (req, res) => {
    const email = String(req.body.email || '').trim();
    if (!isEmail(email)) return res.status(400).render('forgot', { error: 'Enter a valid email address.' });
    const user = (await db.query('SELECT id, email FROM users WHERE lower(email) = lower($1)', [email])).rows[0];
    if (user) await sendMail(mailer.sendPasswordReset(user.email, await issueToken(user.id, 'reset', RESET_HOURS)));
    res.render('message', {
      title: 'Check your email',
      body: `If ${email} has an account, we sent it a link to choose a new password.`,
    });
  });

  router.get('/reset', async (req, res) => {
    const token = String(req.query.token || '');
    const { rows } = await db.query(
      "SELECT 1 FROM email_tokens WHERE token_hash = $1 AND kind = 'reset' AND used_at IS NULL AND expires_at > now()",
      [hashToken(token)]
    );
    if (!rows[0]) {
      return res.status(400).render('message', {
        title: 'That link has expired',
        body: 'Password links work once, for one hour. Ask for a new one.',
        action: { href: '/forgot', label: 'Send a new link' },
      });
    }
    res.render('reset', { error: null, token });
  });

  router.post('/reset', authLimiter, async (req, res) => {
    const token = String(req.body.token || '');
    const password = String(req.body.password || '');
    if (password.length < MIN_PASSWORD) {
      return res.status(400).render('reset', { error: `Choose a password of at least ${MIN_PASSWORD} characters.`, token });
    }
    const user = await redeemToken(token, 'reset');
    if (!user) {
      return res.status(400).render('message', {
        title: 'That link has expired',
        body: 'Password links work once, for one hour. Ask for a new one.',
        action: { href: '/forgot', label: 'Send a new link' },
      });
    }
    // Following the emailed link also proves the address, so it counts as confirming it.
    await db.query('UPDATE users SET password_hash = $1, email_verified_at = COALESCE(email_verified_at, now()) WHERE id = $2', [
      await hashPassword(password),
      user.id,
    ]);
    // Sign out everywhere else: whoever knew the old password should not stay signed in.
    await db.query("DELETE FROM session WHERE (sess ->> 'userId')::bigint = $1", [user.id]);
    const next = await signIn(req, user);
    req.session.flash = 'Your password has been changed.';
    res.redirect(next);
  });

  return router;
};
