window.arrdashVpn = {
    checkStatus: async function (url, timeoutMs) {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), timeoutMs || 4000);
        try {
            const response = await fetch(url, { signal: controller.signal, cache: "no-store" });
            if (!response.ok) return null;
            const data = await response.json();
            return typeof data.in_alias === "boolean" ? data.in_alias : null;
        } catch {
            return null;
        } finally {
            clearTimeout(timer);
        }
    }
};
