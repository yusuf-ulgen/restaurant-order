import { parseArgs, logStep } from './lib/common.mjs';

/**
 * Smoke: Automated internal smoke checks against inactive slot.
 * Enforces fail-closed: unimplemented checks are reported as BLOCKED in execute mode.
 */
export async function runSmoke(options = {}) {
  const flags = parseArgs(process.argv.slice(2), options);
  const targetColor = (flags.color || 'green').toLowerCase();

  logStep('SMOKE', 'RUNNING', `Evaluating smoke test readiness for slot '${targetColor}'...`);

  const smokeChecks = [
    { id: 'staff-auth', name: 'Staff PIN authentication verification', implemented: false },
    { id: 'kds-display', name: 'KDS station ticket display health', implemented: false },
    { id: 'printer-queue', name: 'Thermal receipt printer spooler readiness', implemented: false },
    { id: 'tenant-boundary', name: 'Tenant isolation boundary verification', implemented: false },
  ];

  if (flags.dryRun) {
    for (const check of smokeChecks) {
      logStep('SMOKE', 'DRY-RUN', `[SIMULATED] Smoke check: ${check.name} (Simulation)`);
    }
    logStep('SMOKE', 'PASS', `Dry-run simulated smoke suite passed on slot '${targetColor}'.`);
    return { success: true, dryRun: true, status: 'SIMULATED', checksPassed: smokeChecks.length };
  }

  // Execute mode: if real smoke checks are provided via runner
  if (options.smokeRunner) {
    const results = await options.smokeRunner(targetColor);
    if (!results.success) {
      logStep('SMOKE', 'FAIL', `Smoke suite failed on slot '${targetColor}': ${results.error}`);
      return { success: false, error: results.error };
    }
    logStep('SMOKE', 'PASS', `All custom smoke checks passed on slot '${targetColor}'.`);
    return { success: true, dryRun: false, results };
  }

  // Fail-closed in execute mode: Staff, KDS, Printer, and Tenant smoke checks are not yet implemented
  const unimplemented = smokeChecks.filter((c) => !c.implemented).map((c) => c.name);
  const errorMsg = `BLOCKED: Smoke checks are not yet implemented on the API (${unimplemented.join(', ')}). Refusing to report false-positive PASS in execute mode.`;

  logStep('SMOKE', 'FAIL', errorMsg);
  return {
    success: false,
    status: 'BLOCKED',
    error: errorMsg,
    unimplementedChecks: unimplemented,
  };
}

if (process.argv[1] && process.argv[1].endsWith('smoke.mjs')) {
  const result = await runSmoke();
  if (!result.success) {
    process.exit(1);
  }
}
