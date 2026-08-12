import js from '@eslint/js';
import globals from 'globals';
import tseslint from 'typescript-eslint';

// Vendor asset bundler: main.ts only imports Bootstrap, so a plain TypeScript baseline is enough.
export default tseslint.config(
  { ignores: ['dist'] },
  {
    files: ['**/*.ts'],
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
  },
);
