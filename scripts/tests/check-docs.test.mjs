import test from 'node:test';
import assert from 'node:assert';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {
  validateMarkdownLinks,
  validateMandatoryDocs,
  validateAgentAdapters,
  findMarkdownFiles
} from '../check-docs.mjs';

test('validateMarkdownLinks detects broken links and ignores valid ones', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ro-doc-test-'));

  try {
    fs.writeFileSync(path.join(tempDir, 'target.md'), '# Target');
    fs.writeFileSync(
      path.join(tempDir, 'source.md'),
      '# Source\n[Valid link](./target.md)\n[Broken link](./non-existent.md)\n[External](https://google.com)'
    );

    const mdFiles = findMarkdownFiles(tempDir);
    const broken = validateMarkdownLinks(tempDir, mdFiles);

    assert.strictEqual(broken.length, 1);
    assert.strictEqual(broken[0].link, './non-existent.md');
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('validateMandatoryDocs detects missing documents', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ro-doc-test-'));

  try {
    fs.writeFileSync(path.join(tempDir, 'README.md'), '# Root');

    const missing = validateMandatoryDocs(tempDir, ['README.md', 'AGENTS.md']);

    assert.strictEqual(missing.length, 1);
    assert.strictEqual(missing[0], 'AGENTS.md');
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('validateAgentAdapters detects non-referencing adapters', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ro-doc-test-'));

  try {
    fs.writeFileSync(path.join(tempDir, 'CLAUDE.md'), 'Read AGENTS.md first.');
    fs.writeFileSync(path.join(tempDir, 'GEMINI.md'), 'Just do whatever.');

    const invalid = validateAgentAdapters(tempDir, ['CLAUDE.md', 'GEMINI.md', 'MISSING.md']);

    assert.strictEqual(invalid.length, 2);
    assert.strictEqual(invalid[0].adapter, 'GEMINI.md');
    assert.strictEqual(invalid[0].reason, 'Does not reference AGENTS.md');
    assert.strictEqual(invalid[1].adapter, 'MISSING.md');
    assert.strictEqual(invalid[1].reason, 'File does not exist');
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});
