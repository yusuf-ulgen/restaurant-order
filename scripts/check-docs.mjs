#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export const MANDATORY_DOCS = [
  'AGENTS.md',
  'README.md',
  'docs/README.md',
  'docs/PRODUCT.md',
  'docs/DOMAIN.md',
  'docs/GLOSSARY.md',
  'docs/ARCHITECTURE.md',
  'docs/REPOSITORY-STRUCTURE.md',
  'docs/ROLES-AND-PERMISSIONS.md',
  'docs/SCREEN-INVENTORY.md',
  'docs/STATE-MACHINES.md',
  'docs/NEGATIVE-FLOWS.md',
  'docs/MULTI-TENANCY.md',
  'docs/PAYMENTS-TIPS-COMMISSIONS.md',
  'docs/ORDER-ROUTING-AND-PRINTING.md',
  'docs/REALTIME-AND-NOTIFICATIONS.md',
  'docs/TESTING.md',
  'docs/SECURITY.md',
  'docs/ENVIRONMENTS.md',
  'docs/DELIVERY.md',
  'docs/BLUE-GREEN-RUNBOOK.md',
  'docs/INCIDENT-RESPONSE.md',
  'docs/ROADMAP.md',
  'docs/adr/README.md',
  'docs/runbooks/README.md',
  'docs/templates/ADR-TEMPLATE.md',
  'docs/templates/FEATURE-TEMPLATE.md',
  'docs/templates/INCIDENT-TEMPLATE.md'
];

export const AGENT_ADAPTERS = [
  '.AGENT.md',
  'CLAUDE.md',
  'GEMINI.md',
  '.claude/README.md',
  '.gemini/README.md',
  '.gpt/INSTRUCTIONS.md',
  '.muse/INSTRUCTIONS.md',
  '.github/copilot-instructions.md'
];

const IGNORED_DIRS = new Set([
  '.git',
  'node_modules',
  'bin',
  'obj',
  'dist',
  'coverage',
  'test-results'
]);

export function findMarkdownFiles(dir, fileList = []) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (!IGNORED_DIRS.has(entry.name)) {
        findMarkdownFiles(fullPath, fileList);
      }
    } else if (entry.isFile() && entry.name.endsWith('.md')) {
      fileList.push(fullPath);
    }
  }
  return fileList;
}

