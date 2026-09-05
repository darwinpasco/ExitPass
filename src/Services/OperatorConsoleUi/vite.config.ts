import { loadEnv, type UserConfig } from "vite";
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

const defaultApiProxyTarget = "http://127.0.0.1:56065";

export function createOperatorConsoleViteConfig(
  apiProxyTarget = defaultApiProxyTarget
): UserConfig {
  const trimmedApiProxyTarget = apiProxyTarget.trim() || defaultApiProxyTarget;

  return {
    plugins: [react()],
    server: {
      port: 5175,
      strictPort: true,
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
      include: ["src/**/*.test.{ts,tsx}"],
      globals: true,
      setupFiles: "./src/test/setup.ts"
    }
  };
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, ".", "VITE_");
  return createOperatorConsoleViteConfig(
    env.VITE_OPERATOR_CONSOLE_API_PROXY_TARGET
  );
});
