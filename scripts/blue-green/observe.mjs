import { parseArgs, logStep } from './lib/common.mjs';
import { probeEndpoint } from './health-check.mjs';

/**
 * Observe: Post-cutover observation window.
 * In execute mode, requires a real metrics source (METRICS_URL).
 * Fails-closed with BLOCKED if no metrics provider is configured.
 */
export async function runObserve(options = {}) {
  const flags = parseArgs(process.argv.slice(2), options);
  const targetColor = (flags.color || 'green').toLowerCase();
  const observationDurationSec = options.durationSec || 5;

  logStep('OBSERVE', 'RUNNING', `Observing slot '${targetColor}' post-cutover metrics (${observationDurationSec}s window)...`);

  if (flags.dryRun) {
    logStep('OBSERVE', 'DRY-RUN', `[SIMULATED] Monitoring error rate, p95 latency, and worker status for slot '${targetColor}'`);
    logStep('OBSERVE', 'PASS', `Dry-run simulated observation window completed for slot '${targetColor}'.`);
    return { success: true, dryRun: true, status: 'SIMULATED' };
  }

  // Execute mode requires real metrics source
  const metricsUrl = options.metricsUrl || process.env.METRICS_URL;

  if (!metricsUrl && !options.fetchMetrics) {
    const errorMsg = 'BLOCKED: No metrics provider configured (METRICS_URL is not set). Refusing to report PASS without real metrics.';
    logStep('OBSERVE', 'FAIL', errorMsg);
    return {
      success: false,
      status: 'BLOCKED',
      error: errorMsg,
    };
  }

  try {
    let metrics;
    if (options.fetchMetrics) {
      metrics = await options.fetchMetrics(targetColor);
    } else {
      const probeRes = await probeEndpoint(metricsUrl);
      if (!probeRes.ok) {
        const errorMsg = `Failed to fetch metrics from ${metricsUrl}: status ${probeRes.status}`;
        logStep('OBSERVE', 'FAIL', errorMsg);
        return { success: false, error: errorMsg };
      }
      metrics = probeRes.data;
    }

    const errorRate = Number(metrics.errorRate ?? 0);
    const p95LatencyMs = Number(metrics.p95LatencyMs ?? 0);
    const maxErrorRate = options.maxErrorRate ?? 0.0005; // 0.05%
    const maxLatencyMs = options.maxLatencyMs ?? 500;

    logStep('OBSERVE', 'RUNNING', `Metrics: errorRate=${(errorRate * 100).toFixed(3)}%, p95Latency=${p95LatencyMs}ms`);

    if (errorRate > maxErrorRate) {
      const errorMsg = `Error rate ${(errorRate * 100).toFixed(3)}% exceeded threshold ${(maxErrorRate * 100).toFixed(3)}%`;
      logStep('OBSERVE', 'FAIL', errorMsg);
      return { success: false, error: errorMsg, metrics };
    }

    if (p95LatencyMs > maxLatencyMs) {
      const errorMsg = `P95 latency ${p95LatencyMs}ms exceeded threshold ${maxLatencyMs}ms`;
      logStep('OBSERVE', 'FAIL', errorMsg);
      return { success: false, error: errorMsg, metrics };
    }

    logStep('OBSERVE', 'PASS', `Post-cutover observation verified: metrics within normal thresholds.`);
    return { success: true, dryRun: false, metrics };
  } catch (err) {
    const errorMsg = `Observation failed: ${err.message}`;
    logStep('OBSERVE', 'FAIL', errorMsg);
    return { success: false, error: errorMsg };
  }
}

if (process.argv[1] && process.argv[1].endsWith('observe.mjs')) {
  const result = await runObserve();
  if (!result.success) {
    process.exit(1);
  }
}
