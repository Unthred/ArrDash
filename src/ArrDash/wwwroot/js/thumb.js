// Shared poster fallback chain for WatchStatsMiniList and WatchStatsMiniPosterStrip.
// data-fallback holds a second poster URL to try before giving up to the initials tile.
window.arrdashThumb = {
    onError: function (img) {
        var fallback = img.dataset.fallback;
        if (fallback && !img.dataset.triedFallback) {
            img.dataset.triedFallback = "1";
            img.removeAttribute("data-fallback");
            img.src = fallback;
            return;
        }
        arrdashThumb.showPlaceholder(img);
    },
    onLoad: function (img) {
        // The poster resolver serves a 1x1 transparent PNG instead of a 404 when it
        // can't find art, so a "successful" load can still mean "nothing to show" —
        // treat a near-empty image the same as a failed one.
        if (img.naturalWidth > 2 || img.naturalHeight > 2) return;
        arrdashThumb.onError(img);
    },
    showPlaceholder: function (img) {
        img.style.display = "none";
        var placeholder = img.nextElementSibling;
        if (placeholder) {
            placeholder.classList.remove("watch-stats-thumb-fallback-hidden", "mini-poster-fallback-hidden");
        }
    }
};
