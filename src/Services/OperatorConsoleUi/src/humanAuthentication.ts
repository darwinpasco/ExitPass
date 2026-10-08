export const operatorConsoleAudience = "OPERATOR_CONSOLE";

export const humanAuthenticationRoutes = {
  login: "/v1/human-authentication/login",
  session: "/v1/human-authentication/session",
  logout: "/v1/human-authentication/logout",
  passwordChange: "/v1/human-authentication/password/change",
  establishDeviceBinding: "/v1/operator-console/device-binding/establish",
  bindDeviceSession: "/v1/operator-console/device-binding/bind-session"
} as const;

export interface ChangePasswordCommand {
  currentPassword: string;
  totpCode: string;
  newPassword: string;
}

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
  roleCodes?: string[];
  siteReferences: string[];
  siteGroupReferences: string[];
  hasGlobalScope: boolean;
  operatorDeviceBindingReference?: string;
  operatorShiftReference?: string;
  effectiveSiteReference?: string;
  effectiveSiteGroupReference?: string;
  correlationId: string;
}

export type AuthenticationErrorKind =
  | "unauthenticated"
  | "invalid-credentials"
  | "throttled"
  | "session-expired"
  | "session-revoked"
  | "account-action-required"
  | "temporary-password-expired"
  | "current-password-invalid"
  | "totp-required"
  | "totp-invalid"
  | "password-policy"
  | "unexpected-mfa"
  | "operating-context"
  | "unavailable"
  | "malformed";

export class HumanAuthenticationError extends Error {
  constructor(
    public readonly kind: AuthenticationErrorKind,
    message: string,
    public readonly retryable = false,
    public readonly supportReference?: string,
    public readonly errorCode?: string
  ) {
    super(message);
    this.name = "HumanAuthenticationError";
  }
}

export interface HumanAuthenticationClient {
  login(username: string, password: string): Promise<OperatorConsoleHumanSession>;
  getCurrentSession(): Promise<OperatorConsoleHumanSession>;
  changePassword(command: ChangePasswordCommand): Promise<void>;
  establishDeviceBinding(proof: string): Promise<void>;
  bindDeviceSession(): Promise<OperatorConsoleHumanSession>;
  logout(): Promise<void>;
  clearRuntimeState(): void;
  getCsrfToken(): string | null;
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

  async function send(route: string, init: RequestInit): Promise<Response> {
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
    if (responseCsrfToken) csrfToken = responseCsrfToken;
    return response;
  }

  async function request(
    route: string,
    init: RequestInit,
    operation: "authentication" | "password-change" = "authentication"
  ): Promise<AuthenticationResponseDto> {
    const response = await send(route, init);

    const dto = await parseAuthenticationResponse(response);
    if (!response.ok) {
      throw mapAuthenticationFailure(response.status, dto, operation);
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

    async changePassword(command) {
      if (!csrfToken) {
        throw new HumanAuthenticationError(
          "malformed",
          "The secure password-change request could not be prepared. Return to sign in and try again."
        );
      }

      const dto = await request(humanAuthenticationRoutes.passwordChange, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-CSRF-Token": csrfToken
        },
        body: JSON.stringify(command)
      }, "password-change");
      if (dto.outcome !== "PASSWORD_CHANGED" || dto.authenticated !== false || dto.session != null) {
        throw malformedSession();
      }
      csrfToken = null;
    },

