window.arrdashBackToTop = (function () {
    const bindings = new WeakMap();
    let windowBinding = null;

    function resolveTableScroller(host) {
        if (!host) return null;
        const table = host.querySelector(".mud-table-container");
        return table || null;
    }

    function notifyElement(binding) {
        const top = binding.el.scrollTop || 0;
        const show = top >= binding.threshold;
        if (show === binding.visible) return;
        binding.visible = show;
        binding.dotNet.invokeMethodAsync("OnBackToTopVisibility", show).catch(function () { });
    }

    function notifyWindow() {
        if (!windowBinding) return;
        const top = window.scrollY || document.documentElement.scrollTop || 0;
        const show = top >= windowBinding.threshold;
        if (show === windowBinding.visible) return;
        windowBinding.visible = show;
        windowBinding.dotNet.invokeMethodAsync("OnBackToTopVisibility", show).catch(function () { });
    }

    function clearElementListener(binding) {
        if (!binding.onScroll || !binding.el) return;
        binding.el.removeEventListener("scroll", binding.onScroll);
        binding.onScroll = null;
    }

    function bindElement(binding, host) {
        clearElementListener(binding);
        const el = resolveTableScroller(host);
        if (!el) {
            binding.el = null;
            if (binding.visible) {
                binding.visible = false;
                binding.dotNet.invokeMethodAsync("OnBackToTopVisibility", false).catch(function () { });
            }
            return;
        }

        binding.el = el;
        binding.onScroll = function () { notifyElement(binding); };
        el.addEventListener("scroll", binding.onScroll, { passive: true });
        notifyElement(binding);
    }

    function attach(host, dotNet, thresholdPx) {
        if (!host || !dotNet) return;
        detach(host);

        const threshold = typeof thresholdPx === "number" && thresholdPx > 0 ? thresholdPx : 280;
        const binding = {
            host: host,
            dotNet: dotNet,
            threshold: threshold,
            visible: false,
            el: null,
            onScroll: null,
            observer: null,
            retryTimer: null,
            mutateTimer: null
        };

        bindElement(binding, host);
        binding.retryTimer = window.setTimeout(function () { bindElement(binding, host); }, 250);
        binding.observer = new MutationObserver(function () {
            if (binding.mutateTimer) window.clearTimeout(binding.mutateTimer);
            binding.mutateTimer = window.setTimeout(function () { bindElement(binding, host); }, 100);
        });
        binding.observer.observe(host, { childList: true, subtree: true });
        bindings.set(host, binding);
    }

    function scrollToTop(host) {
        const binding = bindings.get(host);
        if (binding && binding.el) {
            binding.el.scrollTo({ top: 0, behavior: "smooth" });
            return;
        }
        if (host)
            host.scrollIntoView({ behavior: "smooth", block: "start" });
    }

    function detach(host) {
        const binding = bindings.get(host);
        if (!binding) return;
        if (binding.retryTimer) window.clearTimeout(binding.retryTimer);
        if (binding.mutateTimer) window.clearTimeout(binding.mutateTimer);
        if (binding.observer) binding.observer.disconnect();
        clearElementListener(binding);
        bindings.delete(host);
    }

    function attachWindow(dotNet, thresholdPx) {
        detachWindow();
        if (!dotNet) return;
        const threshold = typeof thresholdPx === "number" && thresholdPx > 0 ? thresholdPx : 400;
        windowBinding = {
            dotNet: dotNet,
            threshold: threshold,
            visible: false,
            onScroll: function () { notifyWindow(); }
        };
        window.addEventListener("scroll", windowBinding.onScroll, { passive: true });
        notifyWindow();
    }

    function scrollWindowToTop() {
        window.scrollTo({ top: 0, behavior: "smooth" });
    }

    function detachWindow() {
        if (!windowBinding) return;
        window.removeEventListener("scroll", windowBinding.onScroll);
        windowBinding = null;
    }

    return {
        attach: attach,
        scrollToTop: scrollToTop,
        detach: detach,
        attachWindow: attachWindow,
        scrollWindowToTop: scrollWindowToTop,
        detachWindow: detachWindow
    };
})();
