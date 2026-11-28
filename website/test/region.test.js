'use strict';

// PointerPath restricted to Australia (ALLOWED_COUNTRIES=AU): what works from inside and
// what is refused from outside. Countries come from the bundled IP database, for real.

const test = require('node:test');
const assert = require('node:assert/strict');
const { start } = require('./helpers');
const { countryOfIp } = require('../src/geo');

// The tests name the visitor's address in a header, standing in for the hosting platform's.
const from = (ip) => ({ 'x-client-ip': ip });
const AU = from('1.0.0.1');
const US = from('8.8.8.8');
const UNKNOWN = from('10.0.0.5');

let t;
test.before(async () => {
  t = await start({ ALLOWED_COUNTRIES: 'au', CLIENT_IP_HEADER: 'X-Client-IP' });
});
test.after(async () => {
  await t.stop();
});

const paid = (id, userId, extra) => ({ id: `evt_${id}`, object: 'event', type: 'checkout.session.completed', data: { object: t.paidSession(id, userId, extra) } });

test('the bundled database places addresses in countries', () => {
  assert.equal(countryOfIp('1.0.0.1'), 'AU');
  assert.equal(countryOfIp('8.8.8.8'), 'US');
  assert.equal(countryOfIp('::ffff:1.0.0.1'), 'AU');
  assert.equal(countryOfIp('2001:4860:4860::8888'), 'US');
  assert.equal(countryOfIp('2403:5800::1'), 'AU');
  for (const ip of ['10.0.0.5', '127.0.0.1', '::1', 'not an address', '', undefined]) assert.equal(countryOfIp(ip), null);
});

test('from Australia the site works as usual and says where it is offered', async () => {
  const home = await t.browser(AU).get('/');
  assert.equal(home.status, 200);
  assert.match(home.text, /Buy a licence/);
  assert.match(home.text, /Available in Australia only/);
  assert.doesNotMatch(home.text, /switched off where you appear to be/);
});

test('from elsewhere the site can be read but nothing can be bought, joined or downloaded', async () => {
  for (const where of [US, UNKNOWN]) {
    const b = t.browser(where);
    const home = await b.get('/');
    assert.equal(home.status, 200);
    assert.match(home.text, /available in Australia only/);
    assert.doesNotMatch(home.text, /Buy a licence/);

    const form = await b.get('/signup');
    assert.equal(form.status, 403);
    assert.match(form.text, /PointerPath is available in Australia only/);
    const post = await b.post('/signup', { email: 'abroad@example.com', password: 'correct horse battery', in_region: 'yes' }, '/login');
    assert.equal(post.status, 403);
  }
  assert.equal((await t.db.query("SELECT count(*)::int AS n FROM users WHERE email = 'abroad@example.com'")).rows[0].n, 0);
});

test('signing up needs the declaration, and records it with the country', async () => {
  const b = t.browser(AU);
  const form = await b.get('/signup');
  assert.match(form.text, /I am in Australia and will use PointerPath only in Australia/);

  const without = await b.post('/signup', { email: 'declare@example.com', password: 'correct horse battery' }, '/signup');
  assert.equal(without.status, 400);
  assert.match(without.text, /confirm you are in Australia/);

  const ok = await b.post('/signup', { email: 'declare@example.com', password: 'correct horse battery', in_region: 'yes' }, '/signup');
  assert.equal(ok.status, 200);
  const user = (await t.db.query("SELECT signup_country, region_declared_at FROM users WHERE email = 'declare@example.com'")).rows[0];
  assert.equal(user.signup_country, 'AU');
  assert.ok(user.region_declared_at instanceof Date);
});

test('buying and downloading work in Australia and are refused to the same account abroad', async () => {
  const { b, id, email, password } = await t.newAccount('traveller@example.com', undefined, AU);

  // Abroad: signed in, can see the account, cannot buy or download.
  const abroad = t.browser(US);
  assert.equal((await abroad.post('/login', { email, password })).location, '/account');
  assert.equal((await abroad.get('/account')).status, 200);
  const before = t.stripe.state.created.length;
  assert.equal((await abroad.post('/buy', {}, '/account')).status, 403);
  assert.equal(t.stripe.state.created.length, before);
  assert.equal((await abroad.get('/download')).status, 403);

  // At home.
  const res = await b.post('/buy', {}, '/account');
  assert.equal(res.status, 303);
  const { id: sessionId, params } = t.stripe.state.created.at(-1);
  assert.equal(params.billing_address_collection, 'required');
  assert.equal(params.metadata.ip_country, 'AU');
  await t.webhook(paid(sessionId, id, { metadata: params.metadata, customer_details: { address: { country: 'AU' } } }));
  const licence = (await t.db.query('SELECT purchase_country, billing_country FROM licences WHERE user_id = $1', [id])).rows[0];
  assert.deepEqual(licence, { purchase_country: 'AU', billing_country: 'AU' });
  assert.equal((await b.get('/download')).status, 302);
});

