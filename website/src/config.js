'use strict';

// Everything the site reads from the environment, in one place. See .env.example.

function int(value, fallback) {
  const n = Number.parseInt(value, 10);
  return Number.isFinite(n) ? n : fallback;
}

function loadConfig(env = process.env) {
  const port = int(env.PORT, 3000);
  const isProduction = env.NODE_ENV === 'production';
  const baseUrl = (env.BASE_URL || env.RENDER_EXTERNAL_URL || `http://localhost:${port}`).replace(/\/+$/, '');

  const config = {
    port,
    isProduction,
    baseUrl,
    databaseUrl: env.DATABASE_URL || '',
    sessionSecret: env.SESSION_SECRET || '',
    rateLimit: env.DISABLE_RATE_LIMIT !== '1',
    // How many proxies of the hosting platform sit in front of the app (for the visitor's IP address).
    trustProxyHops: int(env.TRUST_PROXY_HOPS, 1),

    stripe: {
      secretKey: env.STRIPE_SECRET_KEY || '',
      webhookSecret: env.STRIPE_WEBHOOK_SECRET || '',
      priceId: env.STRIPE_PRICE_ID || '',
      automaticTax: env.STRIPE_AUTOMATIC_TAX === '1',
    },

    resend: {
      apiKey: env.RESEND_API_KEY || '',
      from: env.EMAIL_FROM || 'PointerTurn <onboarding@resend.dev>',
    },

    plausible: {
      // New-style Plausible snippet: the site-specific script URL from the Plausible dashboard.
      scriptUrl: env.PLAUSIBLE_SCRIPT_URL || '',
      // Old-style snippet: just the site's domain.
      domain: env.PLAUSIBLE_DOMAIN || '',
    },

    region: {
      // Two-letter country codes PointerTurn is offered in, e.g. "AU". Empty: no restriction.
      allowed: (env.ALLOWED_COUNTRIES || '')
        .split(',')
        .map((c) => c.trim().toUpperCase())
        .filter(Boolean),
      // A header the hosting platform sets to the visitor's real IP address, if it has one
      // that visitors can't forge. Empty: the address Express works out (req.ip).
      clientIpHeader: (env.CLIENT_IP_HEADER || '').trim().toLowerCase(),
      // Log how each request's country was worked out, to check the above after deploying.
      debug: env.GEO_DEBUG === '1',
    },

    installer: {
      url: env.INSTALLER_URL || '',
      version: env.INSTALLER_VERSION || '',
    },

    licence: {
      // EC P-256 private key, PEM or base64 of the PEM. `npm run licence-key` makes one.
      signingKey: env.LICENCE_SIGNING_KEY || '',
      // How long a signed licence works without the add-in reaching the server again.
      tokenDays: int(env.LICENCE_TOKEN_DAYS, 14),
      // How many computers one licence can be active on at a time.
      maxMachines: int(env.LICENCE_MAX_MACHINES, 2),
    },
  };

  if (isProduction) {
    const missing = [];
    if (!config.databaseUrl) missing.push('DATABASE_URL');
    if (!config.sessionSecret) missing.push('SESSION_SECRET');
    if (!config.licence.signingKey) missing.push('LICENCE_SIGNING_KEY');
    if (missing.length) throw new Error(`Missing required environment variables: ${missing.join(', ')}`);
  }
  if (!config.sessionSecret) config.sessionSecret = 'dev-only-session-secret';

  return config;
}

module.exports = { loadConfig };
