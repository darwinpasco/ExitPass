import { loadEnv, type UserConfig } from "vite";
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

const defaultApiProxyTarget = "http://127.0.0.1:56063";
const defaultCentralPmsProxyTarget = "https://localhost:56064";
const customerDigitalSalesInvoicePath = "/v1/webpay/digital-sales-invoices/presentation";

export function createWebPayViteConfig(
  apiProxyTarget = defaultApiProxyTarget,
  centralPmsProxyTarget = defaultCentralPmsProxyTarget
): UserConfig {
  const trimmedApiProxyTarget = apiProxyTarget.trim() || defaultApiProxyTarget;
  const trimmedCentralPmsProxyTarget = centralPmsProxyTarget.trim().replace(/\/+$/, "") || defaultCentralPmsProxyTarget;
  const proxy = {
    [customerDigitalSalesInvoicePath]: {
      target: trimmedCentralPmsProxyTarget,
      changeOrigin: true,
      secure: false
    },
    "/v1": {
      target: trimmedApiProxyTarget,
      changeOrigin: true
    }
  };
  const allowedHosts = [
    ".ngrok-free.app",
    ".ngrok-free.dev"
  ];

  return {
    plugins: [react()],
    server: {
      port: 5174,
      strictPort: true,
      allowedHosts,
      proxy
    },
    preview: {
      port: 5174,
      strictPort: true,
      allowedHosts,
      proxy
    },
    test: {
      environment: "jsdom",
      globals: true,
      setupFiles: "./src/test/setup.ts",
      exclude: ["e2e/**", "dist/**", "node_modules/**"]
    }
  };
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, ".", "VITE_");
  return createWebPayViteConfig(
    env.VITE_WEBPAY_API_PROXY_TARGET,
    env.VITE_WEBPAY_CENTRAL_PMS_PROXY_TARGET
  );
});
