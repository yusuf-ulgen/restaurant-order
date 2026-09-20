import fs from 'node:fs';
import path from 'node:path';
import { parseArgs, logStep } from './lib/common.mjs';

/**
 * Config Validation: Validates Docker Compose files and environment contracts.
 */
export function runConfigValidate(options = {}) {
  const flags = { ...parseArgs(), ...options };
  const rootDir = process.cwd();
  const errors = [];

  logStep('CONFIG-VALIDATE', 'RUNNING', 'Validating environment templates and compose configs...');

  // 1. Validate Root and App .env.example files
  const requiredEnvFiles = [
    '.env.example',
    'apps/api/.env.example',
    'apps/worker/.env.example',
    'apps/customer-web/.env.example',
    'apps/operations-web/.env.example',
    'apps/admin-web/.env.example',
  ];

  for (const envFile of requiredEnvFiles) {
    const fullPath = path.join(rootDir, envFile);
    if (!fs.existsSync(fullPath)) {
      errors.push(`Missing environment contract: ${envFile}`);
      continue;
    }

    const content = fs.readFileSync(fullPath, 'utf8');

    // Verify frontend envs don't leak secrets
    if (envFile.includes('web')) {
      const lines = content.split('\n');
      for (const line of lines) {
        const trimmed = line.trim();
        if (trimmed && !trimmed.startsWith('#') && trimmed.includes('=')) {
          const varName = trimmed.split('=')[0].trim();
          if (!varName.startsWith('VITE_')) {
            errors.push(`Frontend contract ${envFile} contains non-public variable: ${varName}. Only VITE_ variables are permitted.`);
          }
        }
      }
    }
  }

  // 2. Validate Compose File Content Constraints
  const prodComposeFiles = [
    'compose.prod.blue.yml',
    'compose.prod.green.yml',
  ];

  for (const prodFile of prodComposeFiles) {
    const fullPath = path.join(rootDir, prodFile);
    if (fs.existsSync(fullPath)) {
      const content = fs.readFileSync(fullPath, 'utf8');
      // Ensure production compose does not expose raw DB or Redis ports
      if (content.includes('5432:5432') || content.includes('6379:6379')) {
        errors.push(`Production compose file ${prodFile} exposes public DB/Redis ports!`);
      }
    }
  }

  if (errors.length > 0) {
    for (const err of errors) {
      logStep('CONFIG-VALIDATE', 'FAIL', err);
    }
    return { success: false, errors };
  }

  logStep('CONFIG-VALIDATE', 'PASS', 'All configuration contracts and security constraints validated.');
  return { success: true };
}

if (process.argv[1] && process.argv[1].endsWith('config-validate.mjs')) {
  const result = runConfigValidate();
  if (!result.success) {
    process.exit(1);
  }
}
