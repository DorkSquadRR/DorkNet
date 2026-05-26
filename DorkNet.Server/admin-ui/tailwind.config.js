/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        ink: {
          50:  '#f7f7f8',
          100: '#ececef',
          200: '#d3d4d9',
          300: '#a9abb4',
          400: '#7e818d',
          500: '#5d606b',
          600: '#43464f',
          700: '#34363d',
          800: '#23252b',
          850: '#1b1d22',
          900: '#131418',
          950: '#0a0b0e',
        },
        brand: {
          DEFAULT: '#7c5cff',
          50:  '#f1edff',
          100: '#e1d8ff',
          200: '#c5b6ff',
          300: '#a48dff',
          400: '#8d72ff',
          500: '#7c5cff',
          600: '#6948e3',
          700: '#5236b8',
          800: '#3e2989',
          900: '#2a1c5f',
        },
        success: '#3ecf8e',
        warn:    '#f4b740',
        danger:  '#ef5b6b',
      },
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', '-apple-system', 'Segoe UI', 'Roboto', 'sans-serif'],
        mono: ['JetBrains Mono', 'SFMono-Regular', 'Menlo', 'monospace'],
      },
      boxShadow: {
        card: '0 1px 0 rgba(0,0,0,0.05), 0 1px 3px rgba(0,0,0,0.10)',
      },
    },
  },
  plugins: [],
};
