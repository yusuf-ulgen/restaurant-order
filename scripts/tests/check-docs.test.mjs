import test from 'node:test';
import assert from 'node:assert';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {
  validateMarkdownLinks,
  validateMandatoryDocs,
  validateAgentAdapters,
  findMarkdownFiles,
  generateGitHubSlug,
  extractMarkdownAnchors
} from '../check-docs.mjs';

test('generateGitHubSlug correctly generates GitHub-compatible slugs', () => {
  assert.strictEqual(generateGitHubSlug('1. Project Purpose & Scope'), '1-project-purpose-scope');
  assert.strictEqual(generateGitHubSlug('Section with `code` & [link](url)'), 'section-with-code-link');
  assert.strictEqual(generateGitHubSlug('Türkçe Başlık Şçğüöı'), 'türkçe-başlık-şçğüöı');
});

test('extractMarkdownAnchors extracts headings and HTML anchors', () => {
  const content = [
    '# Main Title',
    '## Sub Section',
    '### Sub Section',
    '<a id="custom-anchor"></a>'
  ].join('\n');

  const anchors = extractMarkdownAnchors(content);
  assert.strictEqual(anchors.has('main-title'), true);
  assert.strictEqual(anchors.has('sub-section'), true);
  assert.strictEqual(anchors.has('sub-section-1'), true);
  assert.strictEqual(anchors.has('custom-anchor'), true);
});

test('validateMarkdownLinks detects file:// links as errors', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ro-doc-test-'));

  try {
    fs.writeFileSync(
      path.join(tempDir, 'source.md'),
      '# Source\n[Forbidden](file:///d:/freelance/restaurant-order/docs/README.md)'
    );

    const mdFiles = findMarkdownFiles(tempDir);
    const broken = validateMarkdownLinks(tempDir, mdFiles);

    assert.strictEqual(broken.length, 1);
    assert.strictEqual(broken[0].link, 'file:///d:/freelance/restaurant-order/docs/README.md');
    assert.match(broken[0].reason, /file:\/\//i);
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('validateMarkdownLinks detects Windows absolute paths as errors', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ro-doc-test-'));

  try {
    fs.writeFileSync(
      path.join(tempDir, 'source.md'),
      '# Source\n[Win Drive D](D:/project/README.md)\n[Win Drive C](C:\\project\\README.md)'
    );

    const mdFiles = findMarkdownFiles(tempDir);
    const broken = validateMarkdownLinks(tempDir, mdFiles);

    assert.strictEqual(broken.length, 2);
    assert.match(broken[0].reason, /Windows absolute path/);
    assert.match(broken[1].reason, /Windows absolute path/);
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('validateMarkdownLinks detects links escaping repository root', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ro-doc-test-'));

  try {
    const subDir = path.join(tempDir, 'docs');
    fs.mkdirSync(subDir);
    fs.writeFileSync(
      path.join(subDir, 'sub.md'),
      '# Sub\n[Escape](../../../outside.md)'
    );

    const mdFiles = findMarkdownFiles(tempDir);
    const broken = validateMarkdownLinks(tempDir, mdFiles);

    assert.strictEqual(broken.length, 1);
    assert.match(broken[0].reason, /escapes repository root/);
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('validateMarkdownLinks detects broken relative links and validates existing ones', () => {
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
    assert.match(broken[0].reason, /does not exist/);
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('validateMarkdownLinks validates Markdown anchor links in target files and same file', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ro-doc-test-'));

  try {
    fs.writeFileSync(
      path.join(tempDir, 'target.md'),
      '# Target Document\n\n## Section One\n\nSome content'
    );
    fs.writeFileSync(
      path.join(tempDir, 'source.md'),
      '# Source Document\n\n## Local Header\n\n[Valid Ext Anchor](./target.md#section-one)\n[Broken Ext Anchor](./target.md#section-two)\n[Valid Local Anchor](#local-header)\n[Broken Local Anchor](#missing-header)'
    );

    const mdFiles = findMarkdownFiles(tempDir);
    const broken = validateMarkdownLinks(tempDir, mdFiles);

    assert.strictEqual(broken.length, 2);
    assert.strictEqual(broken[0].link, './target.md#section-two');
    assert.match(broken[0].reason, /section-two/);
    assert.strictEqual(broken[1].link, '#missing-header');
    assert.match(broken[1].reason, /missing-header/);
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
