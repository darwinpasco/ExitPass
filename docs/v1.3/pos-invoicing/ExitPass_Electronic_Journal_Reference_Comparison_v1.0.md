# Electronic Journal reference-sample comparison

Status: G4 acceptance evidence
Reference role: structural and presentation reference, not a byte-for-byte golden master

## Accepted parity boundary

`CanonicalSalesInvoiceTextRenderer` is the sole formatter for the printer-ready visible-text payload and the immutable Electronic Journal invoice text persisted during Sales Invoice issuance. For a given Sales Invoice, the UTF-8 visible-text bytes produced for those two consumers must be identical. Device initialization, feed, cut, drawer, status, transport, and other ESC/POS control bytes are outside this equality.

Historical invoices without an immutable canonical printable-text payload are returned as authoritative text unavailable. They must not be reconstructed from current or mutable templates.

## Structural comparison

| Reference-sample element | Current canonical coverage | Authoritative source / behavior |
|---|---|---|
| SALES INVOICE header | Yes | Fixed canonical heading, centered within the current 48-column presentation. |
| Supplier identity | Yes | Snapshotted registered business name and address. |
| Site/location | Yes | Snapshotted parking-location display; document context may provide the issuance-specific display. |
| Supplier TIN | Yes | Snapshotted fiscal-identity header. |
| Serial number and MIN | Yes | Snapshotted POS serial number and machine identification number. |
| Parking location | Yes | Snapshotted header, with authoritative issuance context taking precedence when present. |
| Terminal identity | Yes | Issuance context, snapshotted terminal ID, or channel-terminal identity in that order. |
| SI number | Yes | Assigned authoritative fiscal document number. |
| Issued date/time | Yes | Fiscal-number assignment timestamp, normalized to UTC, plus business date. |
| Parking details | Yes when available | A section is emitted only when at least one authoritative parking detail exists. |
| Ticket and plate | Yes when available | Issuance reference context; no value is invented when absent. |
| Entry time, payment time, duration | Yes when available | Issuance reference context is rendered as captured. |
| Line items | Yes | Fiscal document lines in authoritative line sequence. |
| Subtotal | Yes | Sum of persisted gross line amounts, calculated inside POS Server. |
| Discounts | Yes | Persisted fiscal discount facts; the statutory entitlement labels the discount when available. |
| VAT breakdown | Yes | Current approved labels are always present: VATable Sales, VAT Amount, VAT Exempt Sales, Zero Rated Sales. Zero values remain visible. |
| Payment details | Yes | Persisted tenders are shown using the governed tender key/provider when available. |
| Total paid | Yes | Persisted tender total, or authoritative final payable value where no separate tender exists. |
| Tendered and change | Yes when available | Rendered only from authoritative issuance-context minor-unit values. |
| Sales Invoice declaration | Yes | Snapshotted current Sales Invoice legal statement. |
| Print/issued presentation timestamp | Yes | Canonical issuance/presentation timestamp in UTC; G4 has no device-specific later print timestamp. |
| Customer information | Yes | Current contract: Customer Name, Address, TIN, Business Style, contextual OSCA/PWD ID, and signature line. |
| Supplier/accreditation/PTU footer | Yes | Snapshotted supplier-developer, accreditation, and PTU information; key fiscal identifiers also remain visible in the header. |
| Separators | Yes | Stable 48-character separators delimit the canonical sections. |
| Complete invoice-block boundaries | Yes | Every block ends in CRLF; sequential blocks are separated by an additional CRLF. |
| Multiple complete invoices | Yes | Export orders complete immutable blocks chronologically and deduplicates fiscal-document replay. |

## Intentional differences from the legacy sample

- The current approved VAT labels replace older VAT wording and remain visible even when their value is `PHP 0.00`.
- The current customer-information contract is authoritative and includes Business Style, contextual OSCA/PWD identification, and the current signature-line behavior.
- Current fiscal-identity/header snapshots determine supplier, Site, terminal, accreditation, and PTU presentation; legacy sample values and ordering are not copied into production code.
- Parking and optional tender/change details are omitted when the issuance record has no authoritative value. Blank sample-shaped values are not fabricated.
- The canonical timestamp is the authoritative issuance/presentation timestamp in UTC. A separate device transport timestamp does not exist in G4.
- Section order follows the current canonical Sales Invoice contract. Structural equivalence does not require preserving legacy sample ordering where the current approved contract differs.
- The export filename follows the governed business-date range (`electronic-journal-YYYYMMDD[-YYYYMMDD].txt`) rather than any legacy naming convention.

## Residual risk

`PHYSICAL_PRINTER_TRANSPORT_PARITY_DEFERRED`

No physical Sales Invoice printer adapter exists in the POS Server repository. The future adapter must consume the exact printer-ready visible-text bytes from `CanonicalSalesInvoiceTextRenderer` and must not reconstruct, reorder, relabel, or recalculate Sales Invoice content. Hardware transport verification belongs to the separate printer-integration task.
