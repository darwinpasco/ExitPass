# ExitPass Realistic Carpark Catalog and Jurisdiction Seed Manifest v1.0

Status: `READY_FOR_REVIEW` manifest; no database seed or operational activation is included.

## Purpose and boundaries

This package translates the operator-supplied carpark workbook into reviewable Site Group, Site, Site-jurisdiction, statutory-policy research, and provenance manifests. It is an input to a later governed SQL task. It does not seed a database, activate a Site, enable WebPay public lookup or payment, publish a statutory-discount policy, configure HikCentral credentials, contact HikCentral, authorize fiscal issuance or exit, or rewrite historical transaction identities.

The source workbook remains outside Git. No workbook copy or substitute workbook is part of this package.

## Authoritative workbook provenance

| Property | Observed value |
| --- | --- |
| Absolute path | `D:\Docs\Carparks.xlsx` |
| Source filename retained in manifests | `Carparks.xlsx` |
| SHA-256 | `63c20cd3aba3e13d6f9fc022083507c0bc43a2ab9c751e9084dd19c59969359a` |
| File size | 8,123 bytes |
| Last modified (UTC, observed read-only) | `2026-08-12T03:12:55.0000000Z` |
| Workbook structure | Valid Open XML workbook |
| Worksheet inventory | `Sheet1` (visible), used range `A1:C47` |
| Hidden or very-hidden worksheets | None |
| Hidden rows or columns | None |
| Merged cells | None |
| Formula cells | None |
| Physical/nonblank rows | 47 including one header row |
| Source columns | `SITE GROUP`, `SITE`, `CITY` |
| Data rows | 46, source rows 2 through 47 |

Read-only inspection preserved the observed file size and creation/last-write timestamps. The recomputed SHA-256 matches the blocked attempt's recorded hash, so this continuation uses the same authoritative workbook bytes.

## Count reconciliation

The authoritative totals are **39 Site Groups** and **46 Sites**. Both exact grouping and normalized grouping produce 39 Site Groups. There are no duplicate source rows and no normalized duplicate Sites.

The earlier 41 count is explained by collapsing only the six Mactan New Town rows into one group: `46 - 5 = 41`. It omitted the additional duplicate-parent reductions for PITX (`-1`) and Bridgetowne (`-1`), which yield `39`. Trailing spaces on `ROBINSONS PIONEER ` and `ALABANG TOWN CENTER ` are real source anomalies but each occurs once and therefore does not change either the exact or normalized Site Group total.

## Normalization

1. Trim leading and trailing whitespace.
2. Collapse repeated internal whitespace to one space.
3. Preserve punctuation, facility qualifiers, block numbers, levels, and branding.
4. Preserve original workbook values and source row numbers alongside normalized display values.
5. Treat exact duplicates and normalized duplicates separately; neither exists among Site rows.
6. Do not merge similar names without source evidence.
7. Generate uppercase ASCII codes by replacing non-alphanumeric runs with one hyphen and trimming edge hyphens. Code collisions fail validation.
8. Preserve `Parañaque` in canonical customer-facing text even though the workbook uses `PARANAQUE`.
9. Resolve every Site to a city or municipality; broad labels such as `CEBU` and `DAVAO` remain only in `original_location_label`.

## Complete multi-Site topology

| Site Group | Workbook rows | Sites | Jurisdiction result |
| --- | --- | --- | --- |
| PITX | 2-3 | PITX Level 3; PITX Open Lot | City of Parañaque |
| Bridgetowne | 36-37 | Bridgetowne Open Lot Block 15; Bridgetowne Open Lot Block 09 | City of Pasig for both supplied Sites |
| Mactan New Town | 38-43 | McDonald's; Al Fresco; Open Lot Gravel; OPR; Beach Parking; Museum | City of Lapu-Lapu for all six |

