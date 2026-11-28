// Analytics glue. Plausible counts pageviews by itself; this adds the three goals (Signup,
// Purchase and Download) and, when a Reddit pixel is set, reports the same things to
// Reddit Ads so it can tell which ads led to them.
(function () {
  var body = document.body;

  window.plausible =
    window.plausible ||
    function () {
      (window.plausible.q = window.plausible.q || []).push(arguments);
    };

  // The newer Plausible snippet needs an init() call; the older one starts by itself.
  if (body.hasAttribute('data-plausible-init')) {
    window.plausible.init =
      window.plausible.init ||
      function (options) {
        window.plausible.o = options || {};
      };
    window.plausible.init();
  }

  // The Reddit pixel: <body data-reddit-pixel="(pixel ID)">. This is Reddit's own loader,
  // kept here rather than in the page because the site allows no inline scripts.
  var pixel = body.getAttribute('data-reddit-pixel');
  if (pixel && !window.rdt) {
    var rdt = (window.rdt = function () {
      if (rdt.sendEvent) rdt.sendEvent.apply(rdt, arguments);
      else rdt.callQueue.push(arguments);
    });
    rdt.callQueue = [];
    var script = document.createElement('script');
    script.src = 'https://www.redditstatic.com/ads/pixel.js';
    script.async = true;
    document.head.appendChild(script);
    rdt('init', pixel, { useDecimalCurrencyValues: true });
    rdt('track', 'PageVisit');
  }

  // The same goal under Reddit's names. A sale carries its value; anything Reddit has no
  // name for goes as a custom event.
  function reddit(name, data) {
    if (!pixel) return;
    if (name === 'Signup') {
      window.rdt('track', 'SignUp');
    } else if (name === 'Purchase') {
      var sale = { itemCount: 1 };
      if (data && typeof data.value === 'number') {
        sale.value = data.value;
        sale.currency = data.currency;
      }
      if (data && data.id) {
        sale.transactionId = data.id;
        sale.conversionId = data.id;
      }
      window.rdt('track', 'Purchase', sale);
    } else {
      window.rdt('track', 'Custom', { customEventName: name });
    }
  }

  function goal(name, data) {
    window.plausible(name);
    reddit(name, data);
  }

  // A page reached by finishing something: <body data-page-event="Purchase">, with any
  // details as JSON in data-page-event-data.
  var pageEvent = body.getAttribute('data-page-event');
  if (pageEvent) {
    var data = null;
    try {
      data = JSON.parse(body.getAttribute('data-page-event-data') || 'null');
    } catch (e) {
      data = null;
    }
    goal(pageEvent, data);
  }

  // Links that count as a goal when followed: <a data-event="Download">.
  document.addEventListener('click', function (e) {
    var link = e.target.closest && e.target.closest('[data-event]');
    if (link) goal(link.getAttribute('data-event'));
  });
})();
