'use strict';

const express = require('express');

module.exports = function webhooks({ config, stripe, billing, log }) {
  const router = express.Router();

  router.post('/stripe', express.raw({ type: 'application/json', limit: '1mb' }), async (req, res) => {
    if (!stripe || !config.stripe.webhookSecret) return res.status(503).send('Stripe is not configured');
    let event;
    try {
      event = stripe.webhooks.constructEvent(req.body, req.get('stripe-signature'), config.stripe.webhookSecret);
    } catch (err) {
      return res.status(400).send('Invalid signature');
    }
    try {
      await billing.handleEvent(event);
      res.json({ received: true });
    } catch (err) {
      // A 500 makes Stripe deliver the event again later.
      log.error(`[stripe] handling ${event.type} ${event.id} failed:`, err);
      res.status(500).send('Handler failed');
    }
  });

  return router;
};
