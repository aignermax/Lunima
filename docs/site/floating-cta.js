/* Shows a compact floating Download button once the hero CTA has scrolled out of view
   and hides it again while the download section itself is visible. */
(function () {
  "use strict";
  var fab = document.getElementById("fab-download");
  var hero = document.querySelector("[data-release-cta]");
  var section = document.getElementById("download");
  if (!fab || !hero || !("IntersectionObserver" in window)) return;

  var heroGone = false;
  var sectionVisible = false;

  function render() {
    var show = heroGone && !sectionVisible;
    fab.classList.toggle("show", show);
    fab.setAttribute("aria-hidden", show ? "false" : "true");
    fab.tabIndex = show ? 0 : -1;
    // The release script rewrites the hero CTA href after load; follow it.
    if (show) fab.setAttribute("href", hero.getAttribute("href"));
  }

  new IntersectionObserver(function (entries) {
    var entry = entries[entries.length - 1];
    // Only "scrolled past" counts: the CTA left the viewport through the top.
    heroGone = !entry.isIntersecting && entry.boundingClientRect.bottom < 0;
    render();
  }).observe(hero);

  if (section) {
    new IntersectionObserver(function (entries) {
      sectionVisible = entries[entries.length - 1].isIntersecting;
      render();
    }, { rootMargin: "0px 0px -20% 0px" }).observe(section);
  }
})();
