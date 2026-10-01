/**
 * Health check response model matching the ASP.NET Core starter endpoints.
 */
export interface HealthStatusResponse {
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  timestamp: string;
  service: string;
  version: string;
}
export * from './auth';
export * from './api-client';

