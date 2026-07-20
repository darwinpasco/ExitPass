import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { App, FeatureUnavailable, MutationUncertainMessage, PageError } from "./App";
import { managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions, hasAllPermissions, hasAnyPermission, hasPermission } from "./permissions";
import type { ManagementPlatformAuthState } from "./types";

const siteA = {
  siteId: "77000000-0000-0000-0000-000000000002",
  sitePosServerId: "88000000-0000-0000-0000-000000000002",
  displayName: "Terminal Parking / North Exit"
};

const siteB = {
  siteId: "77000000-0000-0000-0000-000000000003",
  sitePosServerId: "88000000-0000-0000-0000-000000000003",
  displayName: "City Center Parking"
};

function authState(permissions = [managementPlatformOverviewPermission], sites = [siteA, siteB]): ManagementPlatformAuthState {
  return {
    status: "authenticated",
    principal: {
      authenticated: true,
      subjectRef: "user-123",
      displayName: "Admin User",
      permissions,
      authorizedSites: sites
    }
  };
}

describe("ManagementPlatformUi foundation shell", () => {
  it("renders the Management Platform root and Overview without Operator Console or WebPay branding", () => {
    render(<App authState={authState()} initialPath="/management-platform" />);

    expect(screen.getByRole("heading", { name: "ExitPass Management Platform" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Management Platform foundation" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Management Platform routes" })).toBeInTheDocument();
    expect(screen.queryByText(/Operator Console/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/WebPay/i)).not.toBeInTheDocument();
  });

  it("resolves the Overview route and updates the browser title", async () => {
    render(<App authState={authState()} initialPath="/management-platform/overview" />);

    expect(screen.getByRole("heading", { name: "Management Platform foundation" })).toBeInTheDocument();
    await waitFor(() => expect(document.title).toBe("Overview - ExitPass Management Platform"));
  });

  it("shows a scoped not-found state for unknown Management Platform routes", () => {
    render(<App authState={authState()} initialPath="/management-platform/unknown" />);

    expect(screen.getByRole("status", { name: "Management Platform route not found" })).toBeInTheDocument();
  });

  it("does not display protected content while authentication is loading", () => {
    render(<App authState={{ status: "loading" }} initialPath="/management-platform" />);

    expect(screen.getByRole("status", { name: "Loading" })).toHaveTextContent("Loading Management Platform access");
    expect(screen.queryByRole("heading", { name: "Management Platform foundation" })).not.toBeInTheDocument();
  });

  it("blocks unauthenticated users distinctly from permission denial", () => {
    render(<App authState={{ status: "unauthenticated" }} initialPath="/management-platform" />);

    expect(screen.getByRole("status", { name: "Authentication required" })).toBeInTheDocument();
    expect(screen.queryByText(/Permission denied/i)).not.toBeInTheDocument();
  });

  it("blocks authenticated users without the Overview permission", () => {
    render(<App authState={authState(["unrelated.permission"])} initialPath="/management-platform" />);

    expect(screen.getByRole("alert", { name: "Permission denied" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Management Platform foundation" })).not.toBeInTheDocument();
  });

  it("renders safe principal and Site context without raw claims or tokens", () => {
    render(<App authState={authState()} initialPath="/management-platform" />);

    expect(screen.getByText("Admin User")).toBeInTheDocument();
    expect(screen.getByText(/Current Site: Terminal Parking/i)).toBeInTheDocument();
    expect(screen.queryByText(/access_token/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/raw claims/i)).not.toBeInTheDocument();
  });

  it("renders authorized Sites and switches current Site without accepting free-form Site authority", async () => {
    render(<App authState={authState()} initialPath="/management-platform" />);

    const selector = screen.getByLabelText("Current Site");
    expect(selector).toHaveDisplayValue("Terminal Parking / North Exit");
    expect(screen.queryByText("Unauthorized Site")).not.toBeInTheDocument();

    await userEvent.selectOptions(selector, siteB.siteId);

    expect(selector).toHaveDisplayValue("City Center Parking");
    expect(screen.getByText(/Current Site: City Center Parking/i)).toBeInTheDocument();
  });

  it("renders a no-authorized-Site posture safely", () => {
    render(<App authState={authState([managementPlatformOverviewPermission], [])} initialPath="/management-platform" />);

    expect(screen.getByRole("status", { name: "No authorized Sites" })).toBeInTheDocument();
    expect(screen.getByText(/No authorized Site is available/i)).toBeInTheDocument();
  });

  it("shows Sales Invoice profile navigation only when read permission is present", () => {
    const { rerender } = render(<App authState={authState()} initialPath="/management-platform" />);
    expect(screen.queryByRole("button", { name: /Sales Invoice Profiles/i })).not.toBeInTheDocument();

    rerender(<App authState={authState([managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions.read])} initialPath="/management-platform" />);
    expect(screen.getByRole("button", { name: /Sales Invoice Configuration Sales Invoice Setups/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Create/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Approve/i })).not.toBeInTheDocument();
  });

  it("exposes accessible error, feature-unavailable, and mutation-uncertain components", () => {
    const { rerender } = render(<FeatureUnavailable />);
    expect(screen.getByRole("status", { name: "Feature unavailable" })).toHaveTextContent("not enabled");

    rerender(<MutationUncertainMessage correlationId="corr-123" />);
    expect(screen.getByRole("status", { name: "Result uncertain" })).toHaveTextContent("Refresh and verify");
    expect(screen.getByText(/corr-123/)).toBeInTheDocument();

    rerender(<PageError error={{ kind: "conflict", code: "CONFLICT", message: "Safe message", correlationId: "corr-456", retryable: false, mutationUncertain: false }} />);
    expect(screen.getByRole("alert", { name: "Management Platform error" })).toHaveTextContent("corr-456");
  });

  it("has semantic landmarks, labeled Site selector, keyboard-reachable navigation, and non-color status text", async () => {
    render(<App authState={authState()} initialPath="/management-platform" />);

    expect(screen.getByRole("main", { name: "ExitPass Management Platform" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Management Platform routes" })).toBeInTheDocument();
    expect(screen.getByLabelText("Current Site")).toBeInTheDocument();

    await userEvent.tab();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Overview" }));
    expect(screen.getByText("Foundation ready")).toBeInTheDocument();
  });
});

describe("ManagementPlatformUi permission helpers", () => {
  it("evaluates single, any-of, and all-of permission checks without inheritance", () => {
    const permissions = [managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions.read, futureSalesInvoiceProfilePermissions.manage];

    expect(hasPermission(permissions, futureSalesInvoiceProfilePermissions.read)).toBe(true);
    expect(hasAnyPermission(permissions, ["unrelated.permission", futureSalesInvoiceProfilePermissions.manage])).toBe(true);
    expect(hasAllPermissions(permissions, [futureSalesInvoiceProfilePermissions.read, futureSalesInvoiceProfilePermissions.manage])).toBe(true);
    expect(hasPermission(permissions, futureSalesInvoiceProfilePermissions.approve)).toBe(false);
    expect(hasAllPermissions(permissions, [futureSalesInvoiceProfilePermissions.manage, futureSalesInvoiceProfilePermissions.approve])).toBe(false);
  });
});

describe("ManagementPlatformUi development manual validation scenarios", () => {
  it("authenticated scenario keeps Overview accessible with one authorized Site", () => {
    window.history.pushState({}, "", "/management-platform?mpScenario=authenticated");

    render(<App />);

    expect(screen.getByRole("status", { name: "Development scenario" })).toHaveTextContent("authenticated");
    expect(screen.getByRole("heading", { name: "Management Platform foundation" })).toBeInTheDocument();
    expect(screen.getByLabelText("Current Site")).toHaveDisplayValue("Development Site Alpha");
    expect(screen.queryByText("Development Site Beta")).not.toBeInTheDocument();
  });

  it("unauthenticated scenario shows authentication-required posture without protected Overview", () => {
    window.history.pushState({}, "", "/management-platform?mpScenario=unauthenticated");

    render(<App />);

    expect(screen.getByRole("status", { name: "Development scenario" })).toHaveTextContent("unauthenticated");
    expect(screen.getByRole("status", { name: "Authentication required" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Management Platform foundation" })).not.toBeInTheDocument();
  });

  it("permission-denied scenario is authenticated but lacks the Overview permission", () => {
    window.history.pushState({}, "", "/management-platform?mpScenario=permission-denied");

    render(<App />);

    expect(screen.getByRole("status", { name: "Development scenario" })).toHaveTextContent("permission-denied");
    expect(screen.getByText("Development Permission Denied User")).toBeInTheDocument();
    expect(screen.getByRole("alert", { name: "Permission denied" })).toBeInTheDocument();
    expect(screen.queryByRole("status", { name: "Authentication required" })).not.toBeInTheDocument();
  });

  it("multi-site scenario exposes non-production Sites and updates current Site context", async () => {
    window.history.pushState({}, "", "/management-platform?mpScenario=multi-site");

    render(<App />);

    const selector = screen.getByLabelText("Current Site");
    expect(screen.getByRole("status", { name: "Development scenario" })).toHaveTextContent("multi-site");
    expect(selector).toHaveDisplayValue("Development Site Alpha");
    expect(screen.getByText("Development Site Beta")).toBeInTheDocument();

    await userEvent.selectOptions(selector, "71000000-0000-0000-0000-000000000102");

    expect(selector).toHaveDisplayValue("Development Site Beta");
    expect(screen.getByText(/Current Site: Development Site Beta/i)).toBeInTheDocument();
  });

  it("no-sites scenario renders safe no-authorized-Site posture", () => {
    window.history.pushState({}, "", "/management-platform?mpScenario=no-sites");

    render(<App />);

    expect(screen.getByRole("status", { name: "Development scenario" })).toHaveTextContent("no-sites");
    expect(screen.getByRole("status", { name: "No authorized Sites" })).toBeInTheDocument();
    expect(screen.getByText(/No authorized Site is available/i)).toBeInTheDocument();
  });

  it("unavailable scenario renders safe error with test correlation and no sensitive material", () => {
    window.history.pushState({}, "", "/management-platform?mpScenario=unavailable");

    render(<App />);

    expect(screen.getByRole("status", { name: "Development scenario" })).toHaveTextContent("unavailable");
    expect(screen.getByRole("alert", { name: "Management Platform error" })).toHaveTextContent("dev-scenario-correlation-0001");
    expect(document.body).not.toHaveTextContent("stack trace");
    expect(document.body).not.toHaveTextContent("X-PosServer-Admin-Key");
    expect(document.body).not.toHaveTextContent("https://");
    expect(document.body).not.toHaveTextContent("token");
  });

  it("not-found scenario renders scoped Management Platform not-found posture", () => {
    window.history.pushState({}, "", "/management-platform?mpScenario=not-found");

    render(<App />);

    expect(screen.getByRole("status", { name: "Development scenario" })).toHaveTextContent("not-found");
    expect(screen.getByRole("status", { name: "Management Platform route not found" })).toBeInTheDocument();
  });

  it("unknown scenario falls back safely to authenticated", () => {
    window.history.pushState({}, "", "/management-platform?mpScenario=unknown-value");

    render(<App />);

    expect(screen.getByRole("status", { name: "Development scenario" })).toHaveTextContent("authenticated");
    expect(screen.getByRole("heading", { name: "Management Platform foundation" })).toBeInTheDocument();
  });

  it("production mode ignores mpScenario and exposes no development scenario indicator", () => {
    window.history.pushState({}, "", "/management-platform?mpScenario=unauthenticated");

    render(<App developmentScenariosEnabled={false} />);

    expect(screen.queryByRole("status", { name: "Development scenario" })).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Management Platform foundation" })).toBeInTheDocument();
    expect(screen.queryByRole("status", { name: "Authentication required" })).not.toBeInTheDocument();
  });
});
