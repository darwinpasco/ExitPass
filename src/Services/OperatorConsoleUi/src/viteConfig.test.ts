// @vitest-environment node

import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { createOperatorConsoleViteConfig } from "../vite.config";

describe("Operator Console Vite dev server config", () => {
  it("OperatorConsoleDevServer_AllowsNgrokHostsAndProxiesV1ByDefault", () => {
    const config = createOperatorConsoleViteConfig();

    expect(config.server?.port).toBe(5175);
    expect(config.server?.strictPort).toBe(true);
    expect(config.server?.allowedHosts).toEqual([
      ".ngrok-free.app",
      ".ngrok-free.dev",
      "operator-console-exitpass.ngrok.dev"
    ]);
    expect(config.server?.allowedHosts).not.toBe(true);
    expect(config.server?.proxy?.["/v1"]).toMatchObject({
      target: "https://localhost:56064",
      changeOrigin: true,
      secure: false
    });
    expect(config.server?.proxy?.["/webpay"]).toMatchObject({
      target: "http://127.0.0.1:5174",
      changeOrigin: true
    });
    const webPayProxy = config.server?.proxy?.["/webpay"];
    expect(typeof webPayProxy).toBe("object");
    expect(typeof webPayProxy === "object" && webPayProxy?.rewrite?.("/webpay/sales-invoice")).toBe(
      "/webpay-app/webpay/sales-invoice"
    );
    expect(config.server?.proxy?.["/webpay-app"]).toMatchObject({
      target: "http://127.0.0.1:5174",
      changeOrigin: true
    });
    expect(config.preview).toMatchObject({ port: 5175, strictPort: true });
    expect(config.preview?.proxy?.["/v1"]).toMatchObject({
      target: "https://localhost:56064",
      secure: false
    });
    expect(config.preview?.proxy?.["/webpay-app"]).toMatchObject({ target: "http://127.0.0.1:5174" });
  });

  it("OperatorConsoleDevServer_WhenWebPayTargetIsProvided_UsesItOnlyForCustomerPageRoutes", () => {
    const config = createOperatorConsoleViteConfig("http://localhost:19082", "http://localhost:19084/");

    expect(config.server?.proxy?.["/webpay"]).toMatchObject({ target: "http://localhost:19084" });
    expect(config.server?.proxy?.["/webpay-app"]).toMatchObject({ target: "http://localhost:19084" });
    expect(config.server?.proxy?.["/v1"]).toMatchObject({ target: "http://localhost:19082" });
    expect(config.preview?.proxy?.["/v1"]).toMatchObject({ target: "http://localhost:19082" });
  });

  it("OperatorConsoleDevServer_WhenProxyTargetEnvIsProvided_UsesConfiguredTarget", () => {
    const config = createOperatorConsoleViteConfig("http://localhost:19082");

    expect(config.server?.proxy?.["/v1"]).toMatchObject({
      target: "http://localhost:19082",
      changeOrigin: true,
      secure: true
    });
  });

  it("OperatorConsoleDevServer_WhenCanonicalTargetHasTrailingSlash_AcceptsOnlyItsDevelopmentCertificate", () => {
    const config = createOperatorConsoleViteConfig("https://localhost:56064/");

    expect(config.server?.proxy?.["/v1"]).toMatchObject({
      target: "https://localhost:56064",
      secure: false
    });
  });

  it("OperatorConsoleRepeatableLauncher_BuildsAndUsesStablePreviewHosting", () => {
    const launcher = readFileSync(
      resolve(process.cwd(), "../../../scripts/v1.3/local-runtime/Start-OperatorConsole.ps1"),
      "utf8");

    expect(launcher).toContain("npm.cmd run build");
    expect(launcher).toContain("npm.cmd run preview");
    expect(launcher).not.toMatch(/npm(?:\.cmd)?\s+run\s+dev/i);
  });
});
