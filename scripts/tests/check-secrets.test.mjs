import test from 'node:test';
import assert from 'node:assert';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { checkSecrets, isSafeExample, scanFileForSecrets } from '../check-secrets.mjs';

test('isSafeExample correctly identifies placeholder and test strings', () => {
  assert.strictEqual(isSafeExample('PASSWORD=postgres:postgres'), true);
  assert.strictEqual(isSafeExample('AWS_KEY=AKIAIOSFODNN7EXAMPLE'), true);
  assert.strictEqual(isSafeExample('API_KEY=dummy_key_placeholder'), true);
  assert.strictEqual(isSafeExample('TOKEN=my_live_secret_token'), false);
});

test('scanFileForSecrets catches secret pattern without leaking the value', () => {
  // Construct a realistic token dynamically to avoid triggering scanners on test file
  const testToken = 'ghp_' + '1234567890abcdefghijklmnopqrstuvwxyz';
  const content = `const token = "${testToken}";\n`;

  const findings = scanFileForSecrets('test.js', content);

  assert.strictEqual(findings.length, 1);
  assert.strictEqual(findings[0].rule, 'GITHUB_TOKEN');
  assert.strictEqual(findings[0].line, 1);
  // Ensure the actual secret string is NOT stored on the finding object
  assert.strictEqual(JSON.stringify(findings[0]).includes(testToken), false);
});

test('checkSecrets detects forbidden files and permits .env.example', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ro-secret-test-'));

  try {
    // Permitted file
    fs.writeFileSync(path.join(tempDir, '.env.example'), 'DATABASE_URL=Host=localhost;Port=5432');

    // Forbidden file
    fs.writeFileSync(path.join(tempDir, '.env'), 'DATABASE_URL=Host=prod.db;Port=5432');

    const findings = checkSecrets(tempDir);

    assert.strictEqual(findings.length, 1);
    assert.strictEqual(findings[0].file, '.env');
    assert.strictEqual(findings[0].rule, 'FORBIDDEN_FILE_NAME');
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});
