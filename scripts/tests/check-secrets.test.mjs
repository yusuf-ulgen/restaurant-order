import test from 'node:test';
import assert from 'node:assert';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {
  scanDirectory,
  isAllowlistedSecret,
  scanFileForSecrets,
  KNOWN_SAFE_FIXTURES
} from '../check-secrets.mjs';

test('isAllowlistedSecret correctly identifies known safe fixtures', () => {
  const dummyGh = ['ghp', '000000000000000000000000000000000000'].join('_');
  const dummyStripe = ['sk', 'live', '000000000000000000000000'].join('_');
  const realGh = ['ghp', '1234567890abcdefghijklmnopqrstuvwxyz'].join('_');

  assert.strictEqual(isAllowlistedSecret('AKIAIOSFODNN7EXAMPLE'), true);
  assert.strictEqual(isAllowlistedSecret(dummyGh), true);
  assert.strictEqual(isAllowlistedSecret(dummyStripe), true);
  assert.strictEqual(isAllowlistedSecret('YOUR_API_KEY_HERE'), true);
  assert.strictEqual(isAllowlistedSecret('<YOUR_SECRET_TOKEN>'), true);

  // Real or arbitrary keys are not allowlisted
  assert.strictEqual(isAllowlistedSecret('AKIA1234567890ABCDEF'), false);
  assert.strictEqual(isAllowlistedSecret(realGh), false);
});

test('scanFileForSecrets catches real tokens even when line contains safe words (prevents bypass)', () => {
  // Construct realistic tokens dynamically
  const ghToken = ['ghp', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890'].join('_');
  const awsKey = 'AKIA' + '1234567890ABCDEF';
  const stripeKey = ['sk', 'live', '1234567890abcdefghijklmn'].join('_');

  // These lines contain bypass keywords ('test', 'localhost', 'fake', 'sample') but real tokens!
  const content = [
    `// This is a test token on localhost: ${ghToken}`,
    `const fakeAwsKey = "${awsKey}"; // sample config for testing`,
    `const stripeLive = "${stripeKey}"; // fake test account`
  ].join('\n');

  const findings = scanFileForSecrets('src/config.js', content);

  // All 3 real tokens MUST be caught despite safe words on the line!
  assert.strictEqual(findings.length, 3);
  assert.strictEqual(findings[0].rule, 'GITHUB_TOKEN');
  assert.strictEqual(findings[1].rule, 'AWS_ACCESS_KEY');
  assert.strictEqual(findings[2].rule, 'STRIPE_LIVE_KEY');

  // Verify secret masking: neither finding contains the actual secret string
  assert.strictEqual(JSON.stringify(findings).includes(ghToken), false);
  assert.strictEqual(JSON.stringify(findings).includes(awsKey), false);
  assert.strictEqual(JSON.stringify(findings).includes(stripeKey), false);
  assert.ok(findings[0].snippet.includes('[REDACTED_SECRET]'));
});

test('scanFileForSecrets ignores allowlisted documentation fixtures (prevents false positives)', () => {
  const dummyGh = ['ghp', '000000000000000000000000000000000000'].join('_');
  const dummyStripe = ['sk', 'live', '000000000000000000000000'].join('_');

  const content = [
    'export const AWS_DOC_SAMPLE = "AKIAIOSFODNN7EXAMPLE";',
    `export const DUMMY_GH = "${dummyGh}";`,
    `export const DUMMY_STRIPE = "${dummyStripe}";`
  ].join('\n');

  const findings = scanFileForSecrets('docs/examples.js', content);
  assert.strictEqual(findings.length, 0);
});

test('scanDirectory detects forbidden files and permits .env.example', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ro-secret-test-'));

  try {
    // Permitted file
    fs.writeFileSync(path.join(tempDir, '.env.example'), 'DATABASE_URL=Host=localhost;Port=5432');

    // Forbidden files
    fs.writeFileSync(path.join(tempDir, '.env'), 'DATABASE_URL=Host=prod.db;Port=5432');
    fs.writeFileSync(path.join(tempDir, '.env.production'), 'DATABASE_URL=Host=prod.db;Port=5432');
    fs.writeFileSync(path.join(tempDir, 'id_rsa'), 'fake-private-key-material');
    fs.writeFileSync(path.join(tempDir, 'server.key'), 'fake-key-material');

    const findings = scanDirectory(tempDir, tempDir);

    assert.strictEqual(findings.length, 4);
    const rules = findings.map(f => f.rule);
    assert.ok(rules.every(r => r === 'FORBIDDEN_SECRET_FILE'));
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});
