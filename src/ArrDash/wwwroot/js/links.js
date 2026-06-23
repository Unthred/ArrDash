window.arrdashLinks = {
    open: (url, target) => {
        if (!url) return;
        if (target === '_blank') {
            window.open(url, '_blank', 'noopener,noreferrer');
            return;
        }
        window.location.assign(url);
    }
};
