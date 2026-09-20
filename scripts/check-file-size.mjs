#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const WARNING_THRESHOLD = 450;
const CEILING_THRESHOLD = 600;

const DEFAULT_IGNORED_DIRS = new Set([
  '.git',
  'node_modules',
  'bin',
  'obj',
  'dist',
  '.turbo',
  '.pnpm-store',
  'coverage',
  'test-results',
  'playwright-report'
]);

const BINARY_EXTENSIONS = new Set([
  '.png', '.jpg', '.jpeg', '.gif', '.svg', '.ico', '.webp',
  '.woff', '.woff2', '.ttf', '.eot',
  '.dll', '.exe', '.so', '.dylib', '.pdb',
  '.zip', '.tar', '.gz', '.7z',
  '.pdf', '.mp3', '.mp4'
]);

export function loadAllowlist(allowlistPath) {
  if (!fs.existsSync(allowlistPath)) {
    return new Map();
  }
  try {
    const raw = fs.readFileSync(allowlistPath, 'utf8');
    const data = JSON.parse(raw);
    const map = new Map();
    if (Array.isArray(data.allowlist)) {
      for (const entry of data.allowlist) {
        if (entry.path) {
          map.set(path.normalize(entry.path).toLowerCase(), entry.reason || 'No reason provided');
        }
      }
    }
    return map;
  } catch (err) {
    console.error(`Failed to parse allowlist at ${allowlistPath}: ${err.message}`);
    return new Map();
  }
}

export function isBinaryOrMinified(filePath) {
  const ext = path.extname(filePath).toLowerCase();
  if (BINARY_EXTENSIONS.has(ext)) return true;
  if (filePath.endsWith('.min.js') || filePath.endsWith('.min.css')) return true;
  if (filePath.endsWith('.cobertura.xml') || filePath.endsWith('coverage.json')) return true;
  if (filePath.endsWith('.log') || filePath.endsWith('.jsonl')) return true;
  return false;
}

export function scanDirectory(dir, rootDir, allowlist, results = { warnings: [], errors: [], passed: 0 }) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    const relPath = path.relative(rootDir, fullPath);

    if (entry.isDirectory()) {
      if (!DEFAULT_IGNORED_DIRS.has(entry.name)) {
        scanDirectory(fullPath, rootDir, allowlist, results);
      }
      continue;
    }

    if (!entry.isFile()) continue;
    if (isBinaryOrMinified(fullPath)) continue;

    const normalizedRelPath = path.normalize(relPath).toLowerCase();
    const content = fs.readFileSync(fullPath, 'utf8');
    const lines = content.split('\n').length;

    if (lines > CEILING_THRESHOLD) {
      if (allowlist.has(normalizedRelPath)) {
        results.passed++;
      } else {
        results.errors.push({ path: relPath, lines });
      }
    } else if (lines > WARNING_THRESHOLD) {
      results.warnings.push({ path: relPath, lines });
    } else {
      results.passed++;
    }
  }

  return results;
}

export function checkFileSizes(rootDir, allowlistPath) {
  const allowlist = loadAllowlist(allowlistPath);
  return scanDirectory(rootDir, rootDir, allowlist);
}

// CLI execution
if (process.argv[1] === __filename) {
  const rootDir = path.resolve(__dirname, '..');
  const allowlistPath = path.resolve(__dirname, 'file-size-allowlist.json');

  console.log('=== Checking File Size Limits (450 Warning / 600 Ceiling) ===');
  const results = checkFileSizes(rootDir, allowlistPath);

  if (results.warnings.length > 0) {
    console.log(`\n[WARNING] ${results.warnings.length} file(s) exceed ${WARNING_THRESHOLD} lines:`);
    for (const w of results.warnings) {
      console.log(`  - ${w.path}: ${w.lines} lines (approaching ceiling)`);
    }
  }

  if (results.errors.length > 0) {
    console.error(`\n[FAILED] ${results.errors.length} file(s) exceed ${CEILING_THRESHOLD} lines limit:`);
    for (const e of results.errors) {
      console.error(`  - ${e.path}: ${e.lines} lines (STRICT LIMIT EXCEEDED)`);
    }
    console.error('\nAction required: Decompose these files or add them to scripts/file-size-allowlist.json with justification.');
    process.exit(1);
  }

  console.log(`\n[PASS] All ${results.passed} human-authored files are within limits (< ${CEILING_THRESHOLD} lines).`);
  process.exit(0);
}
