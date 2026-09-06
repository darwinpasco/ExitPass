// @vitest-environment node

import { describe, expect, it } from "vitest";
import { createOperatorConsoleViteConfig } from "../vite.config";

describe("Operator Console Vite dev server config", () => {
  it("OperatorConsoleDevServer_AllowsNgrokHostsAndProxiesV1ByDefault", () => {
    const config = createOperatorConsoleViteConfig();

    expect(config.server?.port).toBe(5175);
    expect(config.server?.strictPort).toBe(true);
    expect(config.server?.allowedHosts).toEqual([".ngrok-free.app", ".ngrok-free.dev"]);
    expect(config.server?.allowedHosts).not.toBe(true);
    expect(config.server?.proxy?.["/v1"]).toMatchObject({
      target: "https://localhost:56064",
      changeOrigin: true,
      secure: false
    });
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
});
