'use strict';

// Sends email through Resend. With no RESEND_API_KEY (local development, tests) the
// message is printed and kept in `outbox` instead.

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function layout({ heading, lines, button }) {
  const paragraphs = lines.map((l) => `<p style="margin:0 0 16px;font-size:16px;line-height:1.55;color:#36414B">${escapeHtml(l)}</p>`).join('');
  const cta = button
    ? `<p style="margin:8px 0 24px"><a href="${escapeHtml(button.url)}" style="display:inline-block;background:#111A22;color:#FFFFFF;font-weight:600;font-size:16px;text-decoration:none;padding:14px 22px;border-radius:14px">${escapeHtml(button.label)}</a></p>
       <p style="margin:0 0 16px;font-size:13px;line-height:1.5;color:#55606B">If the button doesn't work, copy this address into your browser:<br>${escapeHtml(button.url)}</p>`
    : '';
  return `<!doctype html><html><body style="margin:0;background:#F5F6F4;font-family:Inter,'Helvetica Neue',Arial,sans-serif">
<div style="max-width:520px;margin:0 auto;padding:32px 24px">
<p style="margin:0 0 24px;font-size:22px;font-weight:700;letter-spacing:-0.02em;color:#111A22">pointerpath</p>
<div style="background:#FFFFFF;border:1px solid #D5D9DC;border-radius:20px;padding:28px">
<h1 style="margin:0 0 16px;font-size:22px;line-height:1.25;color:#111A22">${escapeHtml(heading)}</h1>
${paragraphs}${cta}
</div></div></body></html>`;
}

function textVersion({ heading, lines, button }) {
  return [heading, '', ...lines, ...(button ? ['', `${button.label}: ${button.url}`] : [])].join('\n');
}

function createMailer(config, { fetchImpl = fetch, log = console.log } = {}) {
  const outbox = [];

  async function send({ to, subject, heading, lines, button }) {
    const message = {
      from: config.resend.from,
      to: [to],
      subject,
      html: layout({ heading, lines, button }),
      text: textVersion({ heading, lines, button }),
    };
    if (!config.resend.apiKey) {
      outbox.push(message);
      log(`[mail] (not sent, no RESEND_API_KEY) to ${to}: ${subject}\n${message.text}\n`);
      return;
    }
    const res = await fetchImpl('https://api.resend.com/emails', {
      method: 'POST',
      headers: { Authorization: `Bearer ${config.resend.apiKey}`, 'Content-Type': 'application/json' },
      body: JSON.stringify(message),
    });
    if (!res.ok) {
      const detail = await res.text().catch(() => '');
      throw new Error(`Resend refused the email (${res.status}): ${detail.slice(0, 300)}`);
    }
  }

  const base = config.baseUrl;
  return {
    outbox,
    send,
    sendVerification: (to, token) =>
      send({
        to,
        subject: 'Confirm your email for PointerPath',
        heading: 'Confirm your email address',
        lines: ['Thanks for creating a PointerPath account. Confirm this address to finish setting it up.', 'The link works for 24 hours.'],
        button: { label: 'Confirm email', url: `${base}/verify?token=${encodeURIComponent(token)}` },
      }),
    sendAlreadyRegistered: (to) =>
      send({
        to,
        subject: 'You already have a PointerPath account',
        heading: 'You already have an account',
        lines: [
          'Someone, probably you, tried to create a PointerPath account with this email address, but one already exists.',
          'Sign in with it, or reset the password if you have forgotten it. If this was not you, ignore this email.',
        ],
        button: { label: 'Sign in', url: `${base}/login` },
      }),
    sendPasswordReset: (to, token) =>
      send({
        to,
        subject: 'Reset your PointerPath password',
        heading: 'Reset your password',
        lines: ['Use the link below to choose a new password. It works for one hour.', 'If you did not ask for this, ignore this email and your password stays as it is.'],
        button: { label: 'Choose a new password', url: `${base}/reset?token=${encodeURIComponent(token)}` },
      }),
    sendLicenceActive: (to) =>
      send({
        to,
        subject: 'Your PointerPath licence is active',
        heading: 'Your licence is active',
        lines: [
          'Thanks for buying PointerPath. Your licence is attached to this email address.',
          'Download the installer from your account, then sign in to PointerPath inside BricsCAD or AutoCAD with this email address and your password to activate it.',
        ],
        button: { label: 'Go to your account', url: `${base}/account` },
      }),
  };
}

module.exports = { createMailer };
