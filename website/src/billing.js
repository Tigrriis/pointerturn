'use strict';

// Everything that talks to Stripe: showing the price, starting a checkout, the customer
// portal, and turning Stripe's events into licences.

const PRICE_CACHE_MS = 10 * 60 * 1000;
// Marks the checkouts this site started, so other sales on the same Stripe account are ignored.
const PRODUCT = 'pointerturn-licence';

function licenceStatusFor(subscriptionStatus) {
  if (subscriptionStatus === 'active' || subscriptionStatus === 'trialing') return 'active';
  if (subscriptionStatus === 'past_due') return 'past_due';
  return 'cancelled';
}

// Stripe moved the period end from the subscription onto its items in 2025 API versions.
function periodEnd(subscription) {
  const seconds = subscription.current_period_end ?? subscription.items?.data?.[0]?.current_period_end;
  return seconds ? new Date(seconds * 1000) : null;
}

const idOf = (value) => (value && typeof value === 'object' ? value.id : value) || null;

function createBilling({ stripe, db, config, mailer, log = console }) {
  const enabled = Boolean(stripe && config.stripe.priceId);
  let cached = null;

  // The price as set in Stripe, so the site never shows a different figure from the checkout.
  async function getPrice() {
    if (!enabled) return null;
    if (cached && Date.now() - cached.at < PRICE_CACHE_MS) return cached.price;
    try {
      const p = await stripe.prices.retrieve(config.stripe.priceId);
      const amount = new Intl.NumberFormat('en-AU', {
        style: 'currency',
        currency: p.currency.toUpperCase(),
        minimumFractionDigits: p.unit_amount % 100 === 0 ? 0 : 2,
      }).format(p.unit_amount / 100);
      const price = {
        amount,
        currency: p.currency.toUpperCase(),
        recurring: p.type === 'recurring',
        interval: p.recurring ? p.recurring.interval : null,
        tax: p.tax_behavior === 'exclusive' ? 'plus GST' : p.tax_behavior === 'inclusive' ? 'including GST' : '',
      };
      cached = { at: Date.now(), price };
      return price;
    } catch (err) {
      log.error('[billing] could not read the price from Stripe:', err.message);
      return cached ? cached.price : null;
    }
  }

  async function customerFor(user) {
    if (user.stripe_customer_id) return user.stripe_customer_id;
    const customer = await stripe.customers.create({ email: user.email, metadata: { user_id: String(user.id) } });
    await db.query('UPDATE users SET stripe_customer_id = $1 WHERE id = $2', [customer.id, user.id]);
    return customer.id;
  }

  // `country` is where the buyer's connection is, when PointerTurn is restricted to some countries.
  async function createCheckout(user, { country = null } = {}) {
    const price = await stripe.prices.retrieve(config.stripe.priceId);
    const recurring = price.type === 'recurring';
    const metadata = { user_id: String(user.id), product: PRODUCT, ...(country ? { ip_country: country } : {}) };
    const session = await stripe.checkout.sessions.create({
      mode: recurring ? 'subscription' : 'payment',
      customer: await customerFor(user),
      line_items: [{ price: config.stripe.priceId, quantity: 1 }],
      client_reference_id: String(user.id),
      metadata,
      // Restricted to some countries: have Stripe collect the billing address, for the record.
      ...(config.region.allowed.length ? { billing_address_collection: 'required' } : {}),
      ...(recurring ? { subscription_data: { metadata } } : { payment_intent_data: { metadata } }),
      ...(config.stripe.automaticTax
        ? { automatic_tax: { enabled: true }, customer_update: { address: 'auto', name: 'auto' }, tax_id_collection: { enabled: true } }
        : {}),
      success_url: `${config.baseUrl}/checkout/return?session_id={CHECKOUT_SESSION_ID}`,
      cancel_url: `${config.baseUrl}/account`,
    });
    return session.url;
  }

  async function createPortal(user) {
    const session = await stripe.billingPortal.sessions.create({
      customer: user.stripe_customer_id,
      return_url: `${config.baseUrl}/account`,
    });
    return session.url;
  }

  // Give the buyer their licence. Called from the webhook and again when the buyer lands
  // back on the site, whichever comes first; the second call changes nothing.
  async function fulfilCheckout(session) {
    if (session.metadata?.product !== PRODUCT) return null;
    if (session.payment_status !== 'paid' && session.payment_status !== 'no_payment_required') return null;
    const userId = Number(session.client_reference_id || session.metadata?.user_id);
    if (!Number.isInteger(userId)) {
      log.error(`[billing] checkout ${session.id} has no account attached; no licence created`);
      return null;
    }

    let kind = 'perpetual';
    let status = 'active';
    let end = null;
    const subscriptionId = idOf(session.subscription);
    if (session.mode === 'subscription' && subscriptionId) {
      const subscription = await stripe.subscriptions.retrieve(subscriptionId);
      kind = 'subscription';
      status = licenceStatusFor(subscription.status);
      end = periodEnd(subscription);
    }

    const { rows } = await db.query(
      `INSERT INTO licences
         (user_id, kind, status, max_machines, stripe_checkout_session_id, stripe_subscription_id, stripe_payment_intent_id,
          current_period_end, purchase_country, billing_country)
       SELECT u.id, $2, $3, $4, $5, $6, $7, $8, $9, $10 FROM users u WHERE u.id = $1
       ON CONFLICT (stripe_checkout_session_id) DO NOTHING
       RETURNING id, (SELECT email FROM users WHERE id = $1) AS email`,
      [
        userId,
        kind,
        status,
        config.licence.maxMachines,
        session.id,
        subscriptionId,
        idOf(session.payment_intent),
        end,
        session.metadata?.ip_country || null,
        session.customer_details?.address?.country || null,
      ]
    );
    if (!rows[0]) return null; // already fulfilled, or the account no longer exists
    mailer.sendLicenceActive(rows[0].email).catch((err) => log.error('[mail] licence email failed:', err.message));
    return rows[0].id;
  }

  async function syncSubscription(subscription) {
    await db.query(
      'UPDATE licences SET status = $1, current_period_end = COALESCE($2, current_period_end), updated_at = now() WHERE stripe_subscription_id = $3',
      [licenceStatusFor(subscription.status), periodEnd(subscription), subscription.id]
    );
  }

  // Every handler here can safely run twice: Stripe redelivers events.
  async function handleEvent(event) {
    const object = event.data.object;
    switch (event.type) {
      case 'checkout.session.completed':
      case 'checkout.session.async_payment_succeeded':
        await fulfilCheckout(object);
        break;
      case 'customer.subscription.updated':
      case 'customer.subscription.deleted':
        await syncSubscription(object);
        break;
      case 'charge.refunded':
        // A full refund of a one-off purchase ends the licence. Subscriptions end when
        // they are cancelled in Stripe.
        if (object.refunded && object.payment_intent) {
          await db.query(
            "UPDATE licences SET status = 'refunded', updated_at = now() WHERE stripe_payment_intent_id = $1 AND kind = 'perpetual'",
            [idOf(object.payment_intent)]
          );
        }
        break;
      default:
        break;
    }
  }

  return { enabled, getPrice, createCheckout, createPortal, fulfilCheckout, handleEvent };
}

module.exports = { createBilling };
