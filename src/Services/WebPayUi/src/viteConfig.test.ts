// @vitest-environment node

import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { createWebPayViteConfig } from "../vite.config";

describe("WebPay Vite dev server config", () => {
  it("WebPayDevServer_AllowsNgrokHostsAndProxiesV1ByDefault", () => {
    const config = createWebPayViteConfig();

    expect(config.server?.port).toBe(5174);
    expect(config.server?.strictPort).toBe(true);
    expect(config.server?.allowedHosts).toEqual([".ngrok-free.app", ".ngrok-free.dev"]);
    expect(config.server?.allowedHosts).not.toBe(true);
    expect(config.server?.proxy?.["/v1"]).toMatchObject({
      target: "http://127.0.0.1:56063",
      changeOrigin: true
    });
    expect(config.preview).toMatchObject({ port: 5174, strictPort: true });
    expect(config.preview?.proxy?.["/v1"]).toMatchObject({
      target: "http://127.0.0.1:56063",
      changeOrigin: true
    });
  });

  it("WebPayDevServer_WhenProxyTargetEnvIsProvided_UsesConfiguredTarget", () => {
    const config = createWebPayViteConfig("http://localhost:19082");

    expect(config.server?.proxy?.["/v1"]).toMatchObject({
      target: "http://localhost:19082",
      changeOrigin: true
    });
    expect(config.preview?.proxy?.["/v1"]).toMatchObject({ target: "http://localhost:19082" });
  });

  it("WebPayRepeatableLauncher_BuildsAndUsesStablePreviewHosting", () => {
    const launcher = readFileSync(
      resolve(process.cwd(), "../../../scripts/v1.3/local-runtime/Start-WebPayPitx.ps1"),
      "utf8");

    expect(launcher).toContain("npm.cmd run build");
    expect(launcher).toContain("npm.cmd run preview");
    expect(launcher).not.toMatch(/npm(?:\.cmd)?\s+run\s+dev/i);
  });
});
