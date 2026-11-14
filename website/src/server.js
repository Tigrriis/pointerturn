'use strict';

const Stripe = require('stripe');
const { loadConfig } = require('./config');
const { createPool, migrate } = require('./db');
const { createMailer } = require('./mail');
const { createApp } = require('./app');

async function main() {
  const config = loadConfig();
  if (!config.databaseUrl) throw new Error('DATABASE_URL is not set. See .env.example.');

  const db = createPool(config.databaseUrl);
  await migrate(db);

  const stripe = config.stripe.secretKey ? new Stripe(config.stripe.secretKey) : null;
  if (!stripe) console.warn('[billing] STRIPE_SECRET_KEY is not set: buying a licence is switched off.');
  if (!config.resend.apiKey) console.warn('[mail] RESEND_API_KEY is not set: emails are printed here instead of sent.');

  const app = createApp({ config, db, stripe, mailer: createMailer(config) });
  const server = app.listen(config.port, () => console.log(`PointerTurn website listening on ${config.baseUrl}`));

  const stop = () => server.close(() => db.end().finally(() => process.exit(0)));
  process.on('SIGTERM', stop);
  process.on('SIGINT', stop);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
