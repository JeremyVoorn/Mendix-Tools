// Mendix Tools — component JS interop (MT-05 Dialog focus trap, MT-06 LogViewer follow).
// Loaded on demand as an ES module via IJSRuntime.import("./js/mxt-interop.js").
// Deliberately tiny: only the behaviours that genuinely need the DOM live here
// (focus trapping and scroll-position detection); everything else is Blazor/CSS.

const FOCUSABLE =
  'a[href],button:not([disabled]),textarea:not([disabled]),' +
  'input:not([disabled]),select:not([disabled]),[tabindex]:not([tabindex="-1"])';

// ---- Dialog (feedback/Dialog.jsx) ----
// Traps Tab focus inside `dialogEl`, focuses its first focusable element, and on
// dispose restores focus to whatever was focused before the dialog opened
// (AC: "focus stays within the dialog … when it closes, focus returns to the trigger").
export function trapFocus(dialogEl) {
  const previouslyFocused = document.activeElement;
  const first = dialogEl.querySelectorAll(FOCUSABLE)[0];
  (first || dialogEl).focus();

  function onKey(e) {
    if (e.key !== 'Tab') return;
    const items = dialogEl.querySelectorAll(FOCUSABLE);
    if (items.length === 0) {
      e.preventDefault();
      return;
    }
    const firstEl = items[0];
    const lastEl = items[items.length - 1];
    if (e.shiftKey && document.activeElement === firstEl) {
      e.preventDefault();
      lastEl.focus();
    } else if (!e.shiftKey && document.activeElement === lastEl) {
      e.preventDefault();
      firstEl.focus();
    }
  }

  dialogEl.addEventListener('keydown', onKey);

  return {
    dispose: () => {
      dialogEl.removeEventListener('keydown', onKey);
      if (previouslyFocused && typeof previouslyFocused.focus === 'function') {
        previouslyFocused.focus();
      }
    },
  };
}

// ---- LogViewer (data/LogViewer.jsx) ----
// Reports whether the scroll container is pinned to the bottom so the component
// can auto-follow the tail and stop following when the user scrolls up.
export function observeScroll(el, dotNetRef) {
  const handler = () => {
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 24;
    dotNetRef.invokeMethodAsync('SetFollowing', atBottom);
  };
  el.addEventListener('scroll', handler, { passive: true });
  el._mxtScroll = handler;
}

export function scrollToBottom(el) {
  if (el) el.scrollTop = el.scrollHeight;
}

export function disposeScroll(el) {
  if (el && el._mxtScroll) {
    el.removeEventListener('scroll', el._mxtScroll);
    el._mxtScroll = null;
  }
}

// ---- Theme (MT-07 app shell) ----
// Applies the chosen theme to <html> (the design system reads [data-theme="dark"])
// and mirrors it into localStorage. The authoritative store is MAUI Preferences
// (ThemeService); the localStorage mirror exists only so the inline boot script in
// index.html can set the attribute synchronously and avoid a light->dark flash on
// start. Light is represented by the absence of the attribute (matches :root).
const THEME_KEY = 'mxt-theme';

export function setTheme(theme) {
  const dark = theme === 'dark';
  if (dark) {
    document.documentElement.setAttribute('data-theme', 'dark');
  } else {
    document.documentElement.removeAttribute('data-theme');
  }
  try {
    localStorage.setItem(THEME_KEY, dark ? 'dark' : 'light');
  } catch (e) {
    /* private mode / storage disabled — Preferences is still authoritative */
  }
}

export function getTheme() {
  try {
    return localStorage.getItem(THEME_KEY) === 'dark' ? 'dark' : 'light';
  } catch (e) {
    return 'light';
  }
}
