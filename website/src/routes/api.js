'use strict';

// The licence API the add-in calls. See "Licence API" in the website README.

const express = require('express');
const { verifyPassword, burnPasswordCheck, isEmail } = require('../security');

const STATUS = {
  invalid_request: 400,
  invalid_credentials: 401,
  invalid_activation: 401,
  no_licence: 402,
  licence_inactive: 402,
  email_not_verified: 403,
  region_not_allowed: 403,
  machine_limit: 409,
};

const text = (value, max) => (typeof value === 'string' ? value.trim().slice(0, max) : '');

module.exports = function api({ db, licences }) {
  const router = express.Router();

  const reply = (res, result) => {
    if (result.error) return res.status(STATUS[result.error] || 400).json(result);
    const { ok, ...body } = result;
    res.json(body);
  };

  // Sign a computer in with the account's email and password.
  router.post('/activate', async (req, res) => {
    const email = text(req.body?.email, 254);
    const password = typeof req.body?.password === 'string' ? req.body.password : '';
    const machineId = text(req.body?.machine_id, 200);
    const machineName = text(req.body?.machine_name, 100);
    if (!isEmail(email) || !password || !machineId) return reply(res, { error: 'invalid_request' });
    if (!req.region.allowed) return reply(res, { error: 'region_not_allowed' });

    const user = (await db.query('SELECT * FROM users WHERE lower(email) = lower($1)', [email])).rows[0];
    if (!user) {
      await burnPasswordCheck(password);
      return reply(res, { error: 'invalid_credentials' });
    }
    if (!(await verifyPassword(password, user.password_hash))) return reply(res, { error: 'invalid_credentials' });
    if (!user.email_verified_at) return reply(res, { error: 'email_not_verified' });

    reply(res, await licences.activate(user, { machineId, machineName, country: req.region.country }));
  });

  // Trade the activation token for a fresh signed licence.
  router.post('/refresh', async (req, res) => {
    const token = text(req.body?.activation_token, 200);
    if (!token) return reply(res, { error: 'invalid_request' });
    // From outside the countries PointerTurn is offered in, no new licence is issued. The
    // one the computer holds is left to run out, so a short trip abroad doesn't cut it off.
    if (!req.region.allowed) return reply(res, { error: 'region_not_allowed' });
    reply(res, await licences.refresh(token, { country: req.region.country }));
  });

  // Sign a computer out, freeing its place.
  router.post('/deactivate', async (req, res) => {
    const token = text(req.body?.activation_token, 200);
    if (!token) return reply(res, { error: 'invalid_request' });
    await licences.deactivateByToken(token);
    res.json({});
  });

  // The public half of the signing key, for building into the add-in.
  router.get('/licence-key', (req, res) => {
    res.json({ alg: 'ES256', ...licences.publicKey });
  });

  router.use((req, res) => res.status(404).json({ error: 'not_found' }));

  return router;
};
