import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { App } from "./App";
import {
  createHumanAuthenticationClient,
  HumanAuthenticationError,
  type HumanAuthenticationClient,
  type OperatorConsoleHumanSession
} from "./humanAuthentication";
import { createOperatorConsoleApiClient, type OperatorConsoleApiClient } from "./apiClient";

type ShellState =
  | { status: "bootstrapping" }
  | { status: "unauthenticated"; message?: string; supportReference?: string }
  | { status: "authenticating" }
  | { status: "authenticated"; session: OperatorConsoleHumanSession }
  | { status: "restricted"; message: string; supportReference?: string };

interface OperatorConsoleAuthenticationShellProps {
  authenticationClient?: HumanAuthenticationClient;
  createWorkspaceClient?: (
    session: OperatorConsoleHumanSession,
    onAuthenticationRequired: () => void
  ) => OperatorConsoleApiClient;
  initialPath?: string;
}

export function OperatorConsoleAuthenticationShell({
  authenticationClient,
  createWorkspaceClient,
  initialPath
}: OperatorConsoleAuthenticationShellProps) {
  const authClient = useMemo(
    () => authenticationClient ?? createHumanAuthenticationClient(),
    [authenticationClient]
  );
  const [state, setState] = useState<ShellState>({ status: "bootstrapping" });
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [logoutPending, setLogoutPending] = useState(false);
  const [logoutMessage, setLogoutMessage] = useState<string | undefined>();
  const activeRef = useRef(true);

  const requireAuthentication = useCallback((message = "Your session ended. Sign in again.") => {
    authClient.clearRuntimeState();
    setPassword("");
    setLogoutPending(false);
    setLogoutMessage(undefined);
    setState({ status: "unauthenticated", message });
  }, [authClient]);

  useEffect(() => {
    activeRef.current = true;
    void authClient
      .getCurrentSession()
      .then((session) => {
        if (!activeRef.current) return;
        if (session.passwordChangeRequired) {
          setState({
            status: "restricted",
            message: "Your password must be changed through the governed account workflow before Operator Console access is available.",
            supportReference: session.correlationId
          });
          return;
        }
        setState({ status: "authenticated", session });
      })
      .catch((error) => {
        if (!activeRef.current) return;
        const mapped = authenticationMessage(error);
        authClient.clearRuntimeState();
        setState({
          status: "unauthenticated",
          message: mapped.silent ? undefined : mapped.message,
          supportReference: mapped.supportReference
        });
      });

    return () => {
      activeRef.current = false;
      authClient.clearRuntimeState();
    };
  }, [authClient]);

  useEffect(() => {
    if (state.status !== "authenticated") return;
    const expiry = Math.min(
      Date.parse(state.session.idleExpiresAt),
      Date.parse(state.session.absoluteExpiresAt)
    );
    const delay = Math.max(0, expiry - Date.now());
    const timeout = window.setTimeout(
      () => requireAuthentication("Your session expired. Sign in again."),
      Math.min(delay, 2_147_483_647)
    );
    return () => window.clearTimeout(timeout);
  }, [requireAuthentication, state]);

  async function submitLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const submittedUsername = username.trim();
    if (!submittedUsername || !password) return;

    setState({ status: "authenticating" });
    try {
      const session = await authClient.login(submittedUsername, password);
      setPassword("");
      if (session.passwordChangeRequired) {
        setState({
          status: "restricted",
          message: "Your password must be changed through the governed account workflow before Operator Console access is available.",
          supportReference: session.correlationId
        });
        return;
      }
      setState({ status: "authenticated", session });
    } catch (error) {
      authClient.clearRuntimeState();
      setPassword("");
      const mapped = authenticationMessage(error);
      setState({
        status: "unauthenticated",
        message: mapped.message,
        supportReference: mapped.supportReference
      });
    }
  }

  async function logout() {
    if (state.status !== "authenticated" || logoutPending) return;
    setLogoutPending(true);
    setLogoutMessage(undefined);
    try {
      await authClient.logout();
      requireAuthentication("You have signed out.");
    } catch (error) {
      setLogoutPending(false);
      const mapped = authenticationMessage(error);
      if (mapped.sessionInvalid) {
        requireAuthentication(mapped.message);
        return;
      }
      setState({
        status: "authenticated",
        session: state.session
      });
      setLogoutMessage(mapped.message);
    }
  }

  const workspaceClient = useMemo(() => {
    if (state.status !== "authenticated") return null;
    return createWorkspaceClient
      ? createWorkspaceClient(state.session, requireAuthentication)
      : createOperatorConsoleApiClient({
          permissions: state.session.permissions,
          onAuthenticationRequired: requireAuthentication
        });
  }, [createWorkspaceClient, requireAuthentication, state]);

  if (state.status === "authenticated" && workspaceClient) {
    return (
      <App
        apiClient={workspaceClient}
        initialPath={initialPath}
        session={state.session}
        logoutPending={logoutPending}
        logoutMessage={logoutMessage}
        onLogout={logout}
      />
    );
  }

  if (state.status === "bootstrapping") {
    return (
      <main className="authenticationShell" aria-busy="true">
        <section className="authenticationPanel" role="status" aria-live="polite">
          <p className="eyebrow">Operator Console</p>
          <h1>Checking your session</h1>
          <p>Confirming your current authenticated Operator Console session.</p>
        </section>
      </main>
    );
  }

  if (state.status === "restricted") {
    return (
      <main className="authenticationShell">
        <section className="authenticationPanel" aria-labelledby="operator-console-account-action-title">
          <p className="eyebrow">Operator Console</p>
          <h1 id="operator-console-account-action-title">Account action required</h1>
          <p className="authenticationError" role="alert">{state.message}</p>
          {state.supportReference && <p className="supportReference">Support reference: {state.supportReference}</p>}
          <button type="button" onClick={() => requireAuthentication()}>Return to sign in</button>
        </section>
      </main>
    );
  }

  const busy = state.status === "authenticating";
  const message = state.status === "unauthenticated" ? state.message : undefined;
  const supportReference = state.status === "unauthenticated" ? state.supportReference : undefined;

  return (
    <main className="authenticationShell">
      <section className="authenticationPanel" aria-labelledby="operator-console-login-title">
        <p className="eyebrow">Operator Console</p>
        <h1 id="operator-console-login-title">Staff sign in</h1>
        <p>Use your ExitPass staff account to open the authorized operations workspace.</p>
        {message && <p className="authenticationError" role="alert">{message}</p>}
        {supportReference && <p className="supportReference">Support reference: {supportReference}</p>}
        <form className="authenticationForm" onSubmit={submitLogin} aria-busy={busy}>
          <label>
            Username
            <input
              autoComplete="username"
              autoFocus
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              disabled={busy}
            />
          </label>
          <label>
            Password
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              disabled={busy}
            />
          </label>
          <button type="submit" disabled={busy || !username.trim() || !password}>
            {busy ? "Signing in" : "Sign in"}
          </button>
        </form>
      </section>
    </main>
  );
}

function authenticationMessage(error: unknown) {
  if (error instanceof HumanAuthenticationError) {
    return {
      message: error.message,
      supportReference: error.supportReference,
      silent: error.kind === "unauthenticated",
      sessionInvalid:
        error.kind === "unauthenticated" ||
        error.kind === "session-expired" ||
        error.kind === "session-revoked"
    };
  }
  return {
    message: "Operator Console authentication is temporarily unavailable. Try again.",
    silent: false,
    sessionInvalid: false
  };
}
