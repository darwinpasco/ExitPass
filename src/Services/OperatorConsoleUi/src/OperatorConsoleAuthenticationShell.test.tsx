import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { createMockOperatorConsoleApiClient } from "./apiClient";
import { OperatorConsoleAuthenticationShell } from "./OperatorConsoleAuthenticationShell";
import { HumanAuthenticationError, type HumanAuthenticationClient, type OperatorConsoleHumanSession } from "./humanAuthentication";

describe("Operator Console authentication shell", () => {
  it("bootstraps from current-session readback without restoring browser authority", async () => {
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
    expect(client.getCurrentSession).toHaveBeenCalledTimes(1);
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
    expect(screen.queryByLabelText(/totp|one-time/i)).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText("Username"), "ordinary.operator");
    await userEvent.type(screen.getByLabelText("Password"), "operator-password");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("Ordinary Operator")).toBeInTheDocument();
    expect(client.login).toHaveBeenCalledWith("ordinary.operator", "operator-password");
    expect(screen.queryByLabelText(/totp|one-time/i)).not.toBeInTheDocument();
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
    expect(screen.getByLabelText("Password")).toHaveAttribute("autocomplete", "current-password");
    expect(screen.getByRole("button", { name: "Sign in" })).toBeDisabled();
  });
});

function authenticationClient(options: {
  currentSession?: OperatorConsoleHumanSession;
  currentError?: Error;
  loginSession?: OperatorConsoleHumanSession;
  loginError?: Error;
  logoutError?: Error;
}): HumanAuthenticationClient {
  return {
    getCurrentSession: vi.fn(async () => {
      if (options.currentError) throw options.currentError;
      return options.currentSession ?? session();
    }),
    login: vi.fn(async () => {
      if (options.loginError) throw options.loginError;
      return options.loginSession ?? session();
    }),
    logout: vi.fn(async () => {
      if (options.logoutError) throw options.logoutError;
    }),
    clearRuntimeState: vi.fn(),
    getCsrfToken: vi.fn(() => "csrf-test-token")
  };
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
      "statutory-discounts.decision.approve",
      "statutory-discounts.decision.reject",
      "statutory-discounts.evidence.review.view"
    ],
    siteReferences: ["13000000-0000-0000-0000-000000000001"],
    siteGroupReferences: ["14000000-0000-0000-0000-000000000001"],
    hasGlobalScope: false,
    correlationId: "15000000-0000-0000-0000-000000000001"
  };
}
