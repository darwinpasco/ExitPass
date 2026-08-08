import { afterEach, describe, expect, it, vi } from "vitest";
import {
  createHumanAuthenticationClient,
  HumanAuthenticationError,
  humanAuthenticationRoutes,
  operatorConsoleAudience
} from "./humanAuthentication";

afterEach(() => {
  vi.restoreAllMocks();
});

describe("Operator Console I-020 authentication client", () => {
  it("logs in with username and password for the Operator Console audience without TOTP", async () => {
    const fetchMock = vi.fn(async () => authenticationResponse(sessionDto(), { csrf: "csrf-login" }));
    const client = createHumanAuthenticationClient({ fetchImpl: fetchMock as typeof fetch });

    const session = await client.login("ordinary.operator", "operator-password");

    expect(session.audience).toBe(operatorConsoleAudience);
    expect(session.mfaRequired).toBe(false);
    expect(fetchMock).toHaveBeenCalledWith(
      humanAuthenticationRoutes.login,
      expect.objectContaining({ method: "POST", credentials: "same-origin", cache: "no-store" })
    );
    const calls = fetchMock.mock.calls as unknown as Array<[string, RequestInit]>;
    const body = JSON.parse(calls[0][1].body as string);
    expect(body).toEqual({
      username: "ordinary.operator",
      password: "operator-password",
      audience: operatorConsoleAudience
    });
    expect(body).not.toHaveProperty("totpCode");
  });

  it("rediscovers the current session and keeps CSRF only for the logout request", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(authenticationResponse(sessionDto(), { csrf: "csrf-current" }))
      .mockResolvedValueOnce(authenticationResponse(null));
    const client = createHumanAuthenticationClient({ fetchImpl: fetchMock as typeof fetch });

    await client.getCurrentSession();
    await client.logout();

    expect(fetchMock.mock.calls[0][0]).toBe(humanAuthenticationRoutes.session);
    const logoutInit = fetchMock.mock.calls[1][1] as RequestInit;
    expect(fetchMock.mock.calls[1][0]).toBe(humanAuthenticationRoutes.logout);
    expect(logoutInit.credentials).toBe("same-origin");
    expect(logoutInit.headers).toEqual(expect.objectContaining({ "X-CSRF-Token": "csrf-current" }));
    expect(window.localStorage).toHaveLength(0);
    expect(window.sessionStorage).toHaveLength(0);
  });

  it("fails closed when logout has no bounded runtime CSRF token", async () => {
    const fetchMock = vi.fn();
    const client = createHumanAuthenticationClient({ fetchImpl: fetchMock as typeof fetch });

    await expect(client.logout()).rejects.toEqual(
      expect.objectContaining({ kind: "malformed" })
    );
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    [401, "INVALID_CREDENTIALS", "invalid-credentials", "The username or password could not be verified."],
    [429, "AUTHENTICATION_THROTTLED", "throttled", "Sign-in attempts are temporarily limited. Wait and try again."],
    [401, "SESSION_EXPIRED", "session-expired", "Your session expired. Sign in again."],
    [401, "SESSION_REVOKED", "session-revoked", "Your session ended. Sign in again."]
  ])("maps %s %s to a controlled state", async (status, errorCode, kind, message) => {
    const fetchMock = vi.fn(async () => failureResponse(status, errorCode));
    const client = createHumanAuthenticationClient({ fetchImpl: fetchMock as typeof fetch });

    await expect(client.login("operator", "wrong")).rejects.toEqual(
      expect.objectContaining({ kind, message })
    );
  });

  it("does not introduce a TOTP input path when an unexpected MFA response is returned", async () => {
    const client = createHumanAuthenticationClient({
      fetchImpl: vi.fn(async () => failureResponse(401, "TOTP_REQUIRED")) as typeof fetch
    });

    await expect(client.login("operator", "password")).rejects.toEqual(
      expect.objectContaining({ kind: "unexpected-mfa" })
    );
  });

  it("rejects another audience, APT tokens, malformed payloads, and transport failures safely", async () => {
    const wrongAudience = createHumanAuthenticationClient({
      fetchImpl: vi.fn(async () => authenticationResponse({ ...sessionDto(), audience: "MANAGEMENT_PLATFORM" })) as typeof fetch
    });
    await expect(wrongAudience.getCurrentSession()).rejects.toEqual(expect.objectContaining({ kind: "malformed" }));

    const aptToken = createHumanAuthenticationClient({
      fetchImpl: vi.fn(async () => authenticationResponse(sessionDto(), { aptSessionToken: "secret" })) as typeof fetch
    });
    await expect(aptToken.getCurrentSession()).rejects.toEqual(expect.objectContaining({ kind: "malformed" }));

    const malformed = createHumanAuthenticationClient({
      fetchImpl: vi.fn(async () => new Response("not-json", { status: 200 })) as typeof fetch
    });
    await expect(malformed.getCurrentSession()).rejects.toEqual(expect.objectContaining({ kind: "malformed" }));

    const unavailable = createHumanAuthenticationClient({
      fetchImpl: vi.fn(async () => { throw new Error("internal host"); }) as typeof fetch
    });
    await expect(unavailable.getCurrentSession()).rejects.toEqual(
      expect.objectContaining({ kind: "unavailable", message: expect.not.stringContaining("internal host") })
    );
  });
});

function sessionDto() {
  return {
    sessionReference: "11000000-0000-0000-0000-000000000001",
    userReference: "12000000-0000-0000-0000-000000000001",
    username: "ordinary.operator",
    displayName: "Ordinary Operator",
    audience: operatorConsoleAudience,
    assurance: "PASSWORD",
    privilegedAccount: false,
    passwordChangeRequired: false,
    mfaRequired: false,
    mfaSatisfied: false,
    authenticatedAt: "2026-08-08T08:00:00+08:00",
    lastSeenAt: "2026-08-08T08:05:00+08:00",
    idleExpiresAt: "2099-08-08T08:35:00+08:00",
    absoluteExpiresAt: "2099-08-08T16:00:00+08:00",
    permissions: ["statutory-discounts.decision.approve"],
    siteReferences: ["13000000-0000-0000-0000-000000000001"],
    siteGroupReferences: ["14000000-0000-0000-0000-000000000001"],
    hasGlobalScope: false,
    deviceServiceIdentityReference: null,
    correlationId: "15000000-0000-0000-0000-000000000001"
  };
}

function authenticationResponse(
  session: ReturnType<typeof sessionDto> | null,
  options: { csrf?: string; aptSessionToken?: string } = {}
) {
  return new Response(JSON.stringify({
    outcome: session ? "AUTHENTICATED" : "LOGGED_OUT",
    authenticated: session !== null,
    session,
    aptSessionToken: options.aptSessionToken ?? null,
    errorCode: null,
    retryable: false,
    correlationId: "15000000-0000-0000-0000-000000000001"
  }), {
    status: 200,
    headers: {
      "Content-Type": "application/json",
      ...(options.csrf ? { "X-CSRF-Token": options.csrf } : {})
    }
  });
}

function failureResponse(status: number, errorCode: string) {
  return new Response(JSON.stringify({
    outcome: "REJECTED",
    authenticated: false,
    session: null,
    aptSessionToken: null,
    errorCode,
    retryable: status >= 500 || status === 429,
    correlationId: "15000000-0000-0000-0000-000000000001"
  }), { status, headers: { "Content-Type": "application/json" } });
}
