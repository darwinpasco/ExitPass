import { useMemo, useState } from "react";
import type { ManagementPlatformSite } from "./types";

export function useManagementPlatformSiteSelection(sites: readonly ManagementPlatformSite[]) {
  const normalizedSites = useMemo(() => [...sites], [sites]);
  const [currentSiteId, setCurrentSiteId] = useState<string | undefined>(normalizedSites[0]?.siteId);
  const currentSite = normalizedSites.find((site) => site.siteId === currentSiteId) ?? normalizedSites[0];

  function switchSite(nextSiteId: string) {
    if (normalizedSites.some((site) => site.siteId === nextSiteId)) {
      setCurrentSiteId(nextSiteId);
    }
  }

  return {
    sites: normalizedSites,
    currentSite,
    switchSite,
    hasSites: normalizedSites.length > 0
  };
}
