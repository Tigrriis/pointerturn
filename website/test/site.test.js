'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const { start } = require('./helpers');

let t;
test.before(async () => {
  t = await start();
});
test.after(async () => {
  await t.stop();
});

const event = (type, object) => ({ id: `evt_${crypto.randomUUID()}`, object: 'event', type, data: { object } });
const licencesOf = async (userId) => (await t.db.query('SELECT * FROM licences WHERE user_id = $1 ORDER BY id', [userId])).rows;

function verifyLicenceToken(token, jwk) {
  const [header, payload, signature] = token.split('.');
  const key = crypto.createPublicKey({ key: jwk, format: 'jwk' });
  const ok = crypto.verify('sha256', Buffer.from(`${header}.${payload}`), { key, dsaEncoding: 'ieee-p1363' }, Buffer.from(signature, 'base64url'));
  return { ok, claims: JSON.parse(Buffer.from(payload, 'base64url')), header: JSON.parse(Buffer.from(header, 'base64url')) };
}

test('home page renders with the price from Stripe', async () => {
  const res = await t.browser().get('/');
  assert.equal(res.status, 200);
  assert.match(res.text, /swept paths/);
  assert.doesNotMatch(res.text, /Final release price/);
  assert.match(res.text, /\$495/);
  assert.match(res.text, /plus GST/);
  assert.match(res.text, /for two seats/);
});

test('sign up, confirm by email, and land signed in', async () => {
  const b = t.browser();
  const res = await b.post('/signup', { email: 'ada@example.com', password: 'correct horse battery' }, '/signup');
  assert.equal(res.status, 200);
  assert.match(res.text, /Check your email/);

  // Not signed in until the address is confirmed.
  assert.equal((await b.get('/account')).location, '/login');

  const link = t.emailLink('ada@example.com');
  assert.match(link.subject, /Confirm your email/);
  const verified = await b.get(link.path);
  assert.equal(verified.status, 302);
  const account = await b.get(verified.location);
  assert.match(account.text, /ada@example\.com/);
  assert.match(account.text, /no active licence/);

  // The link works once.
  assert.equal((await t.browser().get(link.path)).status, 400);
});

test('signing up twice says the same thing and emails the owner instead', async () => {
  await t.newAccount('twice@example.com');
  const res = await t.browser().post('/signup', { email: 'Twice@Example.com', password: 'another password 1' }, '/signup');
  assert.equal(res.status, 200);
  assert.match(res.text, /Check your email/);
  assert.match(t.emailLink('Twice@Example.com').subject, /already have/);
  assert.equal((await t.db.query("SELECT count(*)::int AS n FROM users WHERE lower(email) = 'twice@example.com'")).rows[0].n, 1);
});

test('sign-up rejects a weak password and a bad email', async () => {
  const b = t.browser();
  assert.equal((await b.post('/signup', { email: 'x@example.com', password: 'short' }, '/signup')).status, 400);
  assert.equal((await b.post('/signup', { email: 'not-an-email', password: 'long enough password' }, '/signup')).status, 400);
});

test('forms without the page token are refused', async () => {
  const b = t.browser();
  await b.get('/login');
  const res = await b.rawPost('/login', { email: 'ada@example.com', password: 'x', _csrf: 'wrong' });
  assert.equal(res.status, 403);
});

test('sign in, sign out, wrong password', async () => {
  const { email, password } = await t.newAccount('login@example.com');
  const b = t.browser();
  assert.equal((await b.post('/login', { email, password: 'wrong password!' })).status, 401);
  assert.equal((await b.post('/login', { email: 'nobody@example.com', password })).status, 401);
  const ok = await b.post('/login', { email: 'LOGIN@example.com', password });
  assert.equal(ok.location, '/account');
  assert.equal((await b.get('/account')).status, 200);
  await b.post('/logout', {}, '/account');
  assert.equal((await b.get('/account')).location, '/login');
});

test('password reset: one-time link, old password stops working, other sessions end', async () => {
  const { b: other, email, password } = await t.newAccount('reset@example.com');
  const b = t.browser();
  const sent = await b.post('/forgot', { email }, '/forgot');
  assert.match(sent.text, /Check your email/);
  // Asking for an unknown address looks identical.
  assert.match((await b.post('/forgot', { email: 'ghost@example.com' }, '/forgot')).text, /Check your email/);

  const link = t.emailLink(email);
  assert.match(link.subject, /Reset/);
  const form = await b.get(link.path);
  assert.equal(form.status, 200);
  const token = new URL('http://x' + link.path).searchParams.get('token');
  const done = await b.post('/reset', { token, password: 'a brand new password' }, link.path);
  assert.equal(done.location, '/account');

  assert.equal((await other.get('/account')).location, '/login');
  assert.equal((await t.browser().post('/login', { email, password })).status, 401);
  assert.equal((await t.browser().post('/login', { email, password: 'a brand new password' })).location, '/account');
  assert.equal((await t.browser().get(link.path)).status, 400);
});

