window.arrdashPoll = (function () {
    let timer = null;
    let dotnetRef = null;
    let lastUpdatedAt = null;
    let manualOnly = false;
    let pollSeconds = 30;

    async function tick() {
        if (manualOnly) {
            return;
        }

        try {
            const response = await fetch('/api/dashboard', { cache: 'no-store' });
            if (!response.ok) {
                return;
            }

            const data = await response.json();
            const updatedAt = data.updatedAt;
            if (!updatedAt) {
                return;
            }

            if (lastUpdatedAt && updatedAt !== lastUpdatedAt) {
                if (dotnetRef) {
                    try {
                        await dotnetRef.invokeMethodAsync('SyncFromServerState');
                        lastUpdatedAt = updatedAt;
                        return;
                    } catch {
                        window.arrdashReconnect?.reloadOnce?.();
                        return;
                    }
                }
            }

            lastUpdatedAt = updatedAt;
        } catch {
            // Ignore transient network errors.
        }
    }

    function start(ref, manual, intervalSeconds) {
        stop();
        dotnetRef = ref;
        manualOnly = !!manual;
        pollSeconds = Math.max(5, intervalSeconds || 30);
        lastUpdatedAt = null;

        if (manualOnly) {
            return;
        }

        tick();
        timer = window.setInterval(tick, pollSeconds * 1000);
    }

    function stop() {
        if (timer) {
            window.clearInterval(timer);
        }
        timer = null;
        dotnetRef = null;
        lastUpdatedAt = null;
    }

    return { start, stop };
})();
