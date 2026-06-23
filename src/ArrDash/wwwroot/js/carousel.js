window.arrdashCarousel = {
    scrollBy: function (element, direction) {
        if (!element) return;
        const amount = Math.max(80, element.clientWidth * 0.75) * direction;
        element.scrollBy({ left: amount, behavior: "smooth" });
    },
    getState: function (element) {
        if (!element) {
            return { canScrollLeft: false, canScrollRight: false };
        }

        const maxScroll = element.scrollWidth - element.clientWidth;
        return {
            canScrollLeft: element.scrollLeft > 4,
            canScrollRight: maxScroll - element.scrollLeft > 4
        };
    }
};
