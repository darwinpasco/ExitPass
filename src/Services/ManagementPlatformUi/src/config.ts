import type { ManagementPlatformConfig } from "./types";

const defaultConfig: ManagementPlatformConfig = {
  appBasePath: "/management-platform",
  centralPmsApiBasePath: "/v1/management-platform",
  environmentName: "Local development",
  featureFlags: {}
};

export function getManagementPlatformConfig(): ManagementPlatformConfig {
  return validateManagementPlatformConfig({
    appBasePath: import.meta.env.VITE_MANAGEMENT_PLATFORM_BASE_PATH ?? defaultConfig.appBasePath,
    centralPmsApiBasePath:
      import.meta.env.VITE_MANAGEMENT_PLATFORM_CENTRAL_PMS_API_BASE_PATH ?? defaultConfig.centralPmsApiBasePath,
    environmentName: import.meta.env.VITE_MANAGEMENT_PLATFORM_ENVIRONMENT_NAME ?? defaultConfig.environmentName,
    featureFlags: {}
  });
}

export function validateManagementPlatformConfig(config: ManagementPlatformConfig): ManagementPlatformConfig {
  if (!config.appBasePath.startsWith("/management-platform")) {
    throw new Error("Management Platform startup configuration is invalid.");
  }

  if (!config.centralPmsApiBasePath.startsWith("/v1/management-platform")) {
    throw new Error("Management Platform API configuration is invalid.");
  }

  if (/^https?:\/\//i.test(config.centralPmsApiBasePath)) {
    throw new Error("Management Platform API configuration must be relative.");
  }

  if (!config.environmentName.trim()) {
    throw new Error("Management Platform environment label is invalid.");
  }

  return config;
}
