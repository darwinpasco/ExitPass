// @vitest-environment node

import { describe, expect, it } from "vitest";
import { createStatutoryDiscountOperatorViteConfig } from "../vite.config";

describe("Statutory Discount Operator Vite dev server config", () => {
  it("StatutoryDiscountOperatorDevServer_AllowsNgrokHostsAndProxiesV1ByDefault", () => {
    const config = createStatutoryDiscountOperatorViteConfig();

    expect(config.server?.port).toBe(5175);
    expect(config.server?.allowedHosts).toEqual([".ngrok-free.app", ".ngrok-free.dev"]);
    expect(config.server?.allowedHosts).not.toBe(true);
    expect(config.server?.proxy?.["/v1"]).toMatchObject({
      target: "http://localhost:8082",
      changeOrigin: true
    });
  });

  it("StatutoryDiscountOperatorDevServer_WhenProxyTargetEnvIsProvided_UsesConfiguredTarget", () => {
    const config = createStatutoryDiscountOperatorViteConfig("http://localhost:19082");

    expect(config.server?.proxy?.["/v1"]).toMatchObject({
      target: "http://localhost:19082",
      changeOrigin: true
    });
  });
});