Bridgetowne is intentionally resolved at Site level. The operator workbook identifies both open-lot blocks as Pasig; the DENR Le Pont EIS identifies the Bridgetowne East/C5 Pasig estate, and Pasig City identifies its Bridgetowne location in Rosario. A separate DENR project description identifies Bridgetowne West in Ugong Norte, Quezon City. That separate evidence proves that a future Bridgetowne Site cannot inherit jurisdiction from the Site Group.

Mactan New Town is not treated as generic `CEBU`. Megaworld identifies the township in Lapu-Lapu City. All six rows reuse canonical jurisdiction UUID `23104fc9-a144-381c-4347-ccb2aa1a2998`, jurisdiction code `LAPU_LAPU`, PSGC `0731100000`, and Region VII. Talamban Times Square is separately assigned to City of Cebu based on the workbook and the official FDA establishment registry.

The single Davao row is Cybergate Delta. Robinsons Land identifies it on J.P. Laurel Avenue in Davao City, so the broad workbook label `DAVAO` resolves to `DAVAO_CITY` rather than another Davao-region LGU.

## Canonical jurisdiction reuse

The 46 Sites resolve to 13 existing canonical jurisdictions: Makati, Malabon, Mandaluyong, Manila, Muntinlupa, Parañaque, Pasig, Quezon City, San Juan, Taguig, Cebu City, Lapu-Lapu City, and Davao City. No jurisdiction UUID is allocated by this manifest.

All represented LGUs are canonical highly urbanized cities. NCR is recorded as region `NCR`, never as a province. Cebu City and Lapu-Lapu City are Region VII HUCs with no province foreign key; Davao City is a Region XI HUC with no province foreign key.

The earlier Lapu-Lapu blocker was resolved in `exitpassdb_v1.2` commit `1e307c2bd56c2738a92cdd87571f6caeeaf07b3d` by an identity-preserving correction. Current official PSA facts are PSGC `0731100000` and separate correspondence code `072226000`. The canonical jurisdiction code is `PH-PSGC-0731100000`; the stable UUID remains `23104fc9-a144-381c-4347-ccb2aa1a2998`. Existing Metro Cebu and statutory-policy relationships continue to reference that UUID. Retired `0730110000` is not an active proposed catalog value and no second canonical Lapu-Lapu identity exists.

## Deterministic catalog identity

New proposed Site Group, Site, and assignment IDs are RFC 4122 UUIDv5 values using the standard URL namespace:

`6ba7b811-9dad-11d1-80b4-00c04fd430c8`

Semantic names are:

- `https://exitpass.ph/v1.3/carparks/site-groups/{site-group-code}`
- `https://exitpass.ph/v1.3/carparks/sites/{site-code}`
- `https://exitpass.ph/v1.3/carparks/site-jurisdiction-assignments/{site-code}/{jurisdiction-code}`

Codes derive from normalized identities, not workbook row order. Every UUID is stored literally in CSV and independently recomputed by the validator. Canonical jurisdiction IDs are reused as-is and are not recalculated when an external PSGC changes. The colliding `77000000-0000-0000-0000-000000000001` and `77000000-0000-0000-0000-000000000002` fixture UUIDs are prohibited.

## Safe initial posture

The future SQL design represented here uses `DRAFT` for Site Groups and Sites, with public lookup and payment disabled. Jurisdiction assignments are proposed as `PENDING_APPROVAL`. `proposed_effective_from` is intentionally blank because no historical or approval date was supplied. `Asia/Manila` and `PHP` are proposed under the explicit rule that every supplied operation is in the Philippines.

Catalog identity is separate from operational activation. The manifest creates no lane, tariff, provider, vendor system, projection target, statutory policy version, payment capability, fiscal capability, or exit authority.

## Statutory coverage posture

Coverage is analyzed independently for `SENIOR_CITIZEN` and `PWD` for every represented jurisdiction. A Site Group is business topology, not ordinance authority; policy resolution must use the Site's canonical jurisdiction assignment.

