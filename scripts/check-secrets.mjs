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

const SAFE_EXAMPLE_INDICATORS = [
  'example',
  'placeholder',
  'dummy',
  'mock',
  'fake',
  'sample',
  'test',
  'localhost',
  'postgres:postgres',
  'changeme',
  'YOUR_',
  '<YOUR',
  'AKIAIOSFODNN7EXAMPLE'
];

export function isSafeExample(line) {
  const lower = line.toLowerCase();
  for (const ind of SAFE_EXAMPLE_INDICATORS) {
    if (lower.includes(ind.toLowerCase())) {
      return true;
    }
  }
  return false;
}

export function scanFileForSecrets(filePath, content) {
  const findings = [];
  const lines = content.split('\n');

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];

    if (isSafeExample(line)) {
      continue;
    }

    for (const rule of SECRET_RULES) {
      if (rule.regex.test(line)) {
        findings.push({
          file: filePath,
          line: i + 1,
          rule: rule.name
          // Notice: We intentionally do NOT store or log the actual line content or secret value!
        });
      }
    }
  }

  return findings;
}

export function scanDirectoryForSecrets(dir, rootDir, findings = []) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    const relPath = path.relative(rootDir, fullPath);

    if (entry.isDirectory()) {
      if (!IGNORED_DIRS.has(entry.name)) {
        scanDirectoryForSecrets(fullPath, rootDir, findings);
      }
      continue;
    }

    if (!entry.isFile()) continue;

    // 1. Check forbidden file names
    if (!ALLOWED_FILES.has(entry.name)) {
      for (const pattern of FORBIDDEN_FILE_PATTERNS) {
        if (pattern.test(entry.name)) {
          findings.push({
            file: relPath,
            line: 1,
            rule: 'FORBIDDEN_FILE_NAME'
          });
          break;
        }
      }
    }

    // 2. Check file content
    try {
      const content = fs.readFileSync(fullPath, 'utf8');
      const fileFindings = scanFileForSecrets(relPath, content);
      findings.push(...fileFindings);
    } catch {
      // Binary files or unreadable files are skipped safely
    }
  }

  return findings;
}

export function checkSecrets(rootDir) {
  return scanDirectoryForSecrets(rootDir, rootDir);
}

// CLI execution
if (process.argv[1] === __filename) {
  const rootDir = path.resolve(__dirname, '..');
  console.log('=== Running Secret Scanner & Security Gate ===');

  const findings = checkSecrets(rootDir);

  if (findings.length > 0) {
    console.error(`\n[SECURITY FAILURE] Potential secrets or forbidden files detected (${findings.length}):`);
    for (const f of findings) {
      console.error(`  - ${f.file}:${f.line} [RULE: ${f.rule}]`);
    }
    console.error('\nAction required: Remove credentials immediately. Use environment variables or synthetic test fixtures.');
    process.exit(1);
  }

  console.log('\n[PASS] No exposed secrets, private keys, or forbidden credential files found.');
  process.exit(0);
}
