'use strict';

const express = require('express');

// Sends signed-out visitors to sign in, then back to where they were going.
function requireLogin(req, res, next) {
  if (req.user) return next();
  if (req.method === 'GET') req.session.returnTo = req.originalUrl;
  res.redirect('/login');
}

module.exports = function site({ config, billing }) {
  const router = express.Router();

  router.get('/', async (req, res) => {
    res.render('home', { price: await billing.getPrice() });
  });

  // The installer itself is hosted elsewhere (INSTALLER_URL); this hands it to signed-in users.
  router.get('/download', requireLogin, (req, res) => {
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
