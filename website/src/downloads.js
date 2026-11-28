'use strict';

// Where the installer download comes from.
//
// Either a plain public address (INSTALLER_URL), or a file in a private S3-compatible
// bucket such as Cloudflare R2. For a private bucket the site signs a link that works for
// a few minutes and sends the signed-in visitor to that, so the file can't be fetched by
// someone who has only been given an address.

const crypto = require('node:crypto');

const LINK_SECONDS = 300;

const hmac = (key, text) => crypto.createHmac('sha256', key).update(text).digest();
const sha256 = (text) => crypto.createHash('sha256').update(text).digest('hex');
// URI-encode the way AWS Signature Version 4 wants: everything but A-Z a-z 0-9 - _ . ~
const enc = (text) => encodeURIComponent(text).replace(/[!'()*]/g, (c) => `%${c.charCodeAt(0).toString(16).toUpperCase()}`);

// A presigned GET for one object (AWS Signature Version 4, query-string form).
// `path` is the object's path on `host`, starting with "/". `query` adds parameters such
// as response-content-disposition.
function presign({ host, path, region, accessKeyId, secretAccessKey, expires = LINK_SECONDS, query = {}, now = new Date() }) {
  const amzDate = now.toISOString().replace(/[-:]/g, '').replace(/\.\d{3}/, '');
  const date = amzDate.slice(0, 8);
  const scope = `${date}/${region}/s3/aws4_request`;
  const params = {
    ...query,
    'X-Amz-Algorithm': 'AWS4-HMAC-SHA256',
    'X-Amz-Credential': `${accessKeyId}/${scope}`,
    'X-Amz-Date': amzDate,
    'X-Amz-Expires': String(expires),
    'X-Amz-SignedHeaders': 'host',
  };
  const canonicalQuery = Object.keys(params)
    .sort()
    .map((k) => `${enc(k)}=${enc(params[k])}`)
    .join('&');
  const canonicalPath = path.split('/').map(enc).join('/');
  const canonicalRequest = ['GET', canonicalPath, canonicalQuery, `host:${host}`, '', 'host', 'UNSIGNED-PAYLOAD'].join('\n');
  const toSign = ['AWS4-HMAC-SHA256', amzDate, scope, sha256(canonicalRequest)].join('\n');
  const key = ['s3', 'aws4_request'].reduce(hmac, hmac(hmac(`AWS4${secretAccessKey}`, date), region));
  const signature = hmac(key, toSign).toString('hex');
  return `https://${host}${canonicalPath}?${canonicalQuery}&X-Amz-Signature=${signature}`;
}

function createDownloads(config) {
  const { installer } = config;
  const bucket = installer.bucket;
  const signed = Boolean(bucket.endpoint && bucket.name && bucket.accessKeyId && bucket.secretAccessKey && installer.file);
  const available = signed || Boolean(installer.url);

  // The address to send a visitor to for the installer, or null when none is set up.
  function link() {
    if (signed) {
      const fileName = installer.file.split('/').pop();
      return presign({
        host: new URL(bucket.endpoint).host,
        path: `/${bucket.name}/${installer.file}`,
        region: bucket.region,
        accessKeyId: bucket.accessKeyId,
        secretAccessKey: bucket.secretAccessKey,
        // Saved under its own name, whatever the browser would otherwise call it.
        query: { 'response-content-disposition': `attachment; filename="${fileName}"` },
      });
    }
    return installer.url || null;
  }

  return { available, signed, link };
}

module.exports = { createDownloads, presign };
