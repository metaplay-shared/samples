import MetaplayEslintConfig from '@metaplay/eslint-config/recommended'

export default [
  ...MetaplayEslintConfig,
  {
    files: ['**/*.ts'],
    rules: {
      '@eslint-community/eslint-comments/require-description': 'off',
    },
  },
]
