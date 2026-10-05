export function normalizeScannedTicketReference(rawValue: string): string {
  const value = rawValue.trim();
  if (!value) return "";

  try {
    const parsed = JSON.parse(value) as Record<string, unknown>;
    const ticket = parsed.ticketReference ?? parsed.ticket_reference ?? parsed.ticket ?? parsed.ref;
    if (typeof ticket === "string") return ticket.trim();
  } catch {
    // Continue with URL and plain-text handling.
  }

  try {
    const url = new URL(value);
    const ticket =
      url.searchParams.get("ticketReference") ??
      url.searchParams.get("ticket_reference") ??
      url.searchParams.get("ticket") ??
      url.searchParams.get("ref");
    if (ticket) return ticket.trim();
  } catch {
    // A plain QR payload is already the canonical ticket reference.
  }

  return value;
}
