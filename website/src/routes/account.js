'use strict';

const express = require('express');
const { requireLogin, requireRegion } = require('./site');

module.exports = function account({ config, stripe, licences, billing, log }) {
  const router = express.Router();

  router.get('/account', requireLogin, async (req, res) => {
    const licence = await licences.latestLicence(req.user.id);
    const usable = Boolean(licence && licences.USABLE.includes(licence.status));
    if (req.query.purchased === '1' && usable) res.locals.pageEvent = 'Purchase';
    res.render('account', {
      licence,
      usable,
      machines: usable ? await licences.machines(licence.id) : [],
      price: usable ? null : await billing.getPrice(),
      billingEnabled: billing.enabled,
      installer: config.installer,
    });
  });

  // "Buy" links from the public pages land here: sign in first, then the account page.
  router.get('/buy', requireLogin, (req, res) => res.redirect('/account#licence'));

  router.post('/buy', requireLogin, requireRegion, async (req, res) => {
    if (!billing.enabled) {
      req.session.flash = 'Buying a licence is not available yet.';
      return res.redirect('/account');
    }
    if (!req.user.email_verified_at) {
      req.session.flash = 'Confirm your email address before buying a licence.';
      return res.redirect('/account');
    }
    if (await licences.usableLicence(req.user.id)) {
      req.session.flash = 'This account already has a licence.';
      return res.redirect('/account');
    }
    let checkout;
    try {
      checkout = await billing.createCheckout(req.user, { country: req.region.country });
    } catch (err) {
      // Stripe refused, or is set up wrongly: say so on the account page rather than an error page.
      log.error('[billing] could not start a checkout:', err.message);
      req.session.flash = 'The checkout could not be started. Nothing has been charged. Try again in a moment.';
      return res.redirect('/account');
    }
    res.redirect(303, checkout);
  });

  // Where Stripe sends the buyer after paying. The webhook usually gets there first; this
  // covers the case where it has not arrived yet.
  router.get('/checkout/return', requireLogin, async (req, res) => {
    const id = String(req.query.session_id || '');
    if (stripe && /^cs_[A-Za-z0-9_]+$/.test(id)) {
      const session = await stripe.checkout.sessions.retrieve(id);
      if (String(session.client_reference_id) === String(req.user.id)) await billing.fulfilCheckout(session);
    }
    if (await licences.usableLicence(req.user.id)) {
      req.session.flash = 'Thanks. Your licence is active.';
      return res.redirect('/account?purchased=1');
    }
    req.session.flash = 'Your payment is still being confirmed. This page will show your licence once it is.';
    res.redirect('/account');
  });

  // Stripe's own pages for invoices, the payment card and cancelling.
  router.post('/billing', requireLogin, async (req, res) => {
    if (!stripe || !req.user.stripe_customer_id) return res.redirect('/account');
    res.redirect(303, await billing.createPortal(req.user));
  });

  router.post('/account/machines/:id/deactivate', requireLogin, async (req, res) => {
    if (/^\d+$/.test(req.params.id)) {
      await licences.deactivateForUser(req.user.id, req.params.id);
      req.session.flash = 'That computer has been signed out of PointerPath.';
    }
    res.redirect('/account');
  });

  return router;
};