export function generateGitHubSlug(text) {
  return text
    .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1') // remove markdown links
    .replace(/`([^`]+)`/g, '$1') // remove inline code
    .replace(/[*_~]/g, '') // remove formatting
    .toLowerCase()
    .trim()
    .replace(/[^\p{L}\p{N}\s-]/gu, '') // remove punctuation except whitespace and hyphens
    .replace(/\s+/g, '-'); // whitespace to hyphens
}

export function extractMarkdownAnchors(content) {
  const anchors = new Set();
  const slugCounts = new Map();

  const lines = content.split('\n');
  for (const line of lines) {
    const headingMatch = /^#{1,6}\s+(.+)$/.exec(line.trim());
    if (headingMatch) {
      const headingText = headingMatch[1].trim();
      const rawSlug = generateGitHubSlug(headingText);
      const count = slugCounts.get(rawSlug) || 0;
      slugCounts.set(rawSlug, count + 1);

      const finalSlug = count === 0 ? rawSlug : `${rawSlug}-${count}`;
      anchors.add(finalSlug);
      anchors.add(rawSlug);
    }

    const htmlAnchorRegex = /<(?:a|span|div)[^>]*(?:id|name)=["']([^"']+)["']/gi;
    let htmlMatch;
    while ((htmlMatch = htmlAnchorRegex.exec(line)) !== null) {
      anchors.add(htmlMatch[1].toLowerCase());
    }
  }

  return anchors;
}

export function validateMarkdownLinks(rootDir, mdFiles) {
  const brokenLinks = [];
  const linkRegex = /\[([^\]]+)\]\(([^)]+)\)/g;
  const fileAnchorsCache = new Map();

  function getAnchorsForFile(filePath) {
    if (!fileAnchorsCache.has(filePath)) {
      try {
        const content = fs.readFileSync(filePath, 'utf8');
        fileAnchorsCache.set(filePath, extractMarkdownAnchors(content));
      } catch {
        fileAnchorsCache.set(filePath, new Set());
      }
    }
    return fileAnchorsCache.get(filePath);
  }

  for (const file of mdFiles) {
    const content = fs.readFileSync(file, 'utf8');
    let match;

    while ((match = linkRegex.exec(content)) !== null) {
      const rawLink = match[2].trim();

      // Skip external web links and mailto
      if (rawLink.startsWith('http://') || rawLink.startsWith('https://') || rawLink.startsWith('mailto:')) {
        continue;
      }

      // 1. Forbid 'file://' or 'file:' links
      if (/^file:\/\//i.test(rawLink) || /^file:/i.test(rawLink)) {
        brokenLinks.push({
          source: path.relative(rootDir, file),
          link: rawLink,
          reason: "Forbidden 'file://' URI scheme detected. All internal links must be relative."
        });
        continue;
      }

      // 2. Forbid Windows absolute paths (e.g. D:/..., C:\..., \\...)
      if (/^[a-zA-Z]:[\\/]/.test(rawLink) || rawLink.startsWith('\\\\')) {
        brokenLinks.push({
          source: path.relative(rootDir, file),
          link: rawLink,
          reason: 'Forbidden Windows absolute path detected. Links must be relative.'
        });
        continue;
      }

      const [targetPart, anchorPart] = rawLink.split('#');

      // 3. Forbid POSIX absolute root paths (e.g. /docs/...)
      if (targetPart.startsWith('/') || targetPart.startsWith('\\')) {
        brokenLinks.push({
          source: path.relative(rootDir, file),
          link: rawLink,
          reason: 'Forbidden absolute root path detected. Use relative paths.'
        });
        continue;
      }

      // Resolve target file
      let targetPath = '';
      if (!targetPart) {
        targetPath = file;
      } else {
        targetPath = path.resolve(path.dirname(file), targetPart);
      }

      // 4. Forbid links escaping repository root
      const relToRoot = path.relative(rootDir, targetPath);
      if (relToRoot.startsWith('..') || path.isAbsolute(relToRoot)) {
        brokenLinks.push({
          source: path.relative(rootDir, file),
          link: rawLink,
          reason: 'Link escapes repository root boundary.'
        });
        continue;
      }

      // 5. Verify target exists
      if (!fs.existsSync(targetPath)) {
        brokenLinks.push({
          source: path.relative(rootDir, file),
          link: rawLink,
          resolved: targetPath,
          reason: 'Target file or directory does not exist.'
        });
        continue;
      }

      // 6. Verify anchor if specified and target is a markdown file
      if (anchorPart && targetPath.endsWith('.md')) {
        const anchors = getAnchorsForFile(targetPath);
        const normalizedAnchor = anchorPart.toLowerCase().trim();
        if (!anchors.has(normalizedAnchor)) {
          brokenLinks.push({
            source: path.relative(rootDir, file),
            link: rawLink,
            resolved: targetPath,
            reason: `Anchor '#${anchorPart}' was not found in target Markdown document.`
          });
        }
      }
    }
  }

  return brokenLinks;
}

export function validateMandatoryDocs(rootDir, mandatoryList = MANDATORY_DOCS) {
  const missing = [];
  for (const doc of mandatoryList) {
    const fullPath = path.join(rootDir, doc);
    if (!fs.existsSync(fullPath)) {
      missing.push(doc);
    }
  }
  return missing;
}

export function validateAgentAdapters(rootDir, adapters = AGENT_ADAPTERS) {
  const invalid = [];
  for (const adapter of adapters) {
    const fullPath = path.join(rootDir, adapter);
    if (!fs.existsSync(fullPath)) {
      invalid.push({ adapter, reason: 'File does not exist' });
      continue;
    }

    const content = fs.readFileSync(fullPath, 'utf8');
    if (!content.includes('AGENTS.md')) {
      invalid.push({ adapter, reason: 'Does not reference AGENTS.md' });
    }
  }
  return invalid;
}

export function runDocValidation(rootDir) {
  const mdFiles = findMarkdownFiles(rootDir);
  const brokenLinks = validateMarkdownLinks(rootDir, mdFiles);
  const missingDocs = validateMandatoryDocs(rootDir);
  const invalidAdapters = validateAgentAdapters(rootDir);

  return {
    totalScanned: mdFiles.length,
    brokenLinks,
    missingDocs,
    invalidAdapters,
    isValid: brokenLinks.length === 0 && missingDocs.length === 0 && invalidAdapters.length === 0
  };
}

// CLI execution
if (process.argv[1] === __filename) {
  const rootDir = path.resolve(__dirname, '..');
  console.log('=== Running Documentation & Link Integrity Gate ===');

  const result = runDocValidation(rootDir);

  let hasError = false;

  if (result.missingDocs.length > 0) {
    hasError = true;
    console.error(`\n[FAILED] Missing mandatory base documents (${result.missingDocs.length}):`);
    for (const doc of result.missingDocs) {
      console.error(`  - ${doc}`);
    }
  }

  if (result.invalidAdapters.length > 0) {
    hasError = true;
    console.error(`\n[FAILED] Invalid agent adapter files (${result.invalidAdapters.length}):`);
    for (const item of result.invalidAdapters) {
      console.error(`  - ${item.adapter}: ${item.reason}`);
    }
  }

  if (result.brokenLinks.length > 0) {
    hasError = true;
    console.error(`\n[FAILED] Broken Markdown links found (${result.brokenLinks.length}):`);
    for (const item of result.brokenLinks) {
      console.error(`  - In ${item.source}: "${item.link}" -> Not found`);
    }
  }

  if (hasError) {
    process.exit(1);
  }

  console.log(`\n[PASS] All ${result.totalScanned} Markdown files validated:`);
  console.log('  ✓ Zero broken relative links');
  console.log(`  ✓ All ${MANDATORY_DOCS.length} mandatory base documents present`);
  console.log(`  ✓ All ${AGENT_ADAPTERS.length} agent adapters properly redirect to AGENTS.md`);
  process.exit(0);
}
