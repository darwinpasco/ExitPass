import { loadEnv, type UserConfig } from "vite";
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

const defaultApiProxyTarget = "https://localhost:56064";
const defaultWebPayUiProxyTarget = "http://127.0.0.1:5174";

export function createOperatorConsoleViteConfig(
  apiProxyTarget = defaultApiProxyTarget,
  webPayUiProxyTarget = defaultWebPayUiProxyTarget
): UserConfig {
  const trimmedApiProxyTarget = apiProxyTarget.trim().replace(/\/+$/, "") || defaultApiProxyTarget;
  const trimmedWebPayUiProxyTarget = webPayUiProxyTarget.trim().replace(/\/+$/, "") || defaultWebPayUiProxyTarget;

  return {
    plugins: [react()],
    server: {
      port: 5175,
      strictPort: true,
      allowedHosts: [
        ".ngrok-free.app",
        ".ngrok-free.dev",
        "operator-console-exitpass.ngrok.dev"
      ],
      proxy: {
        "/webpay-app": {
          target: trimmedWebPayUiProxyTarget,
          changeOrigin: true
        },
        "/webpay": {
          target: trimmedWebPayUiProxyTarget,
          changeOrigin: true,
          rewrite: path => `/webpay-app${path}`
        },
        "/v1": {
          target: trimmedApiProxyTarget,
          changeOrigin: true,
          secure: trimmedApiProxyTarget !== defaultApiProxyTarget
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
    env.VITE_OPERATOR_CONSOLE_API_PROXY_TARGET,
    env.VITE_WEBPAY_UI_PROXY_TARGET
  );
});
