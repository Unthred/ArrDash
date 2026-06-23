window.arrdashTheme = {
  isSystemDark: () => window.matchMedia('(prefers-color-scheme: dark)').matches,
  watchSystemPreference: (dotNetRef) => {
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const handler = (event) => dotNetRef.invokeMethodAsync('OnSystemThemeChanged', event.matches);
    if (media.addEventListener) {
      media.addEventListener('change', handler);
    } else {
      media.addListener(handler);
    }
  }
};