test('the add-in can only be activated from Australia, and a licence is not renewed abroad', async () => {
  const { id, email, password } = await t.newAccount('cad@example.com', undefined, AU);
  await t.webhook(paid('cs_test_region', id));
  const body = { email, password, machine_id: 'm1', machine_name: 'DESK' };

  for (const where of [US, UNKNOWN]) {
    const refused = await t.api('/activate', body, where);
    assert.equal(refused.status, 403);
    assert.equal(refused.body.error, 'region_not_allowed');
  }
  assert.equal((await t.db.query('SELECT count(*)::int AS n FROM activations')).rows[0].n, 0);

  const ok = await t.api('/activate', body, AU);
  assert.equal(ok.status, 200);
  assert.equal((await t.db.query('SELECT country FROM activations')).rows[0].country, 'AU');

  // Abroad, the computer keeps the licence it holds but is not given a new one...
  const abroad = await t.api('/refresh', { activation_token: ok.body.activation_token }, US);
  assert.equal(abroad.status, 403);
  assert.equal(abroad.body.error, 'region_not_allowed');
  // ...and back home it refreshes again, still signed in.
  assert.equal((await t.api('/refresh', { activation_token: ok.body.activation_token }, AU)).status, 200);

  // Signing out works from anywhere.
  assert.equal((await t.api('/deactivate', { activation_token: ok.body.activation_token }, US)).status, 200);
});

test('a visitor cannot pick their own country when no platform header is configured', async () => {
  await t.stop();
  t = await start({ ALLOWED_COUNTRIES: 'AU' });
  // The test connection is from 127.0.0.1, which is nowhere: refused, whatever headers say.
  const res = await t.browser({ 'x-client-ip': '1.0.0.1', 'cf-connecting-ip': '1.0.0.1', 'cf-ipcountry': 'AU' }).get('/signup');
  assert.equal(res.status, 403);
});

test('behind two proxies, as on Render, the visitor is found and a forged address is ignored', async () => {
  await t.stop();
  t = await start({ ALLOWED_COUNTRIES: 'AU', CLIENT_IP_HEADER: 'cf-connecting-ip', TRUST_PROXY_HOPS: '2' });
  const signup = async (headers) => (await t.browser(headers).get('/signup')).status;

  // What Render delivers: Cloudflare names the visitor, and appends its own address.
  assert.equal(await signup({ 'cf-connecting-ip': '1.0.0.1', 'x-forwarded-for': '1.0.0.1, 172.71.124.52' }), 200);
  assert.equal(await signup({ 'cf-connecting-ip': '8.8.8.8', 'x-forwarded-for': '8.8.8.8, 172.71.124.52' }), 403);

  // A visitor abroad who sends their own X-Forwarded-For only adds to the front of the list.
  assert.equal(await signup({ 'cf-connecting-ip': '8.8.8.8', 'x-forwarded-for': '1.0.0.1, 8.8.8.8, 172.71.124.52' }), 403);
  await t.stop();
  t = { stop: async () => {} };
});

test('a product ID in place of a price ID still sells the product\'s price', async () => {
  const buyWith = async (products, env) => {
    await t.stop();
    t = await start(env);
    Object.assign(t.stripe.state.products, products);
    const { b } = await t.newAccount('buyer@example.com');
    const home = await t.browser().get('/');
    const res = await b.post('/buy', {}, '/account');
    return { home, res, b };
  };

  // The product has a default price.
  let r = await buyWith({ prod_1: { id: 'prod_1', default_price: 'price_1', prices: [] } }, { STRIPE_PRICE_ID: 'prod_1' });
  assert.match(r.home.text, /\$495/);
  assert.equal(r.res.status, 303);
  assert.equal(t.stripe.state.created.at(-1).params.line_items[0].price, 'price_1');

  // No default, but only one active price.
  r = await buyWith({ prod_2: { id: 'prod_2', default_price: null, prices: [{ id: 'price_1' }] } }, { STRIPE_PRICE_ID: 'prod_2' });
  assert.equal(r.res.status, 303);
  assert.equal(t.stripe.state.created.at(-1).params.line_items[0].price, 'price_1');

  // Nothing to choose a price by: the buyer is told, on their account page, and not charged.
  r = await buyWith({ prod_3: { id: 'prod_3', default_price: null, prices: [{ id: 'price_1' }, { id: 'price_9' }] } }, { STRIPE_PRICE_ID: 'prod_3' });
  assert.equal(r.home.status, 200);
  assert.equal(r.res.location, '/account');
  assert.match((await r.b.get('/account')).text, /checkout could not be started/);
  assert.equal(t.stripe.state.created.length, 0);

  // An ID Stripe doesn't know at all does the same.
  r = await buyWith({}, { STRIPE_PRICE_ID: 'price_missing' });
  assert.equal(r.res.location, '/account');
  assert.match((await r.b.get('/account')).text, /checkout could not be started/);

  await t.stop();
  t = { stop: async () => {} };
});

test('a full price is shown struck through only while the price charged is lower', async () => {
  await t.stop();
  t = await start({ PRICE_WAS: '595' });
  let home = (await t.browser().get('/')).text;
  assert.match(home, /<s class="price__was"><span class="visually-hidden">Was <\/span>\$595<\/s>/);
  assert.match(home, /\$495/);
  const { b } = await t.newAccount('price@example.com');
  assert.match((await b.get('/account')).text, /price__was/);
  await t.stop();

  // Once the price charged has caught up with it, there is nothing to strike through.
  t = await start({ PRICE_WAS: '495' });
  home = (await t.browser().get('/')).text;
  assert.doesNotMatch(home, /price__was/);
  await t.stop();
  t = { stop: async () => {} };
});