The source-quality labels are manifest review labels, not new database controlled codes:

- `OFFICIAL_PRIMARY_SOURCE_RETAINED`
- `OFFICIAL_REFERENCE_IDENTIFIED_FULL_TEXT_UNAVAILABLE`
- `OPERATIONALLY_VERIFIED_OFFICIAL_TEXT_UNAVAILABLE`
- `SECONDARY_SOURCE_EVIDENCE_ONLY`
- `CONFLICTING_EVIDENCE`
- `NO_LOCAL_PARKING_SPECIFIC_POLICY_IDENTIFIED`

No row is marked runtime-publication eligible. Unknown durations, percentages, dates, ordinance numbers, residency rules, and documentary requirements remain blank rather than using zero or an invented value.

For Parañaque Senior Citizen coverage, the established project distinction is preserved: full-fee parking treatment is operationally verified and practiced, while official ordinance text and number remain unavailable online. It is not described as unverified, proposed, or verified from retained primary ordinance text. It remains subject to manual legal review and controlled activation. Parañaque PWD coverage is separately represented from its own repository research mapping and is not copied from the Senior Citizen row.

Quezon City and Muntinlupa have official LGU references retained in the source register. Manila, Mandaluyong, Cebu City, and Parañaque rows preserve repository research classifications and unresolved source limitations. Mandaluyong and Cebu City conflicts remain non-executable. Jurisdictions with no identified local parking-specific policy remain distinct from national entitlement law.

## Proposed HikCentral candidate

Exactly one Site is marked `PROPOSED_NOT_ACTIVATED`: **PITX Level 3**. It exists in workbook row 2, belongs unambiguously to PITX, is located in Parañaque, and PITX's official parking page identifies third-floor private-vehicle parking. Parking Lot Index Code `1` is a future activation proposal only.

This package does not create a Vendor System, projection target, endpoint, credential, connection string, or activation seed.

## Manifest-to-schema map

| Manifest | Future canonical objects |
| --- | --- |
| Site Groups | `sites.site_groups` |
| Sites | `sites.sites` |
| Site jurisdiction assignments | `sites.site_jurisdiction_assignments` |
| Statutory coverage research | Review input for `discounts.statutory_parking_policy_registries` and controlled policy-version work; not directly executable |
| Source register | Seed provenance and review evidence; implementation mechanism to be approved in the SQL task |

## Future implementation order

1. Merge and pin this reviewed manifest.
2. Reinspect the then-current canonical schema and jurisdiction commit.
3. Implement guarded, atomic canonical Site Group and Site seed SQL in `exitpassdb_v1.2`.
4. Insert jurisdiction assignments only after an approved effective date is supplied.
5. Add deterministic read-only validators for IDs, topology, status, and jurisdiction.
6. Keep every realistic Site non-operational.
7. Implement synthetic-fixture migration separately, preserving historical references and rejecting UUID repurposing.
8. Review legal sources before any statutory policy seed or publication.
9. Only after those merges, implement a separate PITX Level 3 HikCentral local activation seed.

Seed conflicts must fail atomically. Rollback means reverting an unapplied source change or executing a separately reviewed forward correction after deployment; it must not rewrite historical transaction identities or repurpose fixture UUIDs.

## Validation and residual risk

`scripts/v1.3/catalog/Test-RealisticCarparkCatalogSeedManifest.ps1` verifies headers, workbook provenance and counts, UUIDv5 values, collisions, referential integrity, canonical jurisdiction mappings, Lapu-Lapu correction, multi-Site topology, policy safety, candidate uniqueness, and repository scope. It has no external module dependency and performs no database, Docker, or network operation.

Residual risks requiring later approval are legal-source completeness, effective dates, operator/legal approval of canonical display identities, ownership/entity metadata absent from the workbook, and controlled fixture migration. The manifests deliberately leave these fields blank or non-operational.
