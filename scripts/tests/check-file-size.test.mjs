import test from 'node:test';
import assert from 'node:assert';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { checkFileSizes, isBinaryOrMinified } from '../check-file-size.mjs';

test('isBinaryOrMinified identifies extensions correctly', () => {
  assert.strictEqual(isBinaryOrMinified('test.png'), true);
  assert.strictEqual(isBinaryOrMinified('test.min.js'), true);
  assert.strictEqual(isBinaryOrMinified('test.dll'), true);
  assert.strictEqual(isBinaryOrMinified('test.ts'), false);
  assert.strictEqual(isBinaryOrMinified('test.cs'), false);
  assert.strictEqual(isBinaryOrMinified('test.md'), false);
});

test('checkFileSizes detects normal, warning, error, and allowlist files', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ro-size-test-'));

  try {
    // Normal file (100 lines)
    fs.writeFileSync(path.join(tempDir, 'normal.ts'), 'line\n'.repeat(100));

    // Warning file (500 lines)
    fs.writeFileSync(path.join(tempDir, 'warning.ts'), 'line\n'.repeat(500));

    // Error file (700 lines)
    fs.writeFileSync(path.join(tempDir, 'too-large.ts'), 'line\n'.repeat(700));

    // Allowlisted file (800 lines)
    fs.writeFileSync(path.join(tempDir, 'allowed-large.ts'), 'line\n'.repeat(800));

    const allowlistPath = path.join(tempDir, 'allowlist.json');
    fs.writeFileSync(allowlistPath, JSON.stringify({
      allowlist: [
        { path: 'allowed-large.ts', reason: 'Test exception' }
      ]
    }));

    const results = checkFileSizes(tempDir, allowlistPath);

    assert.strictEqual(results.warnings.length, 1);
    assert.strictEqual(results.warnings[0].path, 'warning.ts');

    assert.strictEqual(results.errors.length, 1);
    assert.strictEqual(results.errors[0].path, 'too-large.ts');

    // normal.ts, allowed-large.ts, and allowlist.json are passed
    assert.strictEqual(results.passed, 3);
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});
