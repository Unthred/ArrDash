window.arrdashMetrics = {
  pointerInElement: (element, clientX) => {
    if (!element) return null;
    const rect = element.getBoundingClientRect();
    const width = rect.width || 1;
    const x = Math.max(0, Math.min(clientX - rect.left, width));
    return { x, width };
  }
};
