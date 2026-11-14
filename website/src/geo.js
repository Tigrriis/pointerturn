'use strict';

// Which country a request comes from, and whether PointerTurn is offered there.
//
// With ALLOWED_COUNTRIES set (e.g. "AU"), signing up, buying, downloading and activating
// are refused from anywhere else. The country is worked out from the visitor's IP address
// with a database bundled in the app (public-domain registry data), so no request leaves
// the server and nothing about the visitor is sent to anyone.

const fs = require('node:fs');
const net = require('node:net');

const DATA = '@ip-location-db/geo-whois-asn-country/geo-whois-asn-country-';

let tables = null;

// Each file is rows of "first,last,CC" with the addresses as whole numbers, in order.
function loadTables() {
  if (tables) return tables;
  const read = (name) => fs.readFileSync(require.resolve(DATA + name), 'utf8').split('\n').filter(Boolean);

  const v4 = read('ipv4-num.csv');
  const v4Start = new Uint32Array(v4.length);
  const v4End = new Uint32Array(v4.length);
  const v4Country = new Array(v4.length);
  v4.forEach((row, i) => {
    const [a, b, cc] = row.split(',');
    v4Start[i] = Number(a);
    v4End[i] = Number(b);
    v4Country[i] = cc;
  });

  // IPv6 allocations are never finer than the first 64 bits, so that half is enough.
  const v6 = read('ipv6-num.csv');
  const v6Start = new BigUint64Array(v6.length);
  const v6End = new BigUint64Array(v6.length);
  const v6Country = new Array(v6.length);
  v6.forEach((row, i) => {
    const [a, b, cc] = row.split(',');
    v6Start[i] = BigInt(a) >> 64n;
    v6End[i] = BigInt(b) >> 64n;
    v6Country[i] = cc;
  });

  tables = { v4Start, v4End, v4Country, v6Start, v6End, v6Country };
  return tables;
}

// The last range starting at or before `value`, if `value` is inside it.
function find(starts, ends, countries, value) {
  let lo = 0;
  let hi = starts.length - 1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (starts[mid] <= value) lo = mid + 1;
    else hi = mid - 1;
  }
  return hi >= 0 && value <= ends[hi] ? countries[hi] : null;
}

function v4Number(ip) {
  return ip.split('.').reduce((n, part) => n * 256 + Number(part), 0);
}

// The first 64 bits of an IPv6 address.
function v6High(ip) {
  const [head, tail = ''] = ip.split('::');
  const left = head ? head.split(':') : [];
  const right = tail ? tail.split(':') : [];
  const groups = ip.includes('::') ? [...left, ...new Array(Math.max(0, 8 - left.length - right.length)).fill('0'), ...right] : left;
  return groups.slice(0, 4).reduce((n, g) => (n << 16n) | BigInt(parseInt(g || '0', 16)), 0n);
}

// Two-letter country code for an IP address, or null when it isn't a public address we know.
function countryOfIp(ip) {
  let address = String(ip || '').trim();
  const mapped = address.match(/^::ffff:(\d+\.\d+\.\d+\.\d+)$/i);
  if (mapped) address = mapped[1];
  const t = loadTables();
  if (net.isIPv4(address)) return find(t.v4Start, t.v4End, t.v4Country, v4Number(address));
  if (net.isIPv6(address)) return find(t.v6Start, t.v6End, t.v6Country, v6High(address.split('%')[0]));
  return null;
}

function createGeo(config, { log = console } = {}) {
  const allowed = config.region.allowed;
  const restricted = allowed.length > 0;
  const regionNames = new Intl.DisplayNames(['en-AU'], { type: 'region' });
  const names = allowed.map((cc) => regionNames.of(cc) || cc);
  // "Australia", "Australia or New Zealand", "A, B or C"
  const where = names.length <= 1 ? names[0] || '' : `${names.slice(0, -1).join(', ')} or ${names.at(-1)}`;

  function clientIp(req) {
    const header = config.region.clientIpHeader;
    const value = header ? req.get(header) : null;
    return (value ? value.split(',')[0].trim() : null) || req.ip;
  }

  // { country, allowed } for a request. With no restriction everything is allowed and the
  // country isn't looked up.
  function check(req) {
    if (!restricted) return { country: null, allowed: true };
    const ip = clientIp(req);
    const country = countryOfIp(ip);
    if (config.region.debug) {
      log.log(
        `[geo] ${req.method} ${req.path} ip=${ip} country=${country} req.ip=${req.ip} ` +
          `x-forwarded-for=${req.get('x-forwarded-for') || ''} cf-connecting-ip=${req.get('cf-connecting-ip') || ''} ` +
          `true-client-ip=${req.get('true-client-ip') || ''} cf-ipcountry=${req.get('cf-ipcountry') || ''}`
      );
    }
    // An address we can't place counts as outside: the restriction errs on the strict side.
    return { country, allowed: country !== null && allowed.includes(country) };
  }

  return { restricted, where, check, warm: () => restricted && loadTables() };
}

module.exports = { createGeo, countryOfIp };
