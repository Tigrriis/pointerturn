# PointerTurn website

The public site for PointerTurn: it describes the product, sells one licence through Stripe,
hands out the installer, and runs the licence API the add-in signs in to.

- **Accounts.** Sign up with an email and password, confirm the address by email (Resend),
  reset a forgotten password.
- **Buying.** One product, one price, paid through Stripe Checkout. The price shown on the
  site is read from Stripe, so the two can't disagree.
- **Licences.** A paid checkout gives the account a licence. A one-off Stripe price sells a
  licence that doesn't expire; a recurring price sells a subscription that follows Stripe
  (renewals, failed payments, cancelling). Which one you sell is decided by the Stripe price,
  not by this code.
- **Activation.** The add-in signs in with the account's email and password and gets a signed
  licence back that it can check without contacting the server. See [Licence API](#licence-api).
- **Download.** Signed-in users are sent to the installer, wherever it is hosted.
- **Analytics.** Plausible, with three goals: `Signup`, `Purchase` and `Download`.

It is a Node.js (Express) app with server-rendered pages and a Postgres database. The look comes
from `../brand`; the files it needs are copied into `public/brand`.

## Running it locally

Needs Node 20 or later and a Postgres database.

```bash
cd website
npm install
cp .env.example .env    # then set DATABASE_URL
node --env-file=.env src/server.js
```

The database tables are created on first start. With no Stripe key the site runs with buying
switched off; with no Resend key, emails (including the confirmation links) are printed in the
terminal instead of sent.

To run the tests, point `TEST_DATABASE_URL` at a database that can be wiped:

```bash
TEST_DATABASE_URL=postgres://postgres@localhost:5432/pointerturn_test npm test
```

They drive the real app over HTTP: sign-up, sign-in, password reset, buying (with a stand-in for
Stripe's network calls, but real webhook signatures), and the licence API.

## Settings

All settings are environment variables. `.env.example` lists them with comments.

| Variable | Needed | What it is |
|---|---|---|
| `DATABASE_URL` | Yes | Postgres connection string |
| `SESSION_SECRET` | Yes | Random string that signs the sign-in cookie |
| `LICENCE_SIGNING_KEY` | In production | The private key that signs licences. Make one with `npm run licence-key` |
| `BASE_URL` | Once you have a domain | The site's public address, used in emails and Stripe return links. On Render it defaults to the service's `onrender.com` address |
| `STRIPE_SECRET_KEY`, `STRIPE_PRICE_ID`, `STRIPE_WEBHOOK_SECRET` | To sell | Stripe API key, the licence's Price ID, and the webhook's signing secret |
| `STRIPE_AUTOMATIC_TAX` | No | `1` to have Stripe Tax work out GST at checkout |
| `RESEND_API_KEY`, `EMAIL_FROM` | To send email | Resend API key and the From address (on a domain verified in Resend) |
| `PLAUSIBLE_SCRIPT_URL` or `PLAUSIBLE_DOMAIN` | No | The script address from Plausible's installation page, or (older snippet) the site's domain |
| `ALLOWED_COUNTRIES` | No | Two-letter country codes PointerTurn is offered in, e.g. `AU`. See [Restricting to a country](#restricting-to-a-country) |
| `INSTALLER_URL`, `INSTALLER_VERSION` | To offer the download | Where the installer is hosted, and the version shown beside the button |
| `LICENCE_MAX_MACHINES` | No | Computers one licence can be active on at once. Default 2. Applies to licences sold after it is changed |
| `LICENCE_TOKEN_DAYS` | No | How long a signed licence works before the add-in must reach the server again. Default 14 |

## Deploying on Render

`render.yaml` in the repository root describes the web service and its database. In Render,
choose **New > Blueprint**, pick this repository, and fill in the values it asks for. It uses
paid plans: the free web plan sleeps when idle and the free database is deleted after 30 days.

What to set up in the other services:

1. **Licence key.** Run `npm run licence-key` once. Put the private key in `LICENCE_SIGNING_KEY`
   and keep the public key for the add-in. Changing the key later invalidates every licence
   already issued until each computer next reaches the server.
2. **Stripe.** Create one product with one price and put the price's ID in `STRIPE_PRICE_ID`.
   Add a webhook endpoint at `https://<your site>/webhooks/stripe` for these events, and put
   its signing secret in `STRIPE_WEBHOOK_SECRET`:
   - `checkout.session.completed`
   - `checkout.session.async_payment_succeeded`
   - `customer.subscription.updated`
   - `customer.subscription.deleted`
   - `charge.refunded`

   Turn on the customer portal in Stripe's settings so "Invoices and billing" works.
3. **Resend.** Verify your sending domain, then set `RESEND_API_KEY` and `EMAIL_FROM`.
4. **Plausible.** Add the site, set `PLAUSIBLE_SCRIPT_URL`, and add the goals `Signup`,
   `Purchase` and `Download`.
5. **Installer.** Upload `PointerTurn-<version>-setup.exe` somewhere a browser can download it
   from (a public release, or a storage bucket) and set `INSTALLER_URL`.

## Restricting to a country

With `ALLOWED_COUNTRIES` set (`AU` in `render.yaml`), PointerTurn is offered only there:

- **The site** can still be read from anywhere, but from outside the buy buttons are gone and a
  notice says where it is available. Signing up, buying and downloading are refused.
- **Signing up** needs a ticked declaration: "I am in Australia and will use PointerTurn only in
  Australia". When it was ticked, and the country, are kept with the account.
- **Buying** has Stripe collect the billing address. The country the buyer's connection was in
  and the billing address's country are kept with the licence.
- **The add-in** can be activated only from inside. From outside, a computer already signed in is
  not given a fresh licence, so it stops when the one it holds runs out (`LICENCE_TOKEN_DAYS`,
  14 by default): a short trip abroad doesn't cut anyone off, a move does. Signing out works
  from anywhere.

The country comes from the visitor's IP address, looked up in a database bundled with the app
(`@ip-location-db/geo-whois-asn-country`, public-domain registry data). Nothing is sent to an
outside service. An address that can't be placed counts as outside. Update the package now and
then (`npm update @ip-location-db/geo-whois-asn-country`), as address allocations change.

What this does not do:

- It does not stop someone using a VPN with an Australian exit, and it will refuse an Australian
  office whose internet connection leaves through another country.
- It does not check the card. A billing address is whatever the buyer types. To refuse cards
  issued outside Australia, add a rule in Stripe Radar; that is set in Stripe, not here.
- It is not a licence term. The terms of sale need to say the licence is for use in Australia.

### Checking it after the first deploy

The lookup is only as good as the IP address the app is given, and that depends on the hosting
platform's proxies. `render.yaml` sets `GEO_DEBUG=1`, which logs, for each request, the address
used, the country found, and the headers the platform sent. Open the site once from a known
place and read the log:

- If `ip=` is your own address and the country is right, remove `GEO_DEBUG`. Nothing else to do.
- If `ip=` is a proxy's address and one of the headers logged (`cf-connecting-ip`, say) has
  yours, set `CLIENT_IP_HEADER` to that header's name. Only do this for a header the platform
  sets itself and visitors can't forge.
- If your address is in `x-forwarded-for` but not last, raise `TRUST_PROXY_HOPS`.

## Licence API

What the add-in calls. The add-in's side of this is described in
[docs/licensing.md](../docs/licensing.md). JSON in, JSON out, under `/api/v1`. Errors come back as
`{"error": "<code>"}` with the HTTP status shown.

### `POST /api/v1/activate`

Signs a computer in.

```json
{ "email": "…", "password": "…", "machine_id": "…", "machine_name": "DESIGN-PC-04" }
```

`machine_id` is any stable identifier for the computer (a hash of the Windows machine GUID, say);
`machine_name` is what the user sees on their account page. The reply:

```json
{
  "activation_token": "…",
  "licence_token": "<signed licence>",
  "licence_token_expires_at": "2026-11-28T04:00:00.000Z",
  "licence": { "id": "12", "kind": "perpetual", "status": "active", "current_period_end": null }
}
```

The add-in keeps both tokens and discards the password. Activating a computer that is already
signed in keeps its place and replaces its activation token.

| Status | `error` | Meaning |
|---|---|---|
| 400 | `invalid_request` | A field is missing |
| 401 | `invalid_credentials` | Wrong email or password |
| 403 | `email_not_verified` | The account's email address hasn't been confirmed |
| 403 | `region_not_allowed` | The request came from outside the countries PointerTurn is offered in |
| 402 | `no_licence` | The account has no active licence |
| 409 | `machine_limit` | The licence is in use on its maximum number of computers. The reply lists them in `machines`; the user frees one on their account page |
| 429 | | Too many attempts; try later |

### `POST /api/v1/refresh`

`{ "activation_token": "…" }` returns a fresh `licence_token`. Call it when online, well before
the current one expires (daily is plenty). `401 invalid_activation` means the computer was signed
out; `402 licence_inactive` means the licence ended (cancelled, refunded); `403 region_not_allowed`
means no new licence is issued where the computer is, and the one it holds stands until it expires.

### `POST /api/v1/deactivate`

`{ "activation_token": "…" }` signs the computer out and frees its place.

### `GET /api/v1/licence-key`

The public key licences are signed with, as PEM and JWK, with its key ID.

### Checking a licence in the add-in

`licence_token` is a JWT signed with ES256 (ECDSA, P-256, SHA-256; the signature is the 64-byte
`r‖s` form that .NET's `ECDsa.VerifyData` expects). The add-in, with the public key built in:

1. Splits the token on `.`, and verifies the signature over `header.payload`.
2. Checks `iss` is `pointerturn`, `mid` is this computer's `machine_id`, and `exp` is in the future.

A licence that passes lets PointerTurn run with no connection until `exp`. If the licence has
ended, `refresh` is refused and the program stops working when the last signed licence expires:
at most `LICENCE_TOKEN_DAYS` after the cancellation or refund.

Claims: `iss`, `sub` (account ID), `email`, `lic` (licence ID), `kind` (`perpetual` or
`subscription`), `mid`, `iat`, `exp`.

## Layout

```
src/server.js          Starts the app: settings, database, migrations
src/app.js             The Express app: security headers, sessions, routes
src/routes/            site (home, download), auth, account (buying, computers), api, webhooks
src/licences.js        Licences, activations and signing
src/billing.js         Stripe: price, checkout, portal, webhook events
src/mail.js            Emails through Resend
src/migrations/        Database schema
src/views/, public/    Pages, styles, brand files
test/                  End-to-end tests
```
