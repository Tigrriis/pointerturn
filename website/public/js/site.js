// Plausible custom events. Pageviews are counted by Plausible's own script; this adds
// the three goals: Signup, Purchase and Download.
(function () {
  window.plausible =
    window.plausible ||
    function () {
      (window.plausible.q = window.plausible.q || []).push(arguments);
    };

  // The newer Plausible snippet needs an init() call; the older one starts by itself.
  if (document.body.hasAttribute('data-plausible-init')) {
    window.plausible.init =
      window.plausible.init ||
      function (options) {
        window.plausible.o = options || {};
      };
    window.plausible.init();
  }

  // A page reached by finishing something: <body data-page-event="Purchase">.
  var pageEvent = document.body.getAttribute('data-page-event');
  if (pageEvent) window.plausible(pageEvent);

  // Links that count as a goal when followed: <a data-event="Download">.
  document.addEventListener('click', function (e) {
    var link = e.target.closest && e.target.closest('[data-event]');
    if (link) window.plausible(link.getAttribute('data-event'));
  });
})();