test('download needs a sign-in, then goes to the installer', async () => {
  const anon = await t.browser().get('/download');
  assert.equal(anon.location, '/login');
  const { b } = await t.newAccount('dl@example.com');
  const res = await b.get('/download');
  assert.equal(res.status, 302);
  assert.equal(res.location, 'https://downloads.example.com/PointerPath-1.0.0-setup.exe');
});

test('an unconfirmed account cannot buy', async () => {
  const b = t.browser();
  await b.post('/signup', { email: 'unverified@example.com', password: 'correct horse battery' }, '/signup');
  await b.post('/login', { email: 'unverified@example.com', password: 'correct horse battery' });
  const before = t.stripe.state.created.length;
  const res = await b.post('/buy', {}, '/account');
  assert.equal(res.location, '/account');
  assert.equal(t.stripe.state.created.length, before);
  assert.match((await b.get('/account')).text, /Confirm your email address before buying/);
});

test('buying: checkout, webhook, licence, confirmation email', async () => {
  const { b, id, email } = await t.newAccount('buyer@example.com');
  const res = await b.post('/buy', {}, '/account');
  assert.equal(res.status, 303);
  assert.match(res.location, /^https:\/\/checkout\.stripe\.com\//);
  const { id: sessionId, params } = t.stripe.state.created.at(-1);
  assert.equal(params.mode, 'payment');
  assert.equal(params.client_reference_id, String(id));
  assert.equal(params.line_items[0].price, 'price_1');
  assert.match(params.success_url, /^http:\/\/site\.test\/checkout\/return/);

  const session = t.paidSession(sessionId, id);
  assert.equal(await t.webhook(event('checkout.session.completed', session), { secret: 'whsec_wrong' }), 400);
  assert.equal((await licencesOf(id)).length, 0);

  assert.equal(await t.webhook(event('checkout.session.completed', session)), 200);
  assert.equal(await t.webhook(event('checkout.session.completed', session)), 200); // Stripe redelivers
  const [licence, ...rest] = await licencesOf(id);
  assert.equal(rest.length, 0);
  assert.equal(licence.kind, 'perpetual');
  assert.equal(licence.status, 'active');
  assert.equal(licence.max_machines, 2);
  assert.match(t.emailLink(email).subject, /licence is active/);

  const back = await b.get(`/checkout/return?session_id=${sessionId}`);
  assert.equal(back.location, '/account?purchased=1');
  const account = await b.get(back.location);
  assert.match(account.text, /data-page-event="Purchase"/);
  assert.match(account.text, /does not expire/);

  // A second purchase is refused while the first is active.
  assert.equal((await b.post('/buy', {}, '/account')).location, '/account');
});

test('returning from checkout creates the licence if the webhook has not arrived', async () => {
  const { b, id } = await t.newAccount('early@example.com');
  await b.post('/buy', {}, '/account');
  const sessionId = t.stripe.state.created.at(-1).id;
  t.paidSession(sessionId, id);
  assert.equal((await b.get(`/checkout/return?session_id=${sessionId}`)).location, '/account?purchased=1');
  assert.equal((await licencesOf(id)).length, 1);
});

test('unpaid, foreign and unrelated checkouts create nothing', async () => {
  const { b, id } = await t.newAccount('nolicence@example.com');
  const other = await t.newAccount('someoneelse@example.com');

  // Not paid yet.
  await t.webhook(event('checkout.session.completed', t.paidSession('cs_test_unpaid', id, { payment_status: 'unpaid' })));
  // Another sale on the same Stripe account, not started by this site.
  await t.webhook(event('checkout.session.completed', t.paidSession('cs_test_other', id, { metadata: { user_id: String(id) } })));
  assert.equal((await licencesOf(id)).length, 0);

  // Someone else's paid checkout, opened from this account's browser.
  t.paidSession('cs_test_theirs', other.id);
  assert.equal((await b.get('/checkout/return?session_id=cs_test_theirs')).location, '/account');
  assert.equal((await licencesOf(id)).length, 0);
  assert.equal((await licencesOf(other.id)).length, 0);
});

test('licence API: activate, verify the signature, machine limit, refresh, deactivate', async () => {
  const { b, id, email, password } = await t.newAccount('cad@example.com');
  const pc = (n) => ({ email, password, machine_id: `machine-${n}`, machine_name: `DESK-${n}` });

  assert.equal((await t.api('/activate', { email })).status, 400);
  assert.equal((await t.api('/activate', { ...pc(1), password: 'wrong password!' })).body.error, 'invalid_credentials');
  assert.equal((await t.api('/activate', { ...pc(1), email: 'nobody@example.com' })).status, 401);
  const none = await t.api('/activate', pc(1));
  assert.equal(none.status, 402);
  assert.equal(none.body.error, 'no_licence');

  await t.webhook(event('checkout.session.completed', t.paidSession('cs_test_cad', id)));

  const first = await t.api('/activate', pc(1));
  assert.equal(first.status, 200);
  assert.equal(first.body.latest_version, null); // no installer version is set in these tests
  const { body: key } = await t.api('/licence-key');
  const checked = verifyLicenceToken(first.body.licence_token, key.jwk);
  assert.equal(checked.ok, true);
  assert.equal(checked.header.alg, 'ES256');
  assert.equal(checked.header.kid, key.kid);
  assert.equal(checked.claims.mid, 'machine-1');
  assert.equal(checked.claims.email, email);
  assert.equal(checked.claims.kind, 'perpetual');
  const days = (checked.claims.exp - checked.claims.iat) / 86400;
  assert.equal(days, 14);
  // A tampered licence does not verify.
  const [h, , s] = first.body.licence_token.split('.');
  const forged = Buffer.from(JSON.stringify({ ...checked.claims, exp: checked.claims.exp + 1e6 })).toString('base64url');
  assert.equal(verifyLicenceToken(`${h}.${forged}.${s}`, key.jwk).ok, false);

  assert.equal((await t.api('/activate', pc(2))).status, 200);
  const third = await t.api('/activate', pc(3));
  assert.equal(third.status, 409);
  assert.equal(third.body.error, 'machine_limit');
  assert.deepEqual(third.body.machines.map((m) => m.name), ['DESK-1', 'DESK-2']);

  // The same computer signing in again keeps its place and gets a new token.
  const again = await t.api('/activate', pc(1));
  assert.equal(again.status, 200);
  assert.equal((await t.api('/refresh', { activation_token: first.body.activation_token })).body.error, 'invalid_activation');
  const refreshed = await t.api('/refresh', { activation_token: again.body.activation_token });
  assert.equal(refreshed.status, 200);
  assert.equal(verifyLicenceToken(refreshed.body.licence_token, key.jwk).ok, true);

  // Signing out from the add-in frees the place.
  assert.equal((await t.api('/deactivate', { activation_token: again.body.activation_token })).status, 200);
  assert.equal((await t.api('/refresh', { activation_token: again.body.activation_token })).status, 401);
  const fourth = await t.api('/activate', pc(3));
  assert.equal(fourth.status, 200);

  // And so does signing a computer out from the account page.
  const account = await b.get('/account');
  assert.match(account.text, /signed in on 2 of 2 computers/);
  const machineId = (await t.db.query("SELECT id FROM activations WHERE machine_id = 'machine-3' AND revoked_at IS NULL")).rows[0].id;
  await b.post(`/account/machines/${machineId}/deactivate`, {}, '/account');
  assert.equal((await t.api('/refresh', { activation_token: fourth.body.activation_token })).status, 401);
  assert.match((await b.get('/account')).text, /signed in on 1 of 2 computers/);

  // Another account cannot sign this one's computers out.
  const stranger = await t.newAccount('stranger@example.com');
  const remaining = (await t.db.query("SELECT id FROM activations WHERE machine_id = 'machine-2' AND revoked_at IS NULL")).rows[0].id;
  await stranger.b.post(`/account/machines/${remaining}/deactivate`, {}, '/account');
  assert.match((await b.get('/account')).text, /signed in on 1 of 2 computers/);
});

test('the add-in cannot activate an unconfirmed account', async () => {
  const b = t.browser();
  await b.post('/signup', { email: 'pending@example.com', password: 'correct horse battery' }, '/signup');
  const res = await t.api('/activate', { email: 'pending@example.com', password: 'correct horse battery', machine_id: 'm' });
  assert.equal(res.status, 403);
  assert.equal(res.body.error, 'email_not_verified');
});

test('a full refund ends a one-off licence', async () => {
  const { id, email, password } = await t.newAccount('refund@example.com');
  await t.webhook(event('checkout.session.completed', t.paidSession('cs_test_refund', id)));
  const active = await t.api('/activate', { email, password, machine_id: 'm1' });
  assert.equal(active.status, 200);

  await t.webhook(event('charge.refunded', { id: 'ch_1', refunded: false, payment_intent: 'pi_cs_test_refund' })); // partial
  assert.equal((await licencesOf(id))[0].status, 'active');
  await t.webhook(event('charge.refunded', { id: 'ch_1', refunded: true, payment_intent: 'pi_cs_test_refund' }));
  assert.equal((await licencesOf(id))[0].status, 'refunded');

  const refresh = await t.api('/refresh', { activation_token: active.body.activation_token });
  assert.equal(refresh.status, 402);
  assert.equal(refresh.body.error, 'licence_inactive');
});

test('a recurring price sells a subscription that follows Stripe', async () => {
  t.stripe.state.price = { id: 'price_1', type: 'recurring', currency: 'aud', unit_amount: 29900, tax_behavior: 'unspecified', recurring: { interval: 'year' } };
  try {
    const { b, id, email, password } = await t.newAccount('sub@example.com');
    const res = await b.post('/buy', {}, '/account');
    assert.equal(res.status, 303);
    const { id: sessionId, params } = t.stripe.state.created.at(-1);
    assert.equal(params.mode, 'subscription');

    const end = Math.floor(Date.now() / 1000) + 365 * 86400;
    // Newer Stripe API versions report the period end on the subscription's items.
    t.stripe.state.subscriptions.sub_1 = { id: 'sub_1', status: 'active', items: { data: [{ current_period_end: end }] } };
    await t.webhook(event('checkout.session.completed', t.paidSession(sessionId, id, { mode: 'subscription', subscription: 'sub_1', payment_intent: null })));
    const [licence] = await licencesOf(id);
    assert.equal(licence.kind, 'subscription');
    assert.equal(Math.floor(licence.current_period_end.getTime() / 1000), end);
    assert.match((await b.get('/account')).text, /Paid until/);

    const active = await t.api('/activate', { email, password, machine_id: 'm1' });
    assert.equal(active.status, 200);

    // A failed renewal keeps the program working while Stripe retries the card.
    await t.webhook(event('customer.subscription.updated', { id: 'sub_1', status: 'past_due', current_period_end: end }));
    assert.equal((await t.api('/refresh', { activation_token: active.body.activation_token })).status, 200);
    assert.match((await b.get('/account')).text, /Payment overdue/);

    await t.webhook(event('customer.subscription.deleted', { id: 'sub_1', status: 'canceled', current_period_end: end }));
    assert.equal((await licencesOf(id))[0].status, 'cancelled');
    assert.equal((await t.api('/refresh', { activation_token: active.body.activation_token })).status, 402);
    assert.match((await b.get('/account')).text, /no active licence/);

    // "Invoices and billing" hands over to Stripe's portal.
    assert.match((await b.post('/billing', {}, '/account')).location, /^https:\/\/billing\.stripe\.com\//);
  } finally {
    t.stripe.state.price = { id: 'price_1', type: 'one_time', currency: 'aud', unit_amount: 49500, tax_behavior: 'exclusive', recurring: null };
  }
});

test('pages send a content security policy and no inline styles or scripts', async () => {
  const res = await fetch(`http://127.0.0.1:${t.port}/`);
  assert.match(res.headers.get('content-security-policy'), /form-action 'self' https:\/\/checkout\.stripe\.com/);
  const html = await res.text();
  assert.doesNotMatch(html, /\sstyle="/);
  assert.doesNotMatch(html, /<script(?![^>]*\ssrc=)/);
});

test('Plausible is loaded only when configured, and allowed by the security policy', async () => {
  const none = await (await fetch(`http://127.0.0.1:${t.port}/`)).text();
  assert.doesNotMatch(none, /plausible/i);
  await t.stop();

  for (const [env, src] of [
    [{ PLAUSIBLE_DOMAIN: 'pointerpath.example' }, 'https://plausible.io/js/script.js'],
    [{ PLAUSIBLE_SCRIPT_URL: 'https://plausible.io/js/pa-abc123.js' }, 'https://plausible.io/js/pa-abc123.js'],
  ]) {
    t = await start(env);
    const res = await fetch(`http://127.0.0.1:${t.port}/`);
    assert.match(res.headers.get('content-security-policy'), /script-src 'self' https:\/\/plausible\.io/);
    assert.ok((await res.text()).includes(`src="${src}"`));
    await t.stop();
  }
  t = { stop: async () => {} };
});
