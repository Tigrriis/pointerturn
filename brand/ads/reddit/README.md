# Reddit ads

Images and words for PointerPath's Reddit ads. The pitch: a lightweight, low-cost swept path
add-in for Australian civil and traffic engineering firms, for the turns that don't need a full
suite.

The images are made by `tools/ads/make_reddit_ads.js`; don't edit the PNGs by hand. See
[Changing them](#changing-them).

## The ads

Four single-image ads, each with a different reason to look, so Reddit can show which one
works. Each comes square (`-1x1`, 1080 x 1080) and tall (`-4x5`, 1080 x 1350): upload both
to the same ad where Reddit lets you, otherwise use the tall one, which takes up more of a
phone's feed.

Every ad goes to the home page with the button **Learn More**. The headline is the post title
Reddit shows above the image; a second one is given for each to try against the first.

### Price (`price-1x1.png`, `price-4x5.png`)

On the image: "Swept paths for $19 a month."

1. Swept paths inside BricsCAD and AutoCAD for $19 a month. Two seats, AS 2890 design vehicles.
2. Only need a swept path a few times a month? PointerPath is $19/mo for two seats during the beta.

Link: `https://www.pointerpath.app/?utm_source=reddit&utm_medium=paid&utm_campaign=beta-au&utm_content=price`

### Lightweight (`lightweight-1x1.png`, `lightweight-4x5.png`)

On the image: "Most turns don't need the full suite."

1. Most turn checks don't need the full suite. PointerPath is a lightweight swept path add-in.
2. Swept paths for driveways, car parks and loading docks, without paying for a full suite.

Link: `https://www.pointerpath.app/?utm_source=reddit&utm_medium=paid&utm_campaign=beta-au&utm_content=lightweight`

### Vehicles (`vehicles-1x1.png`, `vehicles-4x5.png`)

On the image: "The AS 2890 design vehicles, ready to drive."

1. B85, B99, SRV, MRV and HRV swept paths inside BricsCAD and AutoCAD. $19/mo for two seats.
2. The AS 2890 design vehicles, ready to drive in your own drawing.

Link: `https://www.pointerpath.app/?utm_source=reddit&utm_medium=paid&utm_campaign=beta-au&utm_content=vehicles`

### Inside your CAD (`in-cad-1x1.png`, `in-cad-4x5.png`)

On the image: "Check the turn without leaving your drawing."

1. Check the turn without leaving your drawing. Swept paths inside BricsCAD and AutoCAD, $19/mo.
2. A swept path add-in that runs entirely inside BricsCAD and AutoCAD. No other add-in needed.

Link: `https://www.pointerpath.app/?utm_source=reddit&utm_medium=paid&utm_campaign=beta-au&utm_content=in-cad`

### Carousel (`carousel-1-hook-1x1.png` to `carousel-4-price-1x1.png`)

Four square cards, in the order of their numbers: what it is, the vehicles, why it is light, the
price.

Headline: Lightweight swept paths for Australian civil and traffic engineers. $19/mo for two seats.

Link, on every card: `https://www.pointerpath.app/?utm_source=reddit&utm_medium=paid&utm_campaign=beta-au&utm_content=carousel`

## Setting the campaign up

- **Where it is shown: Australia only.** The site refuses to sell outside Australia, so a click
  from anywhere else is paid for and can't buy.
- **Who sees it.** Start with communities for civil engineering, traffic and transport
  engineering, AutoCAD, Civil 3D and BricsCAD, and with Reddit's interest groups nearest
  engineering and CAD. Search for them in the ad's audience picker, which shows how many
  Australians each reaches: with a country limit on top, small communities may reach too few
  people to run on their own.
- **Counting what it does.** The `utm_` tags on the links show up in Plausible under Sources and
  Campaigns with nothing more to set up: `utm_content` says which ad a visitor came from, and
  the `Signup` and `Purchase` goals say what they went on to do.
- **Comments.** Reddit ads take comments unless they are switched off. Leaving them on suits an
  audience of engineers, who will ask what it does and doesn't do, but only if someone answers.

## What the ads may say

An ad must not say more than the page it leads to. These are the claims the ads make, and
where each comes from; if one of these changes, change the ads.

| Claim | From |
|---|---|
| $19 per month for two seats | The beta price in Stripe, and `LICENCE_MAX_MACHINES` (2) |
| Final release price $39 | `PRICE_WAS` in `render.yaml`, worded as the site words it |
| BricsCAD and AutoCAD | The home page |
| B85 and B99 cars to AS/NZS 2890.1; SRV, MRV and HRV to AS 2890.2 | The home page's vehicle list |
| Runs entirely inside the CAD program, no other add-in needed | The repository's README |
| Metres or millimetres | The home page |
| Australia, beta | `ALLOWED_COUNTRIES`, and the home page's "Beta version pricing" |

Left out on purpose:

- **Other products' names.** The ads say "the full suite", and name nobody.
- **Solve turn, In and out, Conflicts, Editable paths.** They are switched off on the home page
  for now, so the ads don't promise them either.

## The sizes

| | |
|---|---|
| Square | 1080 x 1080 (1:1) |
| Tall | 1080 x 1350 (4:5) |
| File | PNG, each under 200 KB |
| Headlines | All under 100 characters |

These follow what published guides to Reddit's ad sizes agree on. They disagree with each other
on the limits (a file of 3 MB or 20 MB, a headline of 150 or 300 characters), so the images and
headlines here are kept inside the strictest of them. Reddit's own specification pages
couldn't be read when these were made: if the ads manager objects to something on upload, its
word is the one that counts.

## Changing them

The words on the images, the price and the list of ads are at the top of
`tools/ads/make_reddit_ads.js`; the layout is `tools/ads/reddit.css`. The swept path drawing is
the website's own (`website/src/views/partials/sweep.ejs`) and the vehicles are the dialogs'
icons (`tools/icons/vehicles`), so a change to either shows up in the ads the next time they
are made.

From the repo root (needs Node and Playwright, `npm i playwright`, and a connection to Google
Fonts for Outfit and Inter):

```bash
node tools/ads/make_reddit_ads.js
```

To draw only some, or to try a change without overwriting these:

```bash
node tools/ads/make_reddit_ads.js --only price,vehicles --out /tmp/ads
```

It stops with a message if words no longer fit their place on an image (a longer headline, a
higher price), rather than saving an image with something cut off.

When the beta ends, change `PRICE` and `FINAL` in the script and the headlines above together.
