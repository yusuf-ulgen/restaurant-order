import { spawn } from 'node:child_process';

/**
 * CommandRunner: Secure child_process wrapper.
 * Prevents shell injection by using array-based arguments (shell: false).
 * Sanitizes sensitive secrets from logs, stdout, and stderr.
 * Supports dependency injection of fake runners for reliable unit testing.
 */
export class CommandRunner {
  constructor(options = {}) {
    this.spawnFn = options.spawnFn || spawn;
    this.secretMasks = new Set(options.secretMasks || []);
  }

  addSecret(secret) {
    if (secret && typeof secret === 'string' && secret.length >= 4) {
      this.secretMasks.add(secret);
    }
  }

  maskSecrets(text) {
    if (!text || typeof text !== 'string') return text;
    let sanitized = text;

    for (const secret of this.secretMasks) {
      sanitized = sanitized.split(secret).join('***REDACTED***');
    }

    // Generic secret patterns (passwords, tokens, jwt, credentials)
    sanitized = sanitized
      .replace(/(password|passwd|pwd|jwt_secret|secret|token|apikey|authorization)=([^&\s]+)/gi, '$1=***REDACTED***')
      .replace(/(postgres|redis):\/\/([^:]+):([^@]+)@/gi, '$1://$2:***REDACTED***@');

    return sanitized;
  }

  async run(command, args = [], options = {}) {
    return new Promise((resolve) => {
      const sanitizedCmd = `${command} ${args.join(' ')}`;
      const timeoutMs = options.timeoutMs || 120000;

      let stdout = '';
      let stderr = '';

      let child;
      try {
        child = this.spawnFn(command, args, {
          cwd: options.cwd || process.cwd(),
          env: { ...process.env, ...(options.env || {}) },
          stdio: ['pipe', 'pipe', 'pipe'],
          shell: false,
        });
      } catch (err) {
        return resolve({
          success: false,
          exitCode: -1,
          stdout: '',
          stderr: this.maskSecrets(err.message),
          command: this.maskSecrets(sanitizedCmd),
        });
      }

      let timeoutTimer = null;
      if (timeoutMs > 0) {
        timeoutTimer = setTimeout(() => {
          try {
            child.kill('SIGTERM');
          } catch {
            // Ignore kill error if already exited
          }
          resolve({
            success: false,
            exitCode: -1,
            stdout: this.maskSecrets(stdout),
            stderr: 'Command timed out after ' + timeoutMs + 'ms',
            command: this.maskSecrets(sanitizedCmd),
          });
        }, timeoutMs);
      }

      if (child.stdout) {
        child.stdout.on('data', (chunk) => {
          stdout += chunk.toString();
        });
      }

      if (child.stderr) {
        child.stderr.on('data', (chunk) => {
          stderr += chunk.toString();
        });
      }

      child.on('error', (err) => {
        if (timeoutTimer) clearTimeout(timeoutTimer);
        resolve({
          success: false,
          exitCode: -1,
          stdout: this.maskSecrets(stdout),
          stderr: this.maskSecrets(err.message),
          command: this.maskSecrets(sanitizedCmd),
        });
      });

      child.on('close', (exitCode) => {
        if (timeoutTimer) clearTimeout(timeoutTimer);
        resolve({
          success: exitCode === 0,
          exitCode: exitCode ?? 0,
          stdout: this.maskSecrets(stdout),
          stderr: this.maskSecrets(stderr),
          command: this.maskSecrets(sanitizedCmd),
        });
      });
    });
  }
}

/**
 * FakeCommandRunner for unit testing without invoking OS processes.
 */
export class FakeCommandRunner {
  constructor(handler) {
    this.calls = [];
    this.handler = handler || (() => ({ success: true, exitCode: 0, stdout: '', stderr: '' }));
  }

  async run(command, args = [], options = {}) {
    const callRecord = { command, args, options, timestamp: new Date().toISOString() };
    this.calls.push(callRecord);

    const result = await this.handler(command, args, options);
    return {
      success: result.exitCode === 0 || result.success === true,
      exitCode: result.exitCode ?? (result.success === false ? 1 : 0),
      stdout: result.stdout || '',
      stderr: result.stderr || '',
      command: `${command} ${args.join(' ')}`,
    };
  }
}

export const defaultCommandRunner = new CommandRunner();
