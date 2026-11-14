CREATE TABLE users (
  id                 bigserial PRIMARY KEY,
  email              text NOT NULL,
  password_hash      text NOT NULL,
  email_verified_at  timestamptz,
  stripe_customer_id text UNIQUE,
  created_at         timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX users_email_lower ON users (lower(email));

-- One-time links sent by email: confirm an address, or reset a password.
CREATE TABLE email_tokens (
  id         bigserial PRIMARY KEY,
  user_id    bigint NOT NULL REFERENCES users ON DELETE CASCADE,
  kind       text NOT NULL CHECK (kind IN ('verify', 'reset')),
  token_hash text NOT NULL UNIQUE,
  expires_at timestamptz NOT NULL,
  used_at    timestamptz,
  created_at timestamptz NOT NULL DEFAULT now()
);

-- A licence is what a purchase gives an account. A subscription follows its Stripe
-- subscription; a perpetual licence (a one-off payment) stays active unless refunded.
CREATE TABLE licences (
  id                         bigserial PRIMARY KEY,
  user_id                    bigint NOT NULL REFERENCES users ON DELETE CASCADE,
  kind                       text NOT NULL CHECK (kind IN ('subscription', 'perpetual')),
  status                     text NOT NULL CHECK (status IN ('active', 'past_due', 'cancelled', 'refunded')),
  max_machines               integer NOT NULL,
  stripe_checkout_session_id text UNIQUE,
  stripe_subscription_id     text UNIQUE,
  stripe_payment_intent_id   text UNIQUE,
  current_period_end         timestamptz,
  created_at                 timestamptz NOT NULL DEFAULT now(),
  updated_at                 timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX licences_user ON licences (user_id);

-- A computer the add-in has been signed in on. The add-in holds the activation token
-- (only its hash is kept here) and trades it for a fresh signed licence.
CREATE TABLE activations (
  id           bigserial PRIMARY KEY,
  licence_id   bigint NOT NULL REFERENCES licences ON DELETE CASCADE,
  machine_id   text NOT NULL,
  machine_name text NOT NULL DEFAULT '',
  token_hash   text NOT NULL UNIQUE,
  created_at   timestamptz NOT NULL DEFAULT now(),
  last_seen_at timestamptz NOT NULL DEFAULT now(),
  revoked_at   timestamptz
);
CREATE UNIQUE INDEX activations_live ON activations (licence_id, machine_id) WHERE revoked_at IS NULL;

-- Sign-in sessions (the table connect-pg-simple expects).
CREATE TABLE session (
  sid    varchar NOT NULL PRIMARY KEY,
  sess   json NOT NULL,
  expire timestamp(6) NOT NULL
);
CREATE INDEX session_expire ON session (expire);
