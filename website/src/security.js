'use strict';

const crypto = require('node:crypto');
const { promisify } = require('node:util');

const scrypt = promisify(crypto.scrypt);
const SCRYPT = { N: 32768, r: 8, p: 1, maxmem: 64 * 1024 * 1024 };
const KEY_LENGTH = 64;

// Stored as scrypt$N$r$p$salt$hash so the cost can be raised later without a migration.
async function hashPassword(password) {
  const salt = crypto.randomBytes(16);
  const hash = await scrypt(password, salt, KEY_LENGTH, SCRYPT);
  return ['scrypt', SCRYPT.N, SCRYPT.r, SCRYPT.p, salt.toString('base64'), hash.toString('base64')].join('$');
}

async function verifyPassword(password, stored) {
  const parts = String(stored || '').split('$');
  if (parts.length !== 6 || parts[0] !== 'scrypt') return false;
  const [, N, r, p, salt, hash] = parts;
  const expected = Buffer.from(hash, 'base64');
  const actual = await scrypt(password, Buffer.from(salt, 'base64'), expected.length, {
    N: Number(N),
    r: Number(r),
    p: Number(p),
    maxmem: SCRYPT.maxmem,
  });
  return crypto.timingSafeEqual(actual, expected);
}

// A dummy hash to verify against when no account matches, so a wrong email takes as long
// as a wrong password.
let dummyHash;
async function burnPasswordCheck(password) {
  dummyHash = dummyHash || (await hashPassword(crypto.randomBytes(16).toString('hex')));
  await verifyPassword(password, dummyHash);
}

function newToken() {
  return crypto.randomBytes(32).toString('base64url');
}

function hashToken(token) {
  return crypto.createHash('sha256').update(String(token)).digest('hex');
}

function isEmail(value) {
  return typeof value === 'string' && value.length <= 254 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
}

// Only ever redirect to a path on this site.
function safeNext(value) {
  return typeof value === 'string' && /^\/(?![/\\])/.test(value) ? value : null;
}

// Cross-site request forgery protection for the HTML forms: a per-session token that
// every POST must send back in a hidden field.
function csrf() {
  return (req, res, next) => {
    if (!req.session.csrf) req.session.csrf = newToken();
    res.locals.csrfToken = req.session.csrf;
    if (['GET', 'HEAD', 'OPTIONS'].includes(req.method)) return next();
    const sent = Buffer.from(String((req.body && req.body._csrf) || ''));
    const want = Buffer.from(req.session.csrf);
    if (sent.length === want.length && crypto.timingSafeEqual(sent, want)) return next();
    res.status(403).render('message', {
      title: 'That form has expired',
      body: 'Go back, reload the page and try again.',
    });
  };
}

module.exports = { hashPassword, verifyPassword, burnPasswordCheck, newToken, hashToken, isEmail, safeNext, csrf };
