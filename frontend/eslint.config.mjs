// frontend/desktop · frontend/web 이 같이 쓰는 ESLint 설정. 규칙의 정본은 루트 PROJECT_RULES.daiso 이고 여기는 그중 기계가 잡을 수 있는 것만 건다.
import js from '@eslint/js';
import prettier from 'eslint-config-prettier';
import reactHooks from 'eslint-plugin-react-hooks';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    ignores: ['**/node_modules/**', '**/dist/**', '**/out/**', '**/bin/**', '**/obj/**', '**/*.gen.ts'],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    rules: {
      // any 를 새로 쓰지 않는다. 불가피하면 eslint-disable 주석에 이유를 남긴다
      '@typescript-eslint/no-explicit-any': 'error',
      // 디버그용 console.log 는 커밋 전에 지운다. warn·error 는 남겨도 된다
      'no-console': ['error', { allow: ['warn', 'error'] }],
    },
  },
  {
    files: ['web/**/*.{ts,tsx}'],
    languageOptions: { globals: globals.browser },
    plugins: { 'react-hooks': reactHooks },
    rules: reactHooks.configs.recommended.rules,
  },
  {
    files: ['desktop/**/*.ts', '*.{js,mjs}'],
    languageOptions: { globals: globals.node },
  },
  prettier,
);
