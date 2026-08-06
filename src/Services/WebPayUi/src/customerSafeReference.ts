const canonicalUuidPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

// Presentation only: canonical values continue to flow unchanged through API and recovery contracts.
export function formatCustomerSupportReference(value?: string | null): string | null {
  const normalized = value?.trim().toLowerCase();
  if (!normalized || !canonicalUuidPattern.test(normalized)) {
    return null;
  }

  let hash = 0x811c9dc5;
  for (const character of normalized) {
    hash ^= character.charCodeAt(0);
    hash = Math.imul(hash, 0x01000193);
  }

  const token = (hash >>> 0).toString(16).toUpperCase().padStart(8, "0");
  return `${token.slice(0, 4)}-${token.slice(4)}`;
}
