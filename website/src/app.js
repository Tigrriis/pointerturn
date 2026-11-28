'use strict';

const path = require('node:path');
const express = require('express');
const session = require('express-session');
const PgSession = require('connect-pg-simple')(session);
const helmet = require('helmet');
const { rateLimit } = require('express-rate-limit');

const { csrf } = require('./security');
const { createLicences } = require('./licences');
const { createBilling } = require('./billing');
const { createGeo } = require('./geo');

function origin(url) {
  try {
    return new URL(url).origin;
  } catch {
    return null;
  }
}

// Builds the Express app. Everything it depends on is passed in, so tests can run it
// against a test database with a stand-in Stripe client and mailer.
function createApp({ config, db, stripe, mailer, log = console }) {
  const licences = createLicences({ db, config });
  const billing = createBilling({ stripe, db, config, mailer, log });
  const geo = createGeo(config, { log });
  geo.warm();
  const ctx = { config, db, stripe, mailer, licences, billing, geo, log };

  // Added to stylesheet and script addresses. Browsers keep those files for an hour, so
  // without it a new deploy's pages are shown with the previous deploy's styles.
  const assets = `?v=${(process.env.RENDER_GIT_COMMIT || String(Date.now())).slice(0, 10)}`;

  const app = express();
  app.set('trust proxy', config.trustProxyHops); // Render terminates TLS in front of the app
  app.set('view engine', 'ejs');
  app.set('views', path.join(__dirname, 'views'));
  app.disable('x-powered-by');

  const plausibleOrigin = config.plausible.scriptUrl
    ? origin(config.plausible.scriptUrl)
    : config.plausible.domain
      ? 'https://plausible.io'
      : null;
  app.use(
    helmet({
      contentSecurityPolicy: {
        directives: {
          defaultSrc: ["'self'"],
          scriptSrc: ["'self'", ...(plausibleOrigin ? [plausibleOrigin] : [])],
          connectSrc: ["'self'", ...(plausibleOrigin ? [plausibleOrigin] : [])],
          styleSrc: ["'self'", 'https://fonts.googleapis.com'],
          fontSrc: ["'self'", 'https://fonts.gstatic.com'],
          imgSrc: ["'self'", 'data:'],
          // Buying and "Manage billing" are form posts that redirect to Stripe.
          formAction: ["'self'", 'https://checkout.stripe.com', 'https://billing.stripe.com'],
          frameAncestors: ["'none'"],
          upgradeInsecureRequests: config.isProduction ? [] : null,
        },
      },
      strictTransportSecurity: config.isProduction,
    })
  );

  app.get('/healthz', async (req, res) => {
    try {
      await db.query('SELECT 1');
      res.json({ ok: true });
    } catch {
      res.status(503).json({ ok: false });
    }
  });

  app.use(express.static(path.join(__dirname, '..', 'public'), { maxAge: config.isProduction ? '1h' : 0 }));

  const limiter = (limit) =>
    config.rateLimit
      ? rateLimit({ windowMs: 15 * 60 * 1000, limit, standardHeaders: 'draft-7', legacyHeaders: false })
      : (req, res, next) => next();
  ctx.authLimiter = limiter(30);

  // Stripe signs the raw request body, so its webhook is mounted before any body parsing.
  app.use('/webhooks', require('./routes/webhooks')(ctx));

  // The licence API the add-in calls: JSON, no cookies.
  app.use(
    '/api/v1',
    limiter(60),
    express.json({ limit: '10kb' }),
    (req, res, next) => {
      req.region = geo.check(req);
      next();
    },
    require('./routes/api')(ctx)
  );

  app.use(express.urlencoded({ extended: false, limit: '10kb' }));
  app.use(
    session({
      name: 'pt.sid',
      store: new PgSession({ pool: db, tableName: 'session' }),
      secret: config.sessionSecret,
      resave: false,
      saveUninitialized: false,
      cookie: { httpOnly: true, sameSite: 'lax', secure: config.isProduction, maxAge: 30 * 24 * 60 * 60 * 1000 },
    })
  );

  // Who is signed in, and what every page needs.
  app.use(async (req, res, next) => {
    try {
      req.user = null;
      if (req.session.userId) {
        req.user = (await db.query('SELECT * FROM users WHERE id = $1', [req.session.userId])).rows[0] || null;
        if (!req.user) delete req.session.userId;
      }
      res.locals.user = req.user;
      res.locals.plausible = config.plausible;
      res.locals.assets = assets;
      res.locals.path = req.path;
      res.locals.pageEvent = null;
      // Where the visitor is, when PointerPath is restricted to some countries.
      req.region = geo.check(req);
      res.locals.region = { restricted: geo.restricted, where: geo.where, blocked: !req.region.allowed };
      res.locals.flash = req.session.flash || null;
      delete req.session.flash;
      next();
    } catch (err) {
      next(err);
    }
  });
  app.use(csrf());

  app.use(require('./routes/site')(ctx));
  app.use(require('./routes/auth')(ctx));
  app.use(require('./routes/account')(ctx));

  app.use((req, res) => {
    res.status(404).render('message', { title: 'Page not found', body: 'There is nothing at this address.' });
  });

  // eslint-disable-next-line no-unused-vars
  app.use((err, req, res, next) => {
    log.error(err);
    if (res.headersSent) return;
    if (req.originalUrl.startsWith('/api/')) return res.status(500).json({ error: 'server_error' });
    res.status(500);
    if (!res.locals.csrfToken) {
      // Failed before the page helpers were set up.
      Object.assign(res.locals, { user: null, plausible: config.plausible, assets, path: req.path, pageEvent: null, flash: null, csrfToken: '' });
      res.locals.region = { restricted: geo.restricted, where: geo.where, blocked: false };
    }
    res.render('message', { title: 'Something went wrong', body: 'That did not work. Try again in a moment.' });
  });

  return app;
}

module.exports = { createApp };
