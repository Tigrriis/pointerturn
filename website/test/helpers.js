'use strict';

// Runs the real app against a test database, with a stand-in for Stripe's network calls.
// Needs TEST_DATABASE_URL: a database that can be wiped.

const Stripe = require('stripe');
const { loadConfig } = require('../src/config');
const { createPool, migrate } = require('../src/db');
const { createMailer } = require('../src/mail');
const { createApp } = require('../src/app');

const WEBHOOK_SECRET = 'whsec_test';

function fakeStripe() {
  const real = new Stripe('sk_test_dummy');
  const state = { price: { id: 'price_1', type: 'one_time', currency: 'aud', unit_amount: 49500, tax_behavior: 'exclusive', recurring: null }, sessions: {}, subscriptions: {}, created: [] };
  return {
    state,
    webhooks: real.webhooks,
    prices: { retrieve: async () => state.price },
    customers: { create: async () => ({ id: `cus_${state.created.length + 1}` }) },
    checkout: {
      sessions: {
        create: async (params) => {
          const id = `cs_test_${state.created.length + 1}`;
          state.created.push({ id, params });
          return { id, url: `https://checkout.stripe.com/c/pay/${id}` };
        },
        retrieve: async (id) => state.sessions[id],
      },
    },
    subscriptions: { retrieve: async (id) => state.subscriptions[id] },
    billingPortal: { sessions: { create: async () => ({ url: 'https://billing.stripe.com/p/session/test' }) } },
  };
}

async function start(overrides = {}) {
  const databaseUrl = process.env.TEST_DATABASE_URL;
  if (!databaseUrl) throw new Error('Set TEST_DATABASE_URL to a database the tests can wipe');
  const db = createPool(databaseUrl);
  await db.query('DROP SCHEMA public CASCADE; CREATE SCHEMA public');
  await migrate(db);

  const config = loadConfig({
    DATABASE_URL: databaseUrl,
    SESSION_SECRET: 'test',
    DISABLE_RATE_LIMIT: '1',
    STRIPE_PRICE_ID: 'price_1',
    STRIPE_WEBHOOK_SECRET: WEBHOOK_SECRET,
    INSTALLER_URL: 'https://downloads.example.com/PointerTurn-1.0.0-setup.exe',
    BASE_URL: 'http://site.test',
    ...overrides,
  });
  const stripe = fakeStripe();
  const quiet = { error() {}, warn() {}, log() {} };
  const mailer = createMailer(config, { log() {} });
  const app = createApp({ config, db, stripe, mailer, log: quiet });
  const server = await new Promise((resolve) => {
    const s = app.listen(0, () => resolve(s));
  });
  const base = `http://127.0.0.1:${server.address().port}`;

  // A browser, more or less: keeps cookies, doesn't follow redirects.
  function browser() {
    const jar = new Map();
    async function request(method, path, form) {
      const res = await fetch(base + path, {
        method,
        redirect: 'manual',
        headers: {
          cookie: [...jar].map(([k, v]) => `${k}=${v}`).join('; '),
          ...(form ? { 'content-type': 'application/x-www-form-urlencoded' } : {}),
        },
        body: form ? new URLSearchParams(form).toString() : undefined,
      });
      for (const c of res.headers.getSetCookie()) {
        const [pair] = c.split(';');
        const i = pair.indexOf('=');
        if (pair.slice(i + 1)) jar.set(pair.slice(0, i), pair.slice(i + 1));
        else jar.delete(pair.slice(0, i));
      }
      return { status: res.status, location: res.headers.get('location'), text: await res.text() };
    }
    const b = {
      get: (path) => request('GET', path),
      // Fetches a page for its form token first, the way a person would have the form open.
      async post(path, form = {}, from = '/login') {
        const page = await request('GET', from);
        const token = (page.text.match(/name="_csrf" value="([^"]+)"/) || [])[1];
        return request('POST', path, { _csrf: token, ...form });
      },
      rawPost: (path, form) => request('POST', path, form),
    };
    return b;
  }

  async function api(path, body) {
    const res = await fetch(`${base}/api/v1${path}`, {
      method: body ? 'POST' : 'GET',
      headers: { 'content-type': 'application/json' },
      body: body ? JSON.stringify(body) : undefined,
    });
    return { status: res.status, body: await res.json() };
  }

  async function webhook(event, { secret = WEBHOOK_SECRET } = {}) {
    const payload = JSON.stringify(event);
    const res = await fetch(`${base}/webhooks/stripe`, {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'stripe-signature': stripe.webhooks.generateTestHeaderString({ payload, secret }) },
      body: payload,
    });
    return res.status;
  }

  // The link in the most recent email to an address.
  function emailLink(to) {
    const message = [...mailer.outbox].reverse().find((m) => m.to[0] === to);
    const url = message && (message.text.match(/http:\/\/site\.test(\/\S+)/) || [])[1];
    return { subject: message && message.subject, path: url };
  }

  // Sign up, confirm the email and stay signed in.
  async function newAccount(email, password = 'correct horse battery') {
    const b = browser();
    await b.post('/signup', { email, password }, '/signup');
    await b.get(emailLink(email).path);
    const id = (await db.query('SELECT id FROM users WHERE email = $1', [email])).rows[0].id;
    return { b, id, email, password };
  }

  // What Stripe reports once a checkout this site started has been paid.
  function paidSession(id, userId, extra = {}) {
    const session = {
      id,
      object: 'checkout.session',
      mode: 'payment',
      payment_status: 'paid',
      client_reference_id: String(userId),
      metadata: { user_id: String(userId), product: 'pointerturn-licence' },
      payment_intent: `pi_${id}`,
      subscription: null,
      ...extra,
    };
    stripe.state.sessions[id] = session;
    return session;
  }

  const stop = async () => {
    await new Promise((resolve) => server.close(resolve));
    await db.end();
  };

  return { db, config, stripe, mailer, port: server.address().port, browser, api, webhook, emailLink, newAccount, paidSession, stop };
}

module.exports = { start };
