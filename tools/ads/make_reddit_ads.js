'use strict';

// Generate the Reddit ad images in brand/ads/reddit.
//
// Run from the repo root:  node tools/ads/make_reddit_ads.js   (needs Playwright: npm i playwright)
//   --only price,vehicles   draw just the ads whose names start with these
//   --out some/folder    write somewhere else (to try a change without touching the real ones)
//
// Each ad is a small web page, laid out by tools/ads/reddit.css in the brand's colours and type,
// drawn at exactly the image's size and photographed. The swept path figure is the website's
// (website/src/views/partials/sweep.ejs), and the vehicles are the dialogs' icons
// (tools/icons/vehicles), so the ads show what the product and the site show.
//
// The words on the images are below. The words that go beside them on Reddit (headline, button,
// link) are in brand/ads/reddit/README.md. Keep the two in step, and keep both in step with
// the website: an ad must not say what the page it leads to doesn't.

const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.resolve(__dirname, '..', '..');
const read = (...parts) => fs.readFileSync(path.join(ROOT, ...parts), 'utf8');

// ---- What the ads say -------------------------------------------------------------------

const PRICE = '$19';                       // the beta price Stripe charges, per month
const SEATS = 'two seats';                 // LICENCE_MAX_MACHINES, as the site words it
const FINAL = 'Final release price $39';   // PRICE_WAS, as the site words it
const URL = 'pointerpath.app';
const PILL = 'Beta · Australia';

const SIZES = {
  '1x1': { width: 1080, height: 1080 },
  '4x5': { width: 1080, height: 1350 },
};

const VEHICLES = [
  { icon: 'b85', name: 'B85 car', std: 'AS/NZS 2890.1' },
  { icon: 'b99', name: 'B99 car', std: 'AS/NZS 2890.1' },
  { icon: 'srv', name: '6.4 m SRV', std: 'AS 2890.2' },
  { icon: 'mrv', name: '8.8 m MRV', std: 'AS 2890.2' },
  { icon: 'hrv', name: '12.5 m HRV', std: 'AS 2890.2' },
];

// ---- The parts --------------------------------------------------------------------------

const svg = (...parts) => read(...parts).replace(/<\?xml[^>]*\?>/, '').replace(/<title>[^<]*<\/title>/, '').trim();

const lockup = { paper: svg('brand', 'lockup.svg'), ink: svg('brand', 'lockup-reversed.svg') };
// On a cyan background the cyan tile would vanish, so the tile turns ink and the logomark cyan
// (the colours of brand/icon-ink.svg), beside the ink wordmark.
lockup.cyan = lockup.paper
  .replace('<rect x="0" y="0" width="150" height="150" rx="35.25" fill="#3FDCFF"/>', '<rect x="0" y="0" width="150" height="150" rx="35.25" fill="#111A22"/>')
  .replace('<path fill="#111A22" transform=', '<path fill="#3FDCFF" transform=');
if (lockup.cyan === lockup.paper) throw new Error('brand/lockup.svg has changed: the cyan-background logo can no longer be made from it');
const logomark = svg('brand', 'logomark-accent.svg');
const sweep = svg('website', 'src', 'views', 'partials', 'sweep.ejs');
const vehicleIcon = (name) => svg('tools', 'icons', 'vehicles', `${name}.svg`);

const sheet = ({ block = true } = {}) => `
<figure class="sheet">
<div class="sheet__drawing">${sweep}</div>
${block ? `<figcaption class="sheet__block">
<div><span class="label">Vehicle</span><span>8.8 m MRV</span></div>
<div><span class="label">Radius</span><span>10 m</span></div>
<div><span class="label">Units</span><span>Metres</span></div>
</figcaption>` : ''}
</figure>`;

const fleet = () => `
<div class="fleet">
${VEHICLES.map((v) => `<div class="fleet__row">${vehicleIcon(v.icon)}<span class="fleet__name">${v.name}</span><span class="fleet__std">${v.std}</span></div>`).join('\n')}
</div>`;

const priceFoot = `<p class="foot__price"><b>${PRICE}</b> per month for ${SEATS}</p>`;
const finalFoot = `<p class="foot__note">Beta pricing. ${FINAL}.</p>`;

