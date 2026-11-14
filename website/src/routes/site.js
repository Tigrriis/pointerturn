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

module.exports = function site({ config, billing }) {
  const router = express.Router();

  router.get('/', async (req, res) => {
    res.render('home', { price: await billing.getPrice() });
  });

  // The installer itself is hosted elsewhere (INSTALLER_URL); this hands it to signed-in users.
  router.get('/download', requireLogin, requireRegion, (req, res) => {
    if (!config.installer.url) {
      return res.status(503).render('message', {
        title: 'The installer is not available yet',
        body: 'The download has not been published. Check back soon.',
      });
    }
    res.redirect(302, config.installer.url);
  });

  return router;
};

module.exports.requireLogin = requireLogin;
module.exports.requireRegion = requireRegion;
