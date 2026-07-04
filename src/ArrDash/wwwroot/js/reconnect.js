window.arrdashReconnect = (function () {
    let healthTimer = null;
    let modalTimer = null;
    let reloading = false;

    function reloadOnce() {
        if (reloading) {
            return;
        }
        reloading = true;
        window.location.reload();
    }

    function startHealthWatch() {
        if (healthTimer) {
            return;
        }
        healthTimer = window.setInterval(async () => {
            try {
                const response = await fetch('/health', { cache: 'no-store' });
                if (response.ok) {
                    reloadOnce();
                }
            } catch {
                // Server still restarting.
            }
        }, 2000);
    }

    function stopHealthWatch() {
        if (!healthTimer) {
            return;
        }
        window.clearInterval(healthTimer);
        healthTimer = null;
    }

    function isReconnectModalVisible() {
        const modal = document.getElementById('components-reconnect-modal');
        if (!modal) {
            return false;
        }

        const style = window.getComputedStyle(modal);
        return style.display !== 'none' && style.visibility !== 'hidden';
    }

    function startModalWatch() {
        if (modalTimer) {
            return;
        }

        modalTimer = window.setInterval(() => {
            if (isReconnectModalVisible()) {
                startHealthWatch();
            }
        }, 1000);
    }

    async function startBlazor() {
        const startOptions = {
            reconnectionOptions: {
                maxRetries: 60,
                retryIntervalMilliseconds: 2000,
            },
        };

        if (typeof Blazor !== 'undefined' && Blazor.start) {
            await Blazor.start(startOptions);
        }

        startModalWatch();
    }

    return {
        startBlazor,
        reloadOnce,
        stopHealthWatch,
    };
})();