function ad({ theme = 'paper', layout = '', h1 = '', sub = '', facts = [], art = '', foot = priceFoot }) {
  const classes = ['ad', `ad--${theme}`, ...layout.split(' ').filter(Boolean).map((l) => `ad--${l}`)].join(' ');
  return `
<div class="${classes}">
<div class="top"><span class="logo">${lockup[theme]}</span><span class="pill">${PILL}</span></div>
${h1 ? `<div class="head"><h1>${h1}</h1>${sub ? `<p class="sub">${sub}</p>` : ''}</div>` : ''}
${facts.length ? `<ul class="facts">${facts.map((f) => `<li>${f}</li>`).join('')}</ul>` : ''}
${art ? (art.includes('class="big"') ? art : `<div class="art">${art}</div>`) : ''}
<div class="foot">${foot}<p class="foot__url">${URL}</p></div>
</div>`;
}

// ---- The ads ----------------------------------------------------------------------------
// name: [the sizes it is drawn at, its content]. `<span class="mark">` is the highlighted phrase.

const ADS = {
  // Single-image ads: one for each reason to look.
  'price': [['1x1', '4x5'], {
    theme: 'ink',
    h1: `Swept paths for <span class="mark">${PRICE} a month.</span>`,
    sub: 'A lightweight add-in for BricsCAD and AutoCAD.',
    facts: [`One licence, ${SEATS}`, 'AS 2890 cars and rigid trucks', 'Metres or millimetres'],
    art: sheet(),
    foot: finalFoot,
  }],
  'lightweight': [['1x1', '4x5'], {
    theme: 'paper',
    h1: 'Most turns don’t need the <span class="mark">full suite.</span>',
    sub: 'Simple swept paths for driveways, car parks and loading docks.',
    facts: ['Inside BricsCAD and AutoCAD', 'B85 car to 12.5 m HRV', 'Metres or millimetres'],
    art: sheet(),
  }],
  'vehicles': [['1x1', '4x5'], {
    theme: 'paper',
    layout: 'stack',
    h1: 'The <span class="mark">AS 2890</span> design vehicles, ready to drive.',
    sub: 'Swept paths and clearances inside BricsCAD and AutoCAD.',
    art: fleet(),
  }],
  'in-cad': [['1x1', '4x5'], {
    theme: 'cyan',
    h1: 'Check the turn without leaving <span class="mark">your drawing.</span>',
    sub: 'PointerPath runs entirely inside BricsCAD and AutoCAD.',
    facts: ['No other add-in needed', 'AS 2890 cars and rigid trucks', 'Metres or millimetres'],
    art: sheet(),
  }],

  // The carousel: four square cards, read left to right.
  'carousel-1-hook': [['1x1'], {
    theme: 'ink',
    layout: 'card',
    h1: 'Swept paths, <span class="mark">inside your CAD.</span>',
    sub: 'A lightweight add-in for BricsCAD and AutoCAD.',
    art: sheet({ block: false }),
    foot: `<p class="foot__note">For Australian civil and traffic engineers</p>`,
  }],
  'carousel-2-vehicles': [['1x1'], {
    theme: 'paper',
    layout: 'stack',
    h1: 'From a B85 car to a <span class="mark">12.5 m HRV.</span>',
    art: fleet(),
    foot: `<p class="foot__note">Cars and rigid trucks to AS 2890</p>`,
  }],
  'carousel-3-lightweight': [['1x1'], {
    theme: 'cyan',
    layout: 'type',
    h1: '<span class="mark">Lightweight</span> by design.',
    facts: ['Runs inside BricsCAD and AutoCAD', 'No other add-in needed', 'Metres or millimetres', `One licence, ${SEATS}`],
    foot: `<p class="foot__note">Swept paths and clearances</p>`,
  }],
  'carousel-4-price': [['1x1'], {
    theme: 'ink',
    layout: 'price',
    art: `<div class="big"><div class="big__text"><p class="big__amount">${PRICE}<small>/mo</small></p><p class="big__unit">for ${SEATS}.</p><p class="big__sub">Swept paths inside BricsCAD and AutoCAD.</p></div>${logomark}</div>`,
    foot: finalFoot,
  }],
};

// ---- Drawing them -----------------------------------------------------------------------

const FONTS = 'https://fonts.googleapis.com/css2?family=Outfit:wght@500;600&family=Inter:wght@400;500;600&display=block';

