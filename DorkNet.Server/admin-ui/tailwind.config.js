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
          DEFAULT: '#0b95c7',
          50:  '#ecfbff',
          100: '#c8f3ff',
          200: '#8bedff',
          300: '#51e5ff',
          400: '#22cfee',
          500: '#0b95c7',
          600: '#087aa8',
          700: '#096083',
          800: '#0a465f',
          900: '#082f40',
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
