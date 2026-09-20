#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const IGNORED_DIRS = new Set([
  '.git',
  'node_modules',
  'bin',
  'obj',
  'dist',
  '.turbo',
  '.pnpm-store',
  'coverage',
  'test-results'
]);

const FORBIDDEN_FILE_PATTERNS = [
  /^\.env(\.(local|development|staging|production))?$/,
  /^id_rsa$/,
  /^id_ed25519$/,
  /\.(pem|key|pfx|p12)$/i
];

const ALLOWED_FILES = new Set([
  '.env.example',
  '.env.template'
]);

export const SECRET_RULES = [
  {
    name: 'PRIVATE_KEY_BLOCK',
    regex: /-----BEGIN (?:[A-Z0-9_-]+ )?PRIVATE KEY-----/
  },
  {
    name: 'GITHUB_TOKEN',
    regex: /\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{36,}\b/
  },
  {
    name: 'GITHUB_FINE_GRAINED_PAT',
    regex: /\bgithub_pat_[A-Za-z0-9_]{82}\b/
  },
  {
    name: 'AWS_ACCESS_KEY',
    regex: /\bAKIA[0-9A-Z]{16}\b/
  },
  {
    name: 'STRIPE_LIVE_KEY',
    regex: /\bsk_live_[0-9a-zA-Z]{24,}\b/
  },
  {
    name: 'SLACK_TOKEN',
    regex: /\bxox[baprs]-[0-9a-zA-Z]{10,48}\b/
  }
];

export const KNOWN_SAFE_FIXTURES = new Set([
  'AKIAIOSFODNN7EXAMPLE',
  ['ghp', '000000000000000000000000000000000000'].join('_'),
  ['github', 'pat', '0000000000000000000000000000000000000000000000000000000000000000000000000000000000'].join('_'),
  ['sk', 'live', '000000000000000000000000'].join('_'),
  ['xoxb', '0000000000', '0000000000', '000000000000000000000000'].join('-')
]);

export function isAllowlistedSecret(matchedToken) {
  if (KNOWN_SAFE_FIXTURES.has(matchedToken)) {
    return true;
  }

  // Allow explicit documentation placeholder patterns containing EXAMPLE or repeated zeros
  if (matchedToken.includes('EXAMPLE') ||
      matchedToken.includes('00000000') ||
      matchedToken.startsWith('YOUR_') ||
      matchedToken.startsWith('<YOUR_')) {
    return true;
  }

  return false;
}

export function scanFileForSecrets(filePath, content) {
  const findings = [];
  const lines = content.split('\n');

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];

    for (const rule of SECRET_RULES) {
      const match = rule.regex.exec(line);
      if (match) {
        const matchedToken = match[0];

        // Only skip if the matched token itself is an explicitly allowlisted fixture
        if (isAllowlistedSecret(matchedToken)) {
          continue;
        }

        // Sensitive secret detected: mask the value in the sanitized snippet
        const sanitizedSnippet = line.replace(rule.regex, '[REDACTED_SECRET]');

        findings.push({
          file: filePath,
          line: i + 1,
          rule: rule.name,
          snippet: sanitizedSnippet,
          matchLength: matchedToken.length
        });
      }
    }
  }

  return findings;
}

export function scanDirectory(dirPath, repoRoot = dirPath) {
  const findings = [];
  const entries = fs.readdirSync(dirPath, { withFileTypes: true });

  for (const entry of entries) {
    const fullPath = path.join(dirPath, entry.name);
    const relPath = path.relative(repoRoot, fullPath).replace(/\\/g, '/');

    if (entry.isDirectory()) {
      if (!IGNORED_DIRS.has(entry.name)) {
        findings.push(...scanDirectory(fullPath, repoRoot));
      }
    } else if (entry.isFile()) {
      // 1. Check for forbidden file patterns
      const isForbidden = FORBIDDEN_FILE_PATTERNS.some(p => p.test(entry.name));
      if (isForbidden && !ALLOWED_FILES.has(entry.name)) {
        findings.push({
          file: relPath,
          line: 0,
          rule: 'FORBIDDEN_SECRET_FILE',
          snippet: `Forbidden file found in repository: ${entry.name}`,
          matchLength: entry.name.length
        });
        continue;
      }

      // 2. Scan file content (skip binary files and test files itself)
      if (relPath.endsWith('.test.mjs') || relPath.endsWith('.test.ts') || relPath.endsWith('.test.tsx')) {
        continue;
      }

      try {
        const content = fs.readFileSync(fullPath, 'utf8');
        findings.push(...scanFileForSecrets(relPath, content));
      } catch {
        // Binary or unreadable file: skip
      }
    }
  }

  return findings;
}

export function runSecretScan(repoRoot = path.resolve(__dirname, '..')) {
  console.log('=== Running Secret Scanner & Security Gate ===\n');

  const findings = scanDirectory(repoRoot, repoRoot);

  if (findings.length > 0) {
    console.error(`[FAIL] ${findings.length} potential secret(s) or forbidden file(s) found:\n`);
    for (const f of findings) {
      console.error(`  - [${f.rule}] ${f.file}:${f.line}`);
      console.error(`    Snippet: ${f.snippet}`);
    }
    console.error('\nZero secrets policy strictly enforced. Remove all secrets before committing.');
    return false;
  }

  console.log('[PASS] No exposed secrets, private keys, or forbidden credential files found.');
  return true;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const success = runSecretScan();
  process.exit(success ? 0 : 1);
}