    async establishDeviceBinding(proof) {
      const response = await send(humanAuthenticationRoutes.establishDeviceBinding, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ proof })
      });
      if (!response.ok) {
        throw mapOperatingContextFailure(response.status, await parseAuthenticationResponse(response));
      }
      if (response.status !== 204) throw malformedSession();
    },

    async bindDeviceSession() {
      if (!csrfToken) {
        throw new HumanAuthenticationError(
          "malformed",
          "The secure operating-context request could not be prepared. Sign in again."
        );
      }
      const response = await send(humanAuthenticationRoutes.bindDeviceSession, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-CSRF-Token": csrfToken
        },
        body: "{}"
      });
      if (!response.ok) {
        throw mapOperatingContextFailure(response.status, await parseAuthenticationResponse(response));
      }
      if (response.status !== 204) throw malformedSession();
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
    },

    getCsrfToken() {
      return csrfToken;
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
    (session.roleCodes !== undefined && !isStringArray(session.roleCodes)) ||
    !isStringArray(session.siteReferences) ||
    !isStringArray(session.siteGroupReferences) ||
    !isBoolean(session.hasGlobalScope) ||
    !isOptionalString(session.operatorDeviceBindingReference) ||
    !isOptionalString(session.operatorShiftReference) ||
    !isOptionalString(session.effectiveSiteReference) ||
    !isOptionalString(session.effectiveSiteGroupReference) ||
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
    roleCodes: isStringArray(session.roleCodes) ? [...session.roleCodes] : [],
    siteReferences: [...session.siteReferences],
    siteGroupReferences: [...session.siteGroupReferences],
    hasGlobalScope: session.hasGlobalScope,
    operatorDeviceBindingReference: optionalString(session.operatorDeviceBindingReference),
    operatorShiftReference: optionalString(session.operatorShiftReference),
    effectiveSiteReference: optionalString(session.effectiveSiteReference),
    effectiveSiteGroupReference: optionalString(session.effectiveSiteGroupReference),
    correlationId: session.correlationId
  };
}

function mapOperatingContextFailure(status: number, dto: AuthenticationResponseDto) {
  const errorCode = isString(dto.errorCode) ? dto.errorCode.toUpperCase() : "OPERATOR_CONTEXT_UNAVAILABLE";
  const supportReference = isString(dto.correlationId) ? dto.correlationId : undefined;
  const message = errorCode === "OPERATOR_DEVICE_BINDING_REQUIRED"
    ? "This workstation must be provisioned before governed Operator Console actions are available."
    : errorCode === "OPERATOR_ACTIVE_SHIFT_REQUIRED"
      ? "Start an authorized shift before performing governed Operator Console actions."
      : "The trusted Operator Console operating context is not available.";
  return new HumanAuthenticationError(
    status >= 500 ? "unavailable" : "operating-context",
    message,
    status >= 500,
    supportReference,
    errorCode
  );
}

function mapAuthenticationFailure(
  status: number,
  dto: AuthenticationResponseDto,
  operation: "authentication" | "password-change" = "authentication"
) {
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
  if (errorCode === "TEMPORARY_PASSWORD_EXPIRED") {
    return new HumanAuthenticationError(
      "temporary-password-expired",
      "Your temporary password has expired. Use the approved account recovery process or contact an administrator.",
      false,
      supportReference
    );
  }
  if (operation === "password-change" && errorCode === "CURRENT_PASSWORD_INVALID") {
    return new HumanAuthenticationError(
      "current-password-invalid",
      "The current temporary password was not accepted.",
      false,
      supportReference
    );
  }
  if (operation === "password-change" && errorCode === "TOTP_REQUIRED") {
    return new HumanAuthenticationError(
      "totp-required",
      "Enter the current authenticator code.",
      false,
      supportReference
    );
  }
  if (operation === "password-change" && errorCode === "TOTP_INVALID") {
    return new HumanAuthenticationError(
      "totp-invalid",
      "The authenticator code was not accepted.",
      false,
      supportReference
    );
  }
  if (operation === "password-change" && errorCode === "PASSWORD_POLICY_FAILED") {
    return new HumanAuthenticationError(
      "password-policy",
      "The password does not meet the password policy.",
      false,
      supportReference
    );
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

function isOptionalString(value: unknown): boolean {
  return value === undefined || value === null || isString(value);
}

function optionalString(value: unknown): string | undefined {
  return isString(value) ? value : undefined;
}
