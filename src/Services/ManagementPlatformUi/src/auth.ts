import type { ManagementPlatformAuthState, ManagementPlatformPrincipal, ManagementPlatformSite } from "./types";
import { managementPlatformOverviewPermission } from "./permissions";

const defaultSites: ManagementPlatformSite[] = [
  {
    siteId: "77000000-0000-0000-0000-000000000002",
    sitePosServerId: "88000000-0000-0000-0000-000000000002",
    displayName: "Terminal Parking / North Exit"
  }
];

export function createDevelopmentAuthState(): ManagementPlatformAuthState {
  return {
    status: "authenticated",
    principal: createDevelopmentPrincipal()
  };
}

export function createDevelopmentPrincipal(overrides: Partial<ManagementPlatformPrincipal> = {}): ManagementPlatformPrincipal {
  return {
    authenticated: true,
    subjectRef: localFallback(import.meta.env.VITE_MANAGEMENT_PLATFORM_SUBJECT_REF, "local-management-platform-user"),
    displayName: localFallback(import.meta.env.VITE_MANAGEMENT_PLATFORM_DISPLAY_NAME, "Management Platform User"),
    permissions: parseList(
      localFallback(import.meta.env.VITE_MANAGEMENT_PLATFORM_PERMISSIONS, managementPlatformOverviewPermission)
    ),
    authorizedSites: defaultSites,
    ...overrides
  };
}

export function parseList(value: string): string[] {
  return value
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);
}

function localFallback(value: string | undefined, fallback: string): string {
  return value && value.trim() ? value.trim() : fallback;
}
