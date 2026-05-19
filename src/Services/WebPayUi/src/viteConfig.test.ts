// @vitest-environment node

import { describe, expect, it } from "vitest";
import { createWebPayViteConfig } from "../vite.config";

describe("WebPay Vite dev server config", () => {
  it("WebPayDevServer_AllowsNgrokHostsAndProxiesV1ByDefault", () => {
    const config = createWebPayViteConfig();

    expect(config.server?.allowedHosts).toEqual([".ngrok-free.app", ".ngrok-free.dev"]);
    expect(config.server?.allowedHosts).not.toBe(true);
    expect(config.server?.proxy?.["/v1"]).toMatchObject({
      target: "http://localhost:8082",
      changeOrigin: true
    });
  });

  it("WebPayDevServer_WhenProxyTargetEnvIsProvided_UsesConfiguredTarget", () => {
    const config = createWebPayViteConfig("http://localhost:19082");

    expect(config.server?.proxy?.["/v1"]).toMatchObject({
      target: "http://localhost:19082",
      changeOrigin: true
    });
  });
});
