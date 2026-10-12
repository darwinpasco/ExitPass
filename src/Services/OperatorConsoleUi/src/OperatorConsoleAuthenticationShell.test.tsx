import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { createMockOperatorConsoleApiClient } from "./apiClient";
import { OperatorConsoleAuthenticationShell } from "./OperatorConsoleAuthenticationShell";
import { HumanAuthenticationError, type HumanAuthenticationClient, type OperatorConsoleHumanSession } from "./humanAuthentication";

describe("Operator Console authentication shell", () => {
  it("restores the persisted governed workstation context from current-session readback after reload", async () => {
    const client = authenticationClient({ currentSession: session() });

    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={() => createMockOperatorConsoleApiClient()}
        initialPath="/operator-console"
      />
    );

    expect(screen.getByRole("heading", { name: "Checking your session" })).toBeInTheDocument();
    expect(await screen.findByText("Ordinary Operator")).toBeInTheDocument();
    expect(screen.getByText("ordinary.operator")).toBeInTheDocument();
    expect(screen.getByText("1 Site, 1 Site Group")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Session Lookup" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Governed workstation enrollment package")).not.toBeInTheDocument();
    expect(client.validateDeviceTrust).toHaveBeenCalledTimes(1);
    expect(client.getCurrentSession).toHaveBeenCalledTimes(1);
    expect(client.bindDeviceSession).not.toHaveBeenCalled();
    expect(window.localStorage).toHaveLength(0);
    expect(window.sessionStorage).toHaveLength(0);
  });

  it("performs ordinary password login without presenting TOTP", async () => {
    const client = authenticationClient({
      currentError: new HumanAuthenticationError("unauthenticated", "Sign in to continue."),
      loginSession: session()
    });
    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={() => createMockOperatorConsoleApiClient()}
        initialPath="/operator-console"
      />
    );

    await screen.findByRole("heading", { name: "Staff sign in" });
    expect(screen.queryByLabelText("Governed workstation enrollment package")).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/totp|one-time/i)).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Username"), "ordinary.operator");
    await userEvent.type(screen.getByLabelText("Password"), "operator-password");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("Ordinary Operator")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Session Lookup" })).toBeInTheDocument();
    expect(client.login).toHaveBeenCalledWith("ordinary.operator", "operator-password");
    expect(screen.queryByLabelText(/totp|one-time/i)).not.toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("operator-password");
  });

  it("requires enrollment before staff sign-in and transitions directly to sign-in after trust is established", async () => {
    const client = authenticationClient({
      deviceTrustError: deviceBindingRequired(),
      trustAfterEnrollment: true
    });
    const storageSetItem = vi.spyOn(Storage.prototype, "setItem");
    render(<OperatorConsoleAuthenticationShell authenticationClient={client} />);

    const packageInput = await screen.findByLabelText("Governed workstation enrollment package");
    expect(screen.getByRole("heading", { name: "Trusted workstation required" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Staff sign in" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Session Lookup" })).not.toBeInTheDocument();
    expect(client.getCurrentSession).not.toHaveBeenCalled();
    const enrollmentPackage = JSON.stringify({ provisioningCredential: "one-time-token", deviceType: "OPERATOR_CONSOLE" });
    fireEvent.change(packageInput, { target: { value: enrollmentPackage } });
    await userEvent.click(screen.getByRole("button", { name: "Enroll workstation" }));

    await waitFor(() => expect(client.enrollDevice).toHaveBeenCalledWith(enrollmentPackage));
    expect(await screen.findByRole("heading", { name: "Staff sign in" })).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Governed workstation enrollment completed.");
    expect(screen.queryByLabelText("Governed workstation enrollment package")).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Session Lookup" })).not.toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(enrollmentPackage);
    expect(client.login).not.toHaveBeenCalled();
    expect(client.validateDeviceTrust).toHaveBeenCalledTimes(2);
    expect(storageSetItem).not.toHaveBeenCalled();
    storageSetItem.mockRestore();
  });

  it("offers the existing enrollment flow after credential rotation and keeps staff sign-in blocked until rebinding", async () => {
    const client = authenticationClient({
      deviceTrustError: new HumanAuthenticationError(
        "operating-context",
        "The trusted Operator Console operating context is not available.",
        false,
        "19000000-0000-0000-0000-000000000002",
        "OPERATOR_DEVICE_CREDENTIAL_ROTATION_REQUIRED"
      ),
      trustAfterEnrollment: true
    });
    const storageSetItem = vi.spyOn(Storage.prototype, "setItem");
    render(<OperatorConsoleAuthenticationShell authenticationClient={client} />);

    const packageInput = await screen.findByLabelText("Governed workstation enrollment package");
    expect(screen.getByRole("button", { name: "Enroll workstation" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Staff sign in" })).not.toBeInTheDocument();
    const rotationPackage = JSON.stringify({ provisioningCredential: "rotation-token", deviceType: "OPERATOR_CONSOLE" });
    fireEvent.change(packageInput, { target: { value: rotationPackage } });
    await userEvent.click(screen.getByRole("button", { name: "Enroll workstation" }));

    await waitFor(() => expect(client.enrollDevice).toHaveBeenCalledWith(rotationPackage));
    expect(await screen.findByRole("heading", { name: "Staff sign in" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Governed workstation enrollment package")).not.toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(rotationPackage);
    expect(client.login).not.toHaveBeenCalled();
    expect(storageSetItem).not.toHaveBeenCalled();
    storageSetItem.mockRestore();
  });

  it("offers successor enrollment after replacement and keeps staff sign-in blocked until successor trust is established", async () => {
    const client = authenticationClient({
      deviceTrustError: new HumanAuthenticationError(
        "operating-context",
        "Enroll the approved replacement device before governed Operator Console actions are available.",
        false,
        "19000000-0000-0000-0000-000000000003",
        "OPERATOR_DEVICE_REPLACEMENT_ENROLLMENT_REQUIRED"
      ),
      trustAfterEnrollment: true
    });
    const storageSetItem = vi.spyOn(Storage.prototype, "setItem");
    render(<OperatorConsoleAuthenticationShell authenticationClient={client} />);

    const packageInput = await screen.findByLabelText("Governed workstation enrollment package");
    expect(screen.getByText("Enroll the approved replacement device before governed Operator Console actions are available.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Enroll workstation" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Staff sign in" })).not.toBeInTheDocument();
    const replacementPackage = JSON.stringify({ provisioningCredential: "replacement-token", deviceType: "OPERATOR_CONSOLE" });
    fireEvent.change(packageInput, { target: { value: replacementPackage } });
    await userEvent.click(screen.getByRole("button", { name: "Enroll workstation" }));

    await waitFor(() => expect(client.enrollDevice).toHaveBeenCalledWith(replacementPackage));
    expect(await screen.findByRole("heading", { name: "Staff sign in" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Governed workstation enrollment package")).not.toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(replacementPackage);
    expect(client.login).not.toHaveBeenCalled();
    expect(storageSetItem).not.toHaveBeenCalled();
    storageSetItem.mockRestore();
  });

  it("rebinds an authenticated session through the canonical operating-context endpoint", async () => {
    const unbound = { ...session(), operatorDeviceBindingReference: undefined };
    const client = authenticationClient({ currentSession: unbound, bindSession: session() });
    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={() => createMockOperatorConsoleApiClient()}
      />
    );

    expect(await screen.findByText("Ordinary Operator")).toBeInTheDocument();
    expect(client.bindDeviceSession).toHaveBeenCalledTimes(1);
    expect(screen.queryByText("Operator context required")).not.toBeInTheDocument();
  });

  it("fails closed without rendering the workspace when an authenticated session cannot bind its device context", async () => {
    const unbound = { ...session(), operatorDeviceBindingReference: undefined };
    const client = authenticationClient({
      currentSession: unbound,
      bindError: new HumanAuthenticationError(
        "operating-context",
        "This workstation must be provisioned before governed Operator Console actions are available.",
        false,
        undefined,
        "OPERATOR_DEVICE_BINDING_REQUIRED"
      )
    });
    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={() => createMockOperatorConsoleApiClient()}
      />
    );

    expect(await screen.findByRole("heading", { name: "Trusted workstation required" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Staff sign in" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Session Lookup" })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Governed workstation enrollment package")).toHaveAttribute("type", "password");
  });

  it("fails closed before human-session discovery when the device is suspended or revoked", async () => {
    const client = authenticationClient({
      deviceTrustError: new HumanAuthenticationError(
        "operating-context",
        "The trusted Operator Console operating context is not available.",
        false,
        "19000000-0000-0000-0000-000000000001",
        "OPERATOR_DEVICE_BINDING_REVOKED"
      )
    });

    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={() => createMockOperatorConsoleApiClient()}
      />
    );

    expect(await screen.findByRole("heading", { name: "Trusted workstation required" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Staff sign in" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Governed workstation enrollment package")).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Session Lookup" })).not.toBeInTheDocument();
    expect(client.getCurrentSession).not.toHaveBeenCalled();
    expect(client.login).not.toHaveBeenCalled();
  });

  it("disables login credential autocomplete and presents no persistence option", async () => {
    const client = authenticationClient({
      currentError: new HumanAuthenticationError("unauthenticated", "Sign in to continue.")
    });
    const { container } = render(<OperatorConsoleAuthenticationShell authenticationClient={client} />);

    const username = await screen.findByLabelText("Username");
    const password = screen.getByLabelText("Password");
    expect(username).toHaveAttribute("autocomplete", "off");
    expect(password).toHaveAttribute("autocomplete", "off");
    expect(container.querySelector("form.authenticationForm")).toHaveAttribute("autocomplete", "off");
    expect(screen.queryByText(/remember me|keep me signed in|remember username/i)).not.toBeInTheDocument();
  });

  it("changes the temporary password from the restricted session and returns to sign in", async () => {
    const restricted = restrictedSession();
    const client = authenticationClient({
      currentError: new HumanAuthenticationError("unauthenticated", "Sign in to continue."),
      loginSession: restricted
    });
    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={() => createMockOperatorConsoleApiClient()}
      />
    );

    await screen.findByRole("heading", { name: "Staff sign in" });
    await userEvent.type(screen.getByLabelText("Username"), "JuanDC03");
    await userEvent.type(screen.getByLabelText("Password"), "JuanDC03");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("heading", { name: "Change temporary password" })).toBeInTheDocument();
    expect(screen.queryByText("Ordinary Operator")).not.toBeInTheDocument();
    const currentPassword = screen.getByLabelText("Current temporary password");
    const authenticatorCode = screen.getByLabelText("Authenticator code");
    const replacement = screen.getByLabelText("New password");
    await userEvent.type(currentPassword, "JuanDC03");
    await userEvent.type(authenticatorCode, "123456");
    await userEvent.type(replacement, "seven77");
    await userEvent.click(screen.getByRole("button", { name: "Change password" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Password must be at least 8 characters.");
    expect(client.changePassword).not.toHaveBeenCalled();

    await userEvent.clear(replacement);
    await userEvent.type(replacement, "newpass8");
    await userEvent.click(screen.getByRole("button", { name: "Change password" }));

    await waitFor(() => expect(client.changePassword).toHaveBeenCalledWith({
      currentPassword: "JuanDC03",
      totpCode: "123456",
      newPassword: "newpass8"
    }));
    expect(await screen.findByRole("heading", { name: "Staff sign in" })).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Password changed. Sign in with your new password.");
    expect(screen.queryByLabelText("Current temporary password")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Authenticator code")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("New password")).not.toBeInTheDocument();
    expect(client.clearRuntimeState).toHaveBeenCalled();
  });

  it("keeps the restricted form available after an invalid authenticator code", async () => {
    const client = authenticationClient({
      currentSession: restrictedSession(),
      changePasswordError: new HumanAuthenticationError(
        "totp-invalid",
        "The authenticator code was not accepted."
      )
    });
    render(<OperatorConsoleAuthenticationShell authenticationClient={client} />);

    await screen.findByRole("heading", { name: "Change temporary password" });
    await userEvent.type(screen.getByLabelText("Current temporary password"), "JuanDC03");
    await userEvent.type(screen.getByLabelText("Authenticator code"), "000000");
    await userEvent.type(screen.getByLabelText("New password"), "newpass8");
    await userEvent.click(screen.getByRole("button", { name: "Change password" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The authenticator code was not accepted.");
    expect(screen.getByLabelText("Authenticator code")).toHaveValue("");
    expect(screen.getByRole("heading", { name: "Change temporary password" })).toBeInTheDocument();
  });

  it("shows the governed expiry action without weakening invalid-credential messaging", async () => {
    const client = authenticationClient({
      currentError: new HumanAuthenticationError("unauthenticated", "Sign in to continue."),
      loginError: new HumanAuthenticationError(
        "temporary-password-expired",
        "Your temporary password has expired. Use the approved account recovery process or contact an administrator."
      )
    });
    render(<OperatorConsoleAuthenticationShell authenticationClient={client} />);
    await screen.findByRole("heading", { name: "Staff sign in" });
    await userEvent.type(screen.getByLabelText("Username"), "JuanDC03");
    await userEvent.type(screen.getByLabelText("Password"), "JuanDC03");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Your temporary password has expired.");
  });

  it("keeps invalid credentials anti-enumerating and handles unexpected MFA without adding an input", async () => {
    const client = authenticationClient({
      currentError: new HumanAuthenticationError("unauthenticated", "Sign in to continue."),
      loginError: new HumanAuthenticationError("invalid-credentials", "The username or password could not be verified.")
    });
    render(<OperatorConsoleAuthenticationShell authenticationClient={client} />);
    await screen.findByRole("heading", { name: "Staff sign in" });
    await userEvent.type(screen.getByLabelText("Username"), "unknown.operator");
    await userEvent.type(screen.getByLabelText("Password"), "wrong");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The username or password could not be verified.");
    expect(screen.queryByText(/does not exist|incorrect password/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/totp|one-time/i)).not.toBeInTheDocument();
    expect(client.clearRuntimeState).toHaveBeenCalled();
    expect(screen.getByLabelText("Username")).toHaveValue("unknown.operator");
    expect(screen.getByLabelText("Password")).toHaveValue("");
  });

  it("clears both credentials after login when the authenticated session expires", async () => {
    let onAuthenticationRequired: (() => void) | undefined;
    const client = authenticationClient({
      currentError: new HumanAuthenticationError("unauthenticated", "Sign in to continue."),
      loginSession: session()
    });
    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={(_session, callback) => {
          onAuthenticationRequired = callback;
          return createMockOperatorConsoleApiClient();
        }}
      />
    );

    await screen.findByRole("heading", { name: "Staff sign in" });
    await userEvent.type(screen.getByLabelText("Username"), "ordinary.operator");
    await userEvent.type(screen.getByLabelText("Password"), "operator-password");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));
    expect(await screen.findByText("Ordinary Operator")).toBeInTheDocument();

    act(() => onAuthenticationRequired?.());

    expect(await screen.findByLabelText("Username")).toHaveValue("");
    expect(screen.getByLabelText("Password")).toHaveValue("");
    expect(screen.getByRole("alert")).toHaveTextContent("Your session ended. Sign in again.");
    expect(screen.queryByLabelText("Governed workstation enrollment package")).not.toBeInTheDocument();
  });

  it("keeps device trust after human-session expiry and returns to staff sign-in", async () => {
    const client = authenticationClient({
      currentSession: {
        ...session(),
        idleExpiresAt: new Date(Date.now() - 1).toISOString()
      }
    });

    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={() => createMockOperatorConsoleApiClient()}
      />
    );

    expect(await screen.findByRole("heading", { name: "Staff sign in" })).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Your session expired. Sign in again.");
    expect(screen.queryByLabelText("Governed workstation enrollment package")).not.toBeInTheDocument();
    expect(client.validateDeviceTrust).toHaveBeenCalledTimes(1);
  });

  it("clears both credentials after a successful login and explicit logout", async () => {
    const client = authenticationClient({
      currentError: new HumanAuthenticationError("unauthenticated", "Sign in to continue."),
      loginSession: session()
    });
    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={() => createMockOperatorConsoleApiClient()}
      />
    );

    await screen.findByRole("heading", { name: "Staff sign in" });
    await userEvent.type(screen.getByLabelText("Username"), "ordinary.operator");
    await userEvent.type(screen.getByLabelText("Password"), "operator-password");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));
    await userEvent.click(await screen.findByRole("button", { name: "Sign out" }));

    expect(await screen.findByLabelText("Username")).toHaveValue("");
    expect(screen.getByLabelText("Password")).toHaveValue("");
    expect(screen.queryByLabelText("Governed workstation enrollment package")).not.toBeInTheDocument();
  });

  it("does not write login credentials to browser-managed application storage", async () => {
    const storageSetItem = vi.spyOn(Storage.prototype, "setItem");
    const indexedDbDescriptor = Object.getOwnPropertyDescriptor(window, "indexedDB");
    const indexedDbOpen = vi.fn();
    Object.defineProperty(window, "indexedDB", {
      configurable: true,
      value: { open: indexedDbOpen }
    });
    const client = authenticationClient({
      currentError: new HumanAuthenticationError("unauthenticated", "Sign in to continue."),
      loginError: new HumanAuthenticationError("invalid-credentials", "The username or password could not be verified.")
    });

    render(<OperatorConsoleAuthenticationShell authenticationClient={client} />);
    await screen.findByRole("heading", { name: "Staff sign in" });
    await userEvent.type(screen.getByLabelText("Username"), "ordinary.operator");
    await userEvent.type(screen.getByLabelText("Password"), "operator-password");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));
    await screen.findByRole("alert");

    expect(storageSetItem).not.toHaveBeenCalled();
    expect(indexedDbOpen).not.toHaveBeenCalled();
    expect(window.localStorage).toHaveLength(0);
    expect(window.sessionStorage).toHaveLength(0);
    storageSetItem.mockRestore();
    if (indexedDbDescriptor) {
      Object.defineProperty(window, "indexedDB", indexedDbDescriptor);
    } else {
      Reflect.deleteProperty(window, "indexedDB");
    }
  });

  it("calls server logout before clearing the protected workspace", async () => {
    const client = authenticationClient({ currentSession: session() });
    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={() => createMockOperatorConsoleApiClient()}
        initialPath="/operator-console"
      />
    );

    await userEvent.click(await screen.findByRole("button", { name: "Sign out" }));

    await waitFor(() => expect(client.logout).toHaveBeenCalledTimes(1));
    expect(await screen.findByRole("heading", { name: "Staff sign in" })).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("You have signed out.");
  });

  it("keeps the workspace locked to the current session when server logout is unavailable", async () => {
    const client = authenticationClient({
      currentSession: session(),
      logoutError: new HumanAuthenticationError(
        "unavailable",
        "Operator Console authentication is temporarily unavailable. Try again.",
        true
      )
    });
    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={() => createMockOperatorConsoleApiClient()}
        initialPath="/operator-console"
      />
    );

    await userEvent.click(await screen.findByRole("button", { name: "Sign out" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Operator Console authentication is temporarily unavailable. Try again."
    );
    expect(screen.getByText("Ordinary Operator")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Staff sign in" })).not.toBeInTheDocument();
  });

  it("locks the workspace on a protected-request 401 without replaying a mutation", async () => {
    let onAuthenticationRequired: (() => void) | undefined;
    const client = authenticationClient({ currentSession: session() });
    render(
      <OperatorConsoleAuthenticationShell
        authenticationClient={client}
        createWorkspaceClient={(_session, callback) => {
          onAuthenticationRequired = callback;
          return createMockOperatorConsoleApiClient();
        }}
        initialPath="/operator-console"
      />
    );

    expect(await screen.findByText("Ordinary Operator")).toBeInTheDocument();
    act(() => onAuthenticationRequired?.());

    expect(await screen.findByRole("heading", { name: "Staff sign in" })).toBeInTheDocument();
    expect(screen.queryByText("Ordinary Operator")).not.toBeInTheDocument();
    expect(client.login).not.toHaveBeenCalled();
  });

  it("uses accessible responsive login controls and initial username focus", async () => {
    const client = authenticationClient({
      currentError: new HumanAuthenticationError("unauthenticated", "Sign in to continue.")
    });
    render(<OperatorConsoleAuthenticationShell authenticationClient={client} />);

    const username = await screen.findByLabelText("Username");
    expect(username).toHaveFocus();
    expect(username).toHaveAttribute("autocomplete", "off");
    expect(screen.getByLabelText("Password")).toHaveAttribute("autocomplete", "off");
    expect(screen.getByRole("button", { name: "Sign in" })).toBeDisabled();
  });
});

function authenticationClient(options: {
  deviceTrustError?: Error;
  trustAfterEnrollment?: boolean;
  currentSession?: OperatorConsoleHumanSession;
  currentError?: Error;
  loginSession?: OperatorConsoleHumanSession;
  loginError?: Error;
  bindSession?: OperatorConsoleHumanSession;
  bindError?: Error;
  enrollError?: Error;
  changePasswordError?: Error;
  logoutError?: Error;
}): HumanAuthenticationClient {
  let enrolled = false;
  return {
    validateDeviceTrust: vi.fn(async () => {
      if (options.deviceTrustError && !(options.trustAfterEnrollment && enrolled)) {
        throw options.deviceTrustError;
      }
    }),
    getCurrentSession: vi.fn(async () => {
      if (options.currentError) throw options.currentError;
      return options.currentSession ?? session();
    }),
    login: vi.fn(async () => {
      if (options.loginError) throw options.loginError;
      return options.loginSession ?? session();
    }),
    changePassword: vi.fn(async () => {
      if (options.changePasswordError) throw options.changePasswordError;
    }),
    enrollDevice: vi.fn(async () => {
      if (options.enrollError) throw options.enrollError;
      enrolled = true;
    }),
    bindDeviceSession: vi.fn(async () => {
      if (options.bindError) throw options.bindError;
      return options.bindSession ?? session();
    }),
    logout: vi.fn(async () => {
      if (options.logoutError) throw options.logoutError;
    }),
    clearRuntimeState: vi.fn(),
    getCsrfToken: vi.fn(() => "csrf-test-token")
  };
}

function deviceBindingRequired() {
  return new HumanAuthenticationError(
    "operating-context",
    "This workstation must be provisioned before governed Operator Console actions are available.",
    false,
    undefined,
    "OPERATOR_DEVICE_BINDING_REQUIRED"
  );
}

function session(): OperatorConsoleHumanSession {
  return {
    sessionReference: "11000000-0000-0000-0000-000000000001",
    userReference: "12000000-0000-0000-0000-000000000001",
    username: "ordinary.operator",
    displayName: "Ordinary Operator",
    audience: "OPERATOR_CONSOLE",
    assurance: "PASSWORD",
    privilegedAccount: false,
    passwordChangeRequired: false,
    mfaRequired: false,
    mfaSatisfied: false,
    authenticatedAt: "2026-08-08T08:00:00+08:00",
    lastSeenAt: "2026-08-08T08:05:00+08:00",
    idleExpiresAt: "2099-08-08T08:35:00+08:00",
    absoluteExpiresAt: "2099-08-08T16:00:00+08:00",
    permissions: [
      "ticket.lookup",
      "statutory-discounts.decision.approve",
      "statutory-discounts.decision.reject",
      "statutory-discounts.evidence.review.view"
    ],
    siteReferences: ["13000000-0000-0000-0000-000000000001"],
    siteGroupReferences: ["14000000-0000-0000-0000-000000000001"],
    hasGlobalScope: false,
    operatorDeviceBindingReference: "16000000-0000-0000-0000-000000000001",
    effectiveSiteReference: "13000000-0000-0000-0000-000000000001",
    effectiveSiteGroupReference: "14000000-0000-0000-0000-000000000001",
    correlationId: "15000000-0000-0000-0000-000000000001"
  };
}

function restrictedSession(): OperatorConsoleHumanSession {
  return {
    ...session(),
    username: "JuanDC03",
    displayName: "Juan Dela Cruz",
    assurance: "PASSWORD_CHANGE_REQUIRED",
    passwordChangeRequired: true,
    permissions: [],
    siteReferences: [],
    siteGroupReferences: [],
    hasGlobalScope: false
  };
}
