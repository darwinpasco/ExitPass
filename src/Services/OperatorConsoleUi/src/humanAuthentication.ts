export const operatorConsoleAudience = "OPERATOR_CONSOLE";

export const humanAuthenticationRoutes = {
  login: "/v1/human-authentication/login",
  session: "/v1/human-authentication/session",
  logout: "/v1/human-authentication/logout"
} as const;

export interface OperatorConsoleHumanSession {
  sessionReference: string;
  userReference: string;
  username: string;
  displayName: string;
  audience: typeof operatorConsoleAudience;
  assurance: string;
  privilegedAccount: boolean;
  passwordChangeRequired: boolean;
  mfaRequired: boolean;
  mfaSatisfied: boolean;
  authenticatedAt: string;
  lastSeenAt: string;
  idleExpiresAt: string;
  absoluteExpiresAt: string;
  permissions: string[];
  siteReferences: string[];
  siteGroupReferences: string[];
  hasGlobalScope: boolean;
  correlationId: string;
}

export type AuthenticationErrorKind =
  | "unauthenticated"
  | "invalid-credentials"
  | "throttled"
  | "session-expired"
  | "session-revoked"
  | "account-action-required"
  | "unexpected-mfa"
  | "unavailable"
  | "malformed";

export class HumanAuthenticationError extends Error {
  constructor(
    public readonly kind: AuthenticationErrorKind,
    message: string,
    public readonly retryable = false,
    public readonly supportReference?: string
  ) {
    super(message);
    this.name = "HumanAuthenticationError";
  }
}

export interface HumanAuthenticationClient {
  login(username: string, password: string): Promise<OperatorConsoleHumanSession>;
  getCurrentSession(): Promise<OperatorConsoleHumanSession>;
  logout(): Promise<void>;
  clearRuntimeState(): void;
}

interface AuthenticationResponseDto {
  outcome?: unknown;
  authenticated?: unknown;
  session?: unknown;
  aptSessionToken?: unknown;
  errorCode?: unknown;
  retryable?: unknown;
  correlationId?: unknown;
}

interface HumanAuthenticationClientOptions {
  fetchImpl?: typeof fetch;
}

export function createHumanAuthenticationClient(
  options: HumanAuthenticationClientOptions = {}
): HumanAuthenticationClient {
  const fetchImpl = options.fetchImpl ?? globalThis.fetch.bind(globalThis);
  let csrfToken: string | null = null;

  async function request(route: string, init: RequestInit): Promise<AuthenticationResponseDto> {
    let response: Response;
    try {
      response = await fetchImpl(route, {
        ...init,
        credentials: "same-origin",
        cache: "no-store",
        headers: {
          Accept: "application/json",
          ...init.headers
        }
      });
    } catch {
      throw new HumanAuthenticationError(
        "unavailable",
        "Operator Console authentication is temporarily unavailable. Try again.",
        true
      );
    }

    const responseCsrfToken = response.headers.get("X-CSRF-Token");
    if (responseCsrfToken) {
      csrfToken = responseCsrfToken;
    }

    const dto = await parseAuthenticationResponse(response);
    if (!response.ok) {
      throw mapAuthenticationFailure(response.status, dto);
    }
    return dto;
  }

  return {
    async login(username, password) {
      const dto = await request(humanAuthenticationRoutes.login, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          username,
          password,
          audience: operatorConsoleAudience
        })
      });
      return requireAuthenticatedSession(dto);
    },

    async getCurrentSession() {
      const dto = await request(humanAuthenticationRoutes.session, { method: "GET" });
      return requireAuthenticatedSession(dto);
    },

    async logout() {
      if (!csrfToken) {
        throw new HumanAuthenticationError(
          "malformed",
          "The secure logout request could not be prepared. Refresh the session and try again."
        );
      }

      await request(humanAuthenticationRoutes.logout, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-CSRF-Token": csrfToken
        },
        body: "{}"
      });
      csrfToken = null;
    },

    clearRuntimeState() {
      csrfToken = null;
    }
  };
}

async function parseAuthenticationResponse(response: Response): Promise<AuthenticationResponseDto> {
  try {
    const value = await response.json();
    if (!isRecord(value)) {
      throw new Error("Invalid response object.");
    }
    return value;
  } catch {
    throw new HumanAuthenticationError(
      "malformed",
      "Operator Console authentication returned an unsupported response. Try again.",
      true
    );
  }
}

