-- A record of where accounts, purchases and activations came from, for the countries
-- PointerPath is restricted to (ALLOWED_COUNTRIES). Country codes are two letters.
ALTER TABLE users
  ADD COLUMN signup_country text,
  -- When the person confirmed, at sign-up, that they are in an allowed country.
  ADD COLUMN region_declared_at timestamptz;

ALTER TABLE licences
  -- Where the buyer's connection was when they started the checkout.
  ADD COLUMN purchase_country text,
  -- The country of the billing address they gave Stripe.
  ADD COLUMN billing_country text;

-- Where the computer's connection was when it last fetched a licence.
ALTER TABLE activations ADD COLUMN country text;
