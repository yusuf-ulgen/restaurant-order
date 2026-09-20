import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import path from 'node:path';

describe('Design Tokens Integrity Gate', () => {
  const packageRoot = path.resolve(__dirname, '../../..');
  const repoRoot = path.resolve(packageRoot, '../..');
  const tokensCssPath = path.join(packageRoot, 'src/tokens.css');
  const tokensCss = fs.readFileSync(tokensCssPath, 'utf8');

  // Collect all defined --ro-* tokens in tokens.css
  const definedTokens = new Set<string>();
  for (const match of tokensCss.matchAll(/(--ro-[a-zA-Z0-9_-]+)\s*:/g)) {
    if (match[1]) definedTokens.add(match[1]);
  }

  function scanDirectory(dir: string): string[] {
    if (!fs.existsSync(dir)) return [];
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    const files: string[] = [];

    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);
      if (
        entry.isDirectory() &&
        !['node_modules', 'dist', 'coverage', '.git'].includes(entry.name)
      ) {
        files.push(...scanDirectory(fullPath));
      } else if (
        entry.isFile() &&
        /\.(css|ts|tsx)$/.test(entry.name) &&
        !entry.name.endsWith('.d.ts')
      ) {
        files.push(fullPath);
      }
    }
    return files;
  }

  it('verifies that all referenced var(--ro-*) tokens are defined in tokens.css', () => {
    const searchDirs = [
      path.join(repoRoot, 'packages/ui/src'),
      path.join(repoRoot, 'apps/customer-web/src'),
      path.join(repoRoot, 'apps/operations-web/src'),
      path.join(repoRoot, 'apps/admin-web/src'),
    ];

    const allFiles = searchDirs.flatMap(scanDirectory);
    const undefinedTokensWithLocations: { token: string; file: string }[] = [];

    // Regex strictly captures the CSS custom property name, ignoring fallback arguments
    const varRegex = /var\(\s*(--ro-[a-zA-Z0-9_-]+)/g;

    for (const file of allFiles) {
      const content = fs.readFileSync(file, 'utf8');
      for (const match of content.matchAll(varRegex)) {
        const tokenName = match[1];
        if (tokenName && !definedTokens.has(tokenName)) {
          const relativeFile = path.relative(repoRoot, file);
          undefinedTokensWithLocations.push({ token: tokenName, file: relativeFile });
        }
      }
    }

    expect(
      undefinedTokensWithLocations,
      `Found undefined CSS tokens in sources:\n${undefinedTokensWithLocations
        .map((u) => `  - ${u.token} in ${u.file}`)
        .join('\n')}`
    ).toEqual([]);
  });

  it('correctly handles expressions with fallbacks without false positives', () => {
    const sampleCss = `
      color: var(--ro-color-primary, #18181b);
      font-family: var(--ro-font-sans, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto);
      padding: var(--ro-space-4, 16px);
      border: 1px solid var(--ro-color-border, #e4e4e7);
    `;

    const varRegex = /var\(\s*(--ro-[a-zA-Z0-9_-]+)/g;
    const extracted: string[] = [];
    for (const match of sampleCss.matchAll(varRegex)) {
      if (match[1]) extracted.push(match[1]);
    }

    expect(extracted).toEqual([
      '--ro-color-primary',
      '--ro-font-sans',
      '--ro-space-4',
      '--ro-color-border',
    ]);

    for (const token of extracted) {
      expect(definedTokens.has(token)).toBe(true);
    }
  });
});
