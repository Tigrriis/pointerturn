'use strict';

const crypto = require('node:crypto');
const { newToken, hashToken } = require('./security');

// Statuses under which the program is allowed to run. `past_due` is kept working while
// Stripe retries the card.
const USABLE = ['active', 'past_due'];

function loadSigningKey(config, log = console.warn) {
  let pem = config.licence.signingKey.trim();
  if (!pem) {
    log('[licence] LICENCE_SIGNING_KEY is not set: using a throwaway key. Signed licences will stop verifying when the server restarts.');
    return crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' }).privateKey;
  }
  if (!pem.includes('-----BEGIN')) pem = Buffer.from(pem, 'base64').toString('utf8');
  const key = crypto.createPrivateKey(pem.replace(/\\n/g, '\n'));
  if (key.asymmetricKeyType !== 'ec' || key.asymmetricKeyDetails.namedCurve !== 'prime256v1') {
    throw new Error('LICENCE_SIGNING_KEY must be an EC P-256 private key');
  }
  return key;
}

const b64url = (value) => Buffer.from(typeof value === 'string' ? value : JSON.stringify(value)).toString('base64url');

function createLicences({ db, config }) {
  const privateKey = loadSigningKey(config);
  const publicKey = crypto.createPublicKey(privateKey);
  const jwk = publicKey.export({ format: 'jwk' });
  // The key ID is the RFC 7638 thumbprint, so the add-in can tell which key signed a licence.
  const kid = crypto
    .createHash('sha256')
    .update(JSON.stringify({ crv: jwk.crv, kty: jwk.kty, x: jwk.x, y: jwk.y }))
    .digest('base64url');

  // A signed licence is a compact JWS (a JWT) signed with ES256. The add-in checks the
  // signature against the public key it ships with and reads the claims; no secret is
  // needed on the user's computer.
  function sign(claims) {
    const input = `${b64url({ alg: 'ES256', typ: 'JWT', kid })}.${b64url(claims)}`;
    const signature = crypto.sign('sha256', Buffer.from(input), { key: privateKey, dsaEncoding: 'ieee-p1363' });
    return `${input}.${signature.toString('base64url')}`;
  }

  function licenceToken(user, licence, activation) {
    const now = Math.floor(Date.now() / 1000);
    const exp = now + config.licence.tokenDays * 86400;
    const token = sign({
      iss: 'pointerturn',
      sub: String(user.id),
      email: user.email,
      lic: String(licence.id),
      kind: licence.kind,
      mid: activation.machine_id,
      iat: now,
      exp,
    });
    return { token, expiresAt: new Date(exp * 1000).toISOString() };
  }

  function describe(licence) {
    return {
      id: String(licence.id),
      kind: licence.kind,
      status: licence.status,
      // For a subscription, when the paid period ends (it renews then unless cancelled).
      current_period_end: licence.current_period_end ? new Date(licence.current_period_end).toISOString() : null,
    };
  }

  // The licence an account can use right now, if it has one.
  async function usableLicence(userId) {
    const { rows } = await db.query(
      `SELECT * FROM licences WHERE user_id = $1 AND status = ANY($2)
       ORDER BY (status = 'active') DESC, created_at DESC LIMIT 1`,
      [userId, USABLE]
    );
    return rows[0] || null;
  }

  // The licence to show on the account page: the usable one, else the most recent.
  async function latestLicence(userId) {
    return (
      (await usableLicence(userId)) ||
      (await db.query('SELECT * FROM licences WHERE user_id = $1 ORDER BY created_at DESC LIMIT 1', [userId])).rows[0] ||
      null
    );
  }

  async function machines(licenceId) {
    const { rows } = await db.query(
      'SELECT id, machine_name, created_at, last_seen_at FROM activations WHERE licence_id = $1 AND revoked_at IS NULL ORDER BY created_at',
      [licenceId]
    );
    return rows;
  }

  // Sign a computer in. Returns { ok, ... } or { error, ... }.
  async function activate(user, { machineId, machineName }) {
    const licence = await usableLicence(user.id);
    if (!licence) return { error: 'no_licence' };

    const activationToken = newToken();
    const client = await db.connect();
    try {
      await client.query('BEGIN');
      // Lock the licence row so two computers can't both take the last place.
      await client.query('SELECT id FROM licences WHERE id = $1 FOR UPDATE', [licence.id]);
      const live = (
        await client.query('SELECT * FROM activations WHERE licence_id = $1 AND revoked_at IS NULL ORDER BY created_at', [licence.id])
      ).rows;
      let activation = live.find((a) => a.machine_id === machineId);
      if (activation) {
        // The same computer signing in again: give it a new token, the old one stops working.
        activation = (
          await client.query(
            'UPDATE activations SET token_hash = $1, machine_name = $2, last_seen_at = now() WHERE id = $3 RETURNING *',
            [hashToken(activationToken), machineName, activation.id]
          )
        ).rows[0];
      } else if (live.length >= licence.max_machines) {
        await client.query('ROLLBACK');
        return {
          error: 'machine_limit',
          max_machines: licence.max_machines,
          machines: live.map((a) => ({ name: a.machine_name, last_seen_at: a.last_seen_at.toISOString() })),
        };
      } else {
        activation = (
          await client.query(
            'INSERT INTO activations (licence_id, machine_id, machine_name, token_hash) VALUES ($1, $2, $3, $4) RETURNING *',
            [licence.id, machineId, machineName, hashToken(activationToken)]
          )
        ).rows[0];
      }
      await client.query('COMMIT');
      const signed = licenceToken(user, licence, activation);
      return {
        ok: true,
        activation_token: activationToken,
        licence_token: signed.token,
        licence_token_expires_at: signed.expiresAt,
        licence: describe(licence),
      };
    } catch (err) {
      await client.query('ROLLBACK').catch(() => {});
      throw err;
    } finally {
      client.release();
    }
  }

  async function findActivation(activationToken) {
    const { rows } = await db.query(
      `SELECT a.id AS activation_id, a.machine_id, l.*, u.email AS user_email
         FROM activations a JOIN licences l ON l.id = a.licence_id JOIN users u ON u.id = l.user_id
        WHERE a.token_hash = $1 AND a.revoked_at IS NULL`,
      [hashToken(activationToken)]
    );
    return rows[0] || null;
  }

  // Trade an activation token for a fresh signed licence.
  async function refresh(activationToken) {
    const row = await findActivation(activationToken);
    if (!row) return { error: 'invalid_activation' };
    if (!USABLE.includes(row.status)) return { error: 'licence_inactive', licence: describe(row) };
    await db.query('UPDATE activations SET last_seen_at = now() WHERE id = $1', [row.activation_id]);
    const signed = licenceToken({ id: row.user_id, email: row.user_email }, row, { machine_id: row.machine_id });
    return { ok: true, licence_token: signed.token, licence_token_expires_at: signed.expiresAt, licence: describe(row) };
  }

  async function deactivateByToken(activationToken) {
    await db.query('UPDATE activations SET revoked_at = now() WHERE token_hash = $1 AND revoked_at IS NULL', [hashToken(activationToken)]);
  }

  // From the account page: free up a computer's place.
  async function deactivateForUser(userId, activationId) {
    await db.query(
      `UPDATE activations a SET revoked_at = now() FROM licences l
        WHERE a.id = $1 AND a.licence_id = l.id AND l.user_id = $2 AND a.revoked_at IS NULL`,
      [activationId, userId]
    );
  }

  return {
    USABLE,
    publicKey: { kid, jwk, pem: publicKey.export({ type: 'spki', format: 'pem' }) },
    usableLicence,
    latestLicence,
    machines,
    activate,
    refresh,
    deactivateByToken,
    deactivateForUser,
  };
}

module.exports = { createLicences, USABLE };