function page(name, size) {
  const [, content] = ADS[name];
  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<link href="${FONTS}" rel="stylesheet">
<style>${read('brand', 'tokens.css')}</style>
<style>${read('tools', 'ads', 'reddit.css')}</style>
</head>
<body class="r-${size}" style="--h: ${SIZES[size].height}px">${ad(content)}</body></html>`;
}

// Words that have outgrown the layout mean the copy is too long for it: say so rather than save
// an image with something cut off or run together.
const misfits = () => {
  const out = [];
  const box = (el) => el.getBoundingClientRect();
  const inside = (a, b) => a.left >= b.left - 0.5 && a.right <= b.right + 0.5 && a.top >= b.top - 0.5 && a.bottom <= b.bottom + 0.5;
  const name = (el) => `.${el.classList[0] || el.tagName.toLowerCase()}`;
  const adEl = document.querySelector('.ad');
  const style = getComputedStyle(adEl);
  const frame = box(adEl);
  const safe = {
    left: frame.left + parseFloat(style.paddingLeft), right: frame.right - parseFloat(style.paddingRight),
    top: frame.top + parseFloat(style.paddingTop), bottom: frame.bottom - parseFloat(style.paddingBottom),
  };
  const parts = [...adEl.children];
  for (const el of parts) if (!inside(box(el), safe)) out.push(`${name(el)} runs into the margin`);
  for (let i = 0; i < parts.length; i++) {
    for (let j = i + 1; j < parts.length; j++) {
      const a = box(parts[i]), b = box(parts[j]);
      if (a.left < b.right - 0.5 && b.left < a.right - 0.5 && a.top < b.bottom - 0.5 && b.top < a.bottom - 0.5) out.push(`${name(parts[i])} and ${name(parts[j])} overlap`);
    }
  }
  // The words in each part (measured as text, so a long unbreakable phrase that pokes out of
  // its column is caught even though the column itself is the right width).
  for (const part of parts) {
    const range = document.createRange();
    range.selectNodeContents(part);
    const text = range.getBoundingClientRect();
    const own = box(part);
    if (text.left < own.left - 16 || text.right > own.right + 16 || text.top < frame.top || text.bottom > frame.bottom) out.push(`the words in ${name(part)} don't fit it`);
  }
  for (const row of document.querySelectorAll('.fleet__row, .sheet__block > div, .foot')) {
    const kids = [...row.children].map(box);
    if (kids.some((k) => !inside(k, box(row)))) out.push(`something in ${name(row)} doesn't fit it`);
    for (let i = 1; i < kids.length; i++) if (row.matches('.fleet__row, .foot') && kids[i].left < kids[i - 1].right + 8) out.push(`${name(row)} is too crowded`);
  }
  return out;
};

async function main() {
  const args = process.argv.slice(2);
  const arg = (flag) => (args.includes(flag) ? args[args.indexOf(flag) + 1] : '');
  const only = arg('--only').split(',').filter(Boolean);
  const outDir = path.resolve(arg('--out') || path.join(ROOT, 'brand', 'ads', 'reddit'));
  fs.mkdirSync(outDir, { recursive: true });

  const { chromium } = require('playwright');
  const browser = await chromium.launch();
  const problems = [];
  try {
    for (const name of Object.keys(ADS)) {
      if (only.length && !only.some((o) => name.startsWith(o))) continue;
      for (const size of ADS[name][0]) {
        const tab = await browser.newPage({ viewport: SIZES[size], deviceScaleFactor: 1 });
        await tab.setContent(page(name, size), { waitUntil: 'networkidle' });
        const fonts = await tab.evaluate(async () => {
          await document.fonts.ready;
          return ['600 40px Outfit', '500 20px Inter'].filter((f) => !document.fonts.check(f));
        });
        if (fonts.length) throw new Error(`The brand fonts didn't load (${fonts.join(', ')}): is fonts.googleapis.com reachable?`);
        const file = `${name}-${size}.png`;
        for (const p of await tab.evaluate(misfits)) problems.push(`${file}: ${p}`);
        await tab.screenshot({ path: path.join(outDir, file) });
        await tab.close();
        console.log(`${file}  ${SIZES[size].width} x ${SIZES[size].height}  ${Math.round(fs.statSync(path.join(outDir, file)).size / 1024)} KB`);
      }
    }
  } finally {
    await browser.close();
  }
  if (problems.length) {
    console.error(`\n${problems.join('\n')}`);
    process.exitCode = 1;
  }
}

main().catch((err) => {
  console.error(err.message);
  process.exitCode = 1;
});
