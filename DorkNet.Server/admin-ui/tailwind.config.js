/** @type {import('tailwindcss').Config} */

// Every palette colour is an RGB-channel CSS variable (see src/index.css),
// so one class name works in both themes: the light theme redefines the
// variables under :root, the dark theme under [data-theme="dark"]. The
// `ink` scale is ordered by contrast, not lightness: ink-950 is the page
// background and ink-50 the strongest text in either theme.
const v = (name) => `rgb(var(--${name}) / <alpha-value>)`;
const scale = (prefix, steps) => Object.fromEntries(steps.map((s) => [s, v(`${prefix}-${s}`)]));

export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  darkMode: ['selector', '[data-theme="dark"]'],
  theme: {
    extend: {
      colors: {
        ink: scale('ink', [50, 100, 200, 300, 400, 500, 600, 700, 800, 850, 900, 950]),
        brand: { DEFAULT: v('brand-500'), ...scale('brand', [50, 100, 200, 300, 400, 500, 600, 700, 800, 900]) },
        success: v('success'),
        warn: v('warn'),
        danger: v('danger'),
        // Sidebar / mobile bar background and the selected-row tint.
        bar: v('bar'),
        sel: v('sel'),
      },
      fontFamily: {
        sans: ['"IBM Plex Sans"', 'ui-sans-serif', 'system-ui', '-apple-system', '"Segoe UI"', 'Roboto', 'sans-serif'],
        mono: ['"IBM Plex Mono"', 'SFMono-Regular', 'Menlo', 'monospace'],
      },
      boxShadow: {
        // Only for things that float above the page (toasts, drawers, dialogs).
        float: '0 8px 30px rgb(0 0 0 / 0.18)',
      },
    },
  },
  plugins: [],
};
