export interface UserPrincipalDto {
  userId: string;
  email: string;
  role: string;
  tenantId?: string | null;
  branchId?: string | null;
  securityVersion: number;
}

export interface SessionDto {
  id: string;
  authMethod: string;
  sessionState: string;
  createdAtUtc: string;
  lastActivityUtc: string;
  expiresAtUtc: string;
  isCurrent: boolean;
}

export interface AuthResultDto {
  user: UserPrincipalDto;
  session: SessionDto;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
}

export interface TerminalContextDto {
  terminalId: string;
  tenantId: string;
  branchId: string;
  terminalName: string;
  deviceIdentifier: string;
  isActive: boolean;
}

export interface ActivateTerminalResultDto {
  terminalId: string;
  deviceSecret: string;
  tenantId: string;
  branchId: string;
  terminalName: string;
}

/**
 * Manages single-flight concurrent token refresh requests.
 * Prevents multiple simultaneous refresh calls when several API requests return 401.
 */
export class SingleFlightRefreshQueue {
  private inFlightPromise: Promise<boolean> | null = null;

  public async executeRefresh(refreshFn: () => Promise<boolean>): Promise<boolean> {
    if (this.inFlightPromise) {
      return this.inFlightPromise;
    }

    this.inFlightPromise = (async () => {
      try {
        return await refreshFn();
      } finally {
        this.inFlightPromise = null;
      }
    })();

    return this.inFlightPromise;
  }
}
