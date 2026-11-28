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
      // The final release price, in dollars, to show beside the lower beta price that Stripe
      // charges. Display only: nothing is charged from it.
      priceFull: Number.parseFloat(env.PRICE_WAS) || 0,
    },

    resend: {
      apiKey: env.RESEND_API_KEY || '',
      from: env.EMAIL_FROM || 'PointerPath <onboarding@resend.dev>',
    },

    plausible: {
      // New-style Plausible snippet: the site-specific script URL from the Plausible dashboard.
      scriptUrl: env.PLAUSIBLE_SCRIPT_URL || '',
      // Old-style snippet: just the site's domain.
      domain: env.PLAUSIBLE_DOMAIN || '',
    },

    legal: {
      // Who the terms, privacy and refund pages say sells PointerPath, and how to reach them.
      name: (env.LEGAL_NAME || 'PointerPath').trim(),
      // Shown beside the name once set, e.g. "12 345 678 901".
      abn: (env.LEGAL_ABN || '').trim(),
      email: (env.CONTACT_EMAIL || 'hello@pointerpath.app').trim(),
    },

    reddit: {
      // The pixel ID from Reddit Ads, for measuring what its ads lead to. Not a secret: it
      // is in every page. Empty: no Reddit code is loaded at all.
      pixelId: /^[A-Za-z0-9_-]{3,40}$/.test((env.REDDIT_PIXEL_ID || '').trim()) ? env.REDDIT_PIXEL_ID.trim() : '',
    },

    region: {
      // Two-letter country codes PointerPath is offered in, e.g. "AU". Empty: no restriction.
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
      version: env.INSTALLER_VERSION || '',
      // Either a public address for the file...
      url: env.INSTALLER_URL || '',
      // ...or its name in a private bucket (Cloudflare R2, or anything that speaks S3), from
      // which the site hands out links that work for a few minutes.
      file: env.INSTALLER_FILE || '',
      bucket: {
        endpoint: (env.R2_ENDPOINT || '').replace(/\/+$/, ''),
        name: env.R2_BUCKET || '',
        region: env.R2_REGION || 'auto',
        accessKeyId: env.R2_ACCESS_KEY_ID || '',
        secretAccessKey: env.R2_SECRET_ACCESS_KEY || '',
      },
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
