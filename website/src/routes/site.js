'use strict';

const express = require('express');

// Sends signed-out visitors to sign in, then back to where they were going.
function requireLogin(req, res, next) {
  if (req.user) return next();
  if (req.method === 'GET') req.session.returnTo = req.originalUrl;
  res.redirect('/login');
}

// Refuses visitors outside the countries PointerPath is offered in (ALLOWED_COUNTRIES).
function requireRegion(req, res, next) {
  if (req.region.allowed) return next();
  res.status(403).render('message', {
    title: `PointerPath is available in ${res.locals.region.where} only`,
    body:
      `Accounts, licences and downloads are offered only to people in ${res.locals.region.where}, and you appear to be somewhere else. ` +
      `If you are in ${res.locals.region.where}, check whether a VPN is making it look otherwise.`,
  });
}

module.exports = function site({ config, billing, downloads }) {
  const router = express.Router();

  router.get('/', async (req, res) => {
    res.render('home', { price: await billing.getPrice() });
  });

  // Terms of use, privacy policy and refund policy. Open to everyone, wherever they are.
  const policies = { terms: 'Terms of use', privacy: 'Privacy policy', refunds: 'Refund policy' };
  for (const [page, title] of Object.entries(policies)) {
    router.get(`/${page}`, (req, res) => {
      res.render(`legal/${page}`, { title, legal: config.legal, offlineDays: config.licence.tokenDays, site: new URL(config.baseUrl).host });
    });
  }

  // The installer itself is hosted elsewhere; this hands it to signed-in users. From a
  // private bucket, each visitor gets a link of their own that works for a few minutes.
  router.get('/download', requireLogin, requireRegion, (req, res) => {
    const link = downloads.link();
    if (!link) {
      return res.status(503).render('message', {
        title: 'The installer is not available yet',
        body: 'The download has not been published. Check back soon.',
      });
    }
    res.set('Cache-Control', 'no-store');
    res.redirect(302, link);
  });

  return router;
};

module.exports.requireLogin = requireLogin;
module.exports.requireRegion = requireRegion;
