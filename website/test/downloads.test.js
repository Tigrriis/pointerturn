'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { presign } = require('../src/downloads');
const { start } = require('./helpers');

test('a signed link matches the worked example in the AWS Signature Version 4 documentation', () => {
  // "Authenticating Requests: Using Query Parameters", the examplebucket/test.txt example.
  const url = presign({
    host: 'examplebucket.s3.amazonaws.com',
    path: '/test.txt',
    region: 'us-east-1',
    accessKeyId: 'AKIAIOSFODNN7EXAMPLE',
    secretAccessKey: 'wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY',
    expires: 86400,
    now: new Date('2013-05-24T00:00:00Z'),
  });
  assert.equal(
    url,
    'https://examplebucket.s3.amazonaws.com/test.txt?X-Amz-Algorithm=AWS4-HMAC-SHA256' +
      '&X-Amz-Credential=AKIAIOSFODNN7EXAMPLE%2F20130524%2Fus-east-1%2Fs3%2Faws4_request' +
      '&X-Amz-Date=20130524T000000Z&X-Amz-Expires=86400&X-Amz-SignedHeaders=host' +
      '&X-Amz-Signature=aeeed9bbccd4d02ee5c0109b86d86835f995330da4c265957d157751f604d404'
  );
});

test('with a private bucket, a signed-in visitor is sent to a short-lived link of their own', async () => {
  const t = await start({
    INSTALLER_URL: '',
    INSTALLER_FILE: 'PointerPath-0.1.0-setup.exe',
    INSTALLER_VERSION: '0.1.0',
    R2_ENDPOINT: 'https://0123456789abcdef.r2.cloudflarestorage.com/',
    R2_BUCKET: 'pointerpath-installer',
    R2_ACCESS_KEY_ID: 'test-key-id',
    R2_SECRET_ACCESS_KEY: 'test-secret',
  });
  try {
    assert.equal((await t.browser().get('/download')).location, '/login');

    const { b } = await t.newAccount('dl@example.com');
    const account = await b.get('/account');
    assert.match(account.text, /Download the installer/);
    assert.match(account.text, /Version 0\.1\.0/);

    const res = await b.get('/download');
    assert.equal(res.status, 302);
    const link = new URL(res.location);
    assert.equal(link.origin + link.pathname, 'https://0123456789abcdef.r2.cloudflarestorage.com/pointerpath-installer/PointerPath-0.1.0-setup.exe');
    assert.equal(link.searchParams.get('X-Amz-Expires'), '300');
    assert.equal(link.searchParams.get('X-Amz-Credential').split('/')[0], 'test-key-id');
    assert.match(link.searchParams.get('X-Amz-Credential'), /\/auto\/s3\/aws4_request$/);
    assert.equal(link.searchParams.get('response-content-disposition'), 'attachment; filename="PointerPath-0.1.0-setup.exe"');
    assert.match(link.searchParams.get('X-Amz-Signature'), /^[0-9a-f]{64}$/);
    // The secret itself is never in the link.
    assert.doesNotMatch(res.location, /test-secret/);
  } finally {
    await t.stop();
  }
});

test('with nothing set up, the account page says the download is not published', async () => {
  const t = await start({ INSTALLER_URL: '' });
  try {
    const { b } = await t.newAccount('none@example.com');
    assert.match((await b.get('/account')).text, /has not been published yet/);
    assert.equal((await b.get('/download')).status, 503);
  } finally {
    await t.stop();
  }
});