function requireAuthenticatedSession(dto: AuthenticationResponseDto): OperatorConsoleHumanSession {
  if (dto.aptSessionToken !== null && dto.aptSessionToken !== undefined) {
    throw malformedSession();
  }
  if (dto.authenticated !== true || !isRecord(dto.session)) {
    throw mapAuthenticationFailure(401, dto);
  }

  const session = dto.session;
  if (
    !isString(session.sessionReference) ||
    !isString(session.userReference) ||
    !isString(session.username) ||
    !isString(session.displayName) ||
    session.audience !== operatorConsoleAudience ||
    !isString(session.assurance) ||
    !isBoolean(session.privilegedAccount) ||
    !isBoolean(session.passwordChangeRequired) ||
    !isBoolean(session.mfaRequired) ||
    !isBoolean(session.mfaSatisfied) ||
    !isString(session.authenticatedAt) ||
    !isString(session.lastSeenAt) ||
    !isString(session.idleExpiresAt) ||
    !isString(session.absoluteExpiresAt) ||
    !isStringArray(session.permissions) ||
    !isStringArray(session.siteReferences) ||
    !isStringArray(session.siteGroupReferences) ||
    !isBoolean(session.hasGlobalScope) ||
    !isString(session.correlationId)
  ) {
    throw malformedSession();
  }

  if (session.mfaRequired) {
    throw new HumanAuthenticationError(
      "unexpected-mfa",
      "This Operator Console session requires unsupported authentication assurance. Contact an administrator."
    );
  }

  return {
    sessionReference: session.sessionReference,
    userReference: session.userReference,
    username: session.username,
    displayName: session.displayName,
    audience: operatorConsoleAudience,
    assurance: session.assurance,
    privilegedAccount: session.privilegedAccount,
    passwordChangeRequired: session.passwordChangeRequired,
    mfaRequired: false,
    mfaSatisfied: session.mfaSatisfied,
    authenticatedAt: session.authenticatedAt,
    lastSeenAt: session.lastSeenAt,
    idleExpiresAt: session.idleExpiresAt,
    absoluteExpiresAt: session.absoluteExpiresAt,
    permissions: [...session.permissions],
    siteReferences: [...session.siteReferences],
    siteGroupReferences: [...session.siteGroupReferences],
    hasGlobalScope: session.hasGlobalScope,
    correlationId: session.correlationId
  };
}

function mapAuthenticationFailure(status: number, dto: AuthenticationResponseDto) {
  const errorCode = isString(dto.errorCode) ? dto.errorCode.toUpperCase() : "";
  const retryable = dto.retryable === true;
  const supportReference = isString(dto.correlationId) ? dto.correlationId : undefined;

  if (errorCode === "AUTHENTICATION_THROTTLED") {
    return new HumanAuthenticationError(
      "throttled",
      "Sign-in attempts are temporarily limited. Wait and try again.",
      true,
      supportReference
    );
  }
  if (errorCode === "SESSION_EXPIRED") {
    return new HumanAuthenticationError("session-expired", "Your session expired. Sign in again.", false, supportReference);
  }
  if (errorCode === "SESSION_REVOKED") {
    return new HumanAuthenticationError("session-revoked", "Your session ended. Sign in again.", false, supportReference);
  }
  if (errorCode === "TOTP_REQUIRED" || errorCode === "TOTP_INVALID") {
    return new HumanAuthenticationError(
      "unexpected-mfa",
      "This Operator Console account requires unsupported authentication assurance. Contact an administrator.",
      false,
      supportReference
    );
  }
  if (errorCode === "PASSWORD_CHANGE_REQUIRED" || errorCode === "MFA_ENROLLMENT_REQUIRED") {
    return new HumanAuthenticationError(
      "account-action-required",
      "Your account requires an administrative action before Operator Console access is available.",
      false,
      supportReference
    );
  }
  if (status === 401 && errorCode === "INVALID_CREDENTIALS") {
    return new HumanAuthenticationError(
      "invalid-credentials",
      "The username or password could not be verified.",
      false,
      supportReference
    );
  }
  if (status === 401) {
    return new HumanAuthenticationError("unauthenticated", "Sign in to continue.", false, supportReference);
  }
  if (status === 429) {
    return new HumanAuthenticationError(
      "throttled",
      "Sign-in attempts are temporarily limited. Wait and try again.",
      true,
      supportReference
    );
  }
  if (status >= 500 || retryable) {
    return new HumanAuthenticationError(
      "unavailable",
      "Operator Console authentication is temporarily unavailable. Try again.",
      true,
      supportReference
    );
  }
  return new HumanAuthenticationError(
    "invalid-credentials",
    "The username or password could not be verified.",
    false,
    supportReference
  );
}

function malformedSession() {
  return new HumanAuthenticationError(
    "malformed",
    "Operator Console authentication returned an unsupported response. Try again.",
    true
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isString(value: unknown): value is string {
  return typeof value === "string" && value.length > 0;
}

function isBoolean(value: unknown): value is boolean {
  return typeof value === "boolean";
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === "string");
}
