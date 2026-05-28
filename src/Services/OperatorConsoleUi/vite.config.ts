import { loadEnv, type UserConfig } from "vite";
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

const defaultApiProxyTarget = "http://localhost:8082";

export function createStatutoryDiscountOperatorViteConfig(
  apiProxyTarget = defaultApiProxyTarget
): UserConfig {
  const trimmedApiProxyTarget = apiProxyTarget.trim() || defaultApiProxyTarget;

  return {
    plugins: [react()],
    server: {
      port: 5175,
      allowedHosts: [".ngrok-free.app", ".ngrok-free.dev"],
      proxy: {
        "/v1": {
          target: trimmedApiProxyTarget,
          changeOrigin: true
        }
      }
    },
    test: {
      environment: "jsdom",
      globals: true,
      setupFiles: "./src/test/setup.ts"
    }
  };
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, ".", "VITE_");
  return createStatutoryDiscountOperatorViteConfig(
    env.VITE_STATUTORY_DISCOUNT_OPERATOR_API_PROXY_TARGET
  );
});
