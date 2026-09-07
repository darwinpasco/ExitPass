export function RuntimeProfileBadge() {
  const profile = (import.meta.env.VITE_EXITPASS_RUNTIME_PROFILE ?? "").trim().toUpperCase();
  const label = (import.meta.env.VITE_EXITPASS_RUNTIME_PROFILE_LABEL ?? "").trim();

  if (profile !== "DEVELOPER" || !label) return null;

  return <p className="runtime-profile-badge">{label}</p>;
}
