'use strict';

// Makes the key pair that signs licences. Run once: `npm run licence-key`.
// The private key goes in the LICENCE_SIGNING_KEY environment variable on the server and
// nowhere else. The public key is what the add-in ships with to check a licence.

const crypto = require('node:crypto');

const { privateKey, publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' });
const pem = privateKey.export({ type: 'pkcs8', format: 'pem' });
const jwk = publicKey.export({ format: 'jwk' });

console.log('LICENCE_SIGNING_KEY (private: keep it secret, set it on the server as one line):\n');
console.log(Buffer.from(pem).toString('base64'));
console.log('\nPublic key (safe to share; build it into the add-in):\n');
console.log(publicKey.export({ type: 'spki', format: 'pem' }));
console.log(JSON.stringify({ kty: jwk.kty, crv: jwk.crv, x: jwk.x, y: jwk.y }, null, 2));
