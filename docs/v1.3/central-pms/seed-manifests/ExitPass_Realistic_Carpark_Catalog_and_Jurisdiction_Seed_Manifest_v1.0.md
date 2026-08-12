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

The approved canonical `effective_from` is `2026-08-13T00:00:00+08:00` (`2026-08-12T16:00:00Z`) for all 39 Site Groups, all 46 Sites, and all 46 Site-jurisdiction assignments. It is a governed seed input representing the start of canonical catalog identity and jurisdiction-mapping validity. It is **not** an activation, approval, publication, payment, fiscal, exit, or HikCentral timestamp. Future SQL must persist this exact instant and must not substitute `now()`, migration execution time, commit time, or file timestamps.

The future SQL design uses `DRAFT` for Site Groups and Sites, with public lookup and payment disabled. Jurisdiction assignments remain `PENDING_APPROVAL`. Those lifecycle and operational controls remain authoritative despite the supplied validity timestamp. `effective_to` remains blank. `Asia/Manila` and `PHP` are approved under the explicit rule that every supplied operation is in the Philippines.

Catalog identity is separate from operational activation. The manifest creates no lane, tariff, provider, vendor system, projection target, statutory policy version, payment capability, fiscal capability, or exit authority.

## Complete canonical Site type classification

Each Site is classified independently against the canonical enum. Explicit physical-form evidence takes precedence over Site Group context. `OTHER` is used only for a confirmed facility or location whose approved evidence does not fit another enum; it is not a placeholder for unresolved research. A blank ambiguity column means the selected enum is sufficiently supported for canonical seeding, while `MEDIUM` confidence preserves the narrower evidence limitation.

| Site UUID | Site code | Site name | `site_type` | Evidence source IDs | Classification rationale | Confidence | Unresolved ambiguity |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2d1dcdf8-f563-537c-8542-0bde7cc9da97 | PITX-LEVEL-3 | PITX Level 3 | STRUCTURED_PARKING | SRC-WORKBOOK-CARPARKS;SRC-PITX | The explicit Level 3 facility qualifier and PITX third-floor parking evidence establish constructed structured parking. | HIGH | None |
| b336964f-3b84-5404-8690-97ead0929b1f | PITX-OPEN-LOT | PITX Open Lot | OPEN_LOT | SRC-WORKBOOK-CARPARKS;SRC-PITX | The operator-supplied Site identity explicitly says Open Lot; physical form overrides terminal context. | HIGH | None |
| 26189a6a-1f29-5591-8467-0e40085bce2f | CYBER-EXXA-TOWER | Cyber Exxa Tower | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Site is an office-tower property, but the approved evidence does not establish a parking garage, mall, campus, terminal, or open-lot form. | MEDIUM | None |
| a37daf7e-b812-53dd-a3dd-b3889c375fb2 | ROBINSONS-MALABON | Robinsons Malabon | MALL_PARKING | SRC-WORKBOOK-CARPARKS | The operator Site identity is the Robinsons Malabon retail-mall carpark; no more specific physical form is supplied. | MEDIUM | None |
| 0d5b8df7-f4e3-58bf-a768-e6c3378f6b92 | TEKTITE-TOWERS | Tektite Towers | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Site serves an office-tower property, a facility category not otherwise represented by the enum, with no approved physical-form qualifier. | MEDIUM | None |
| 1b5b2105-dc5f-5294-a241-01f05de2dcdc | F-ORTIGAS-AVE | F. Ortigas Ave. | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Site is identified by a street location; no approved evidence establishes an open lot or constructed parking form. | MEDIUM | None |
| d1ccd074-2fe9-570f-b9d7-03970bbc6e8e | MERALCO-AVE | Meralco Ave. | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Site is identified by a street location; no approved evidence establishes an open lot or constructed parking form. | MEDIUM | None |
| e2792ce4-29e8-513d-b08d-b43ae4a212ac | ARYA-RESIDENCES | Arya Residences | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Site serves a residential property, a facility category not otherwise represented by the enum, with no approved physical-form qualifier. | HIGH | None |
| f13fbbdb-d707-519b-bf76-eefb79233005 | CYBER-SIGMA | Cyber Sigma | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Site is an office-property carpark with no approved physical-form or broader campus classification. | MEDIUM | None |
| 1f3057bf-ccc3-5322-87ae-21c6be307d79 | GRAND-CENTRAL-RESIDENCES | Grand Central Residences | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Site serves a residential property, a facility category not otherwise represented by the enum, with no approved physical-form qualifier. | HIGH | None |
| 877d773f-b07c-5c0a-bef3-6983ddc2c767 | ROCKWELL-PPM | Rockwell PPM | OTHER | SRC-WORKBOOK-CARPARKS | The approved operator abbreviation confirms the Site identity but does not, by itself, establish one of the more specific physical or property-context enum values. | MEDIUM | None |
| b1bed6db-61b5-5936-9e5b-780c4eaa0464 | ROCKWELL-SANTOLAN | Rockwell Santolan | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Rockwell property Site has no approved physical-form, mall, campus, terminal, or mixed-use qualifier in the manifest evidence. | MEDIUM | None |
| 7bc9ed78-ee43-52ca-997e-f4de6d92d572 | ROCKWELL-SHERIDAN | Rockwell Sheridan | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Rockwell property Site has no approved physical-form, mall, campus, terminal, or mixed-use qualifier in the manifest evidence. | MEDIUM | None |
| 36cb6781-2372-5629-bff1-18c2ebf8897d | DELOS-SANTOS-HOSPITAL | Delos Santos Hospital | OTHER | SRC-WORKBOOK-CARPARKS | The Site explicitly serves a hospital, a confirmed facility category not represented by another enum value. | HIGH | None |
| eb98130b-edb7-5a3f-90d5-8d474809e936 | NATIONAL-BOOKSTORE | National Bookstore | OTHER | SRC-WORKBOOK-CARPARKS | The Site explicitly serves a retail establishment rather than a confirmed mall, and no physical parking form is supplied. | HIGH | None |
| eef20dd3-20fc-572b-9574-939517abbd95 | MAKATI-CINEMA-SQUARE | Makati Cinema Square | MALL_PARKING | SRC-WORKBOOK-CARPARKS | The named Cinema Square retail complex is the mall property served by the Site; no more specific physical form is supplied. | HIGH | None |
| 52936ca1-7e08-5ac7-aa53-68245330580d | LANDMARK-ALABANG | Landmark Alabang | MALL_PARKING | SRC-WORKBOOK-CARPARKS | The operator Site identity is the Landmark Alabang retail carpark; no more specific physical form is supplied. | MEDIUM | None |
| 72ca5540-31dc-5a08-bced-e95a986f2902 | INSULAR-VALRO | Insular Valro | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed operator Site identity has no approved physical-form or matching property-context qualifier. | MEDIUM | None |
| f5d0af07-2790-588f-b83c-c1c63f740582 | ROBINSONS-OTIS | Robinsons Otis | MALL_PARKING | SRC-WORKBOOK-CARPARKS | The operator Site identity is the Robinsons Otis retail-mall carpark; no more specific physical form is supplied. | MEDIUM | None |
| 6042e5ee-d7da-5b4c-bcab-5815ef3591eb | ROBINSONS-PIONEER | Robinsons Pioneer | MALL_PARKING | SRC-WORKBOOK-CARPARKS | The operator Site identity is the Robinsons Pioneer retail-mall carpark; no more specific physical form is supplied. | MEDIUM | None |
| d6a7c750-0b44-540e-acf4-9d4aa2f2c7af | WOODLAND | Woodland | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed operator Site identity has no approved physical-form or matching property-context qualifier. | MEDIUM | None |
| 37b1a6f0-1e9c-507a-b967-81e30e95ea05 | ROBINSONS-CYBERGATE | Robinsons Cybergate | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Cybergate property Site has no approved evidence of a multi-building campus or a specific parking physical form. | MEDIUM | None |
| 53f6f0f6-4341-59e7-85f5-b526b1bdcbd0 | MANILA-HOTEL | Manila Hotel | OTHER | SRC-WORKBOOK-CARPARKS | The Site explicitly serves a hotel, a confirmed facility category not represented by another enum value. | HIGH | None |
| 1cef054e-4254-58bd-b0b7-060cca9418d6 | CYBER-BETA | Cyber Beta | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Site is an office-property carpark with no approved physical-form or broader campus classification. | MEDIUM | None |
| 995fdc44-b216-5f5c-8760-5c942da3cb40 | CYBER-TERA-CYBER-GIGA | Cyber Tera / Cyber Giga | OTHER | SRC-WORKBOOK-CARPARKS | The combined office-property identity is confirmed, but the evidence does not establish a governed campus boundary or parking physical form. | MEDIUM | None |
| e850eaad-6903-5cf7-aeb1-e0bb2e2d5a1c | MARCO-POLO-HOTEL | Marco Polo Hotel | OTHER | SRC-WORKBOOK-CARPARKS | The Site explicitly serves a hotel, a confirmed facility category not represented by another enum value. | HIGH | None |
| cf15f183-5a4d-5257-9e6a-d167aafec86b | SM-GRACE-MALL | SM Grace Mall | MALL_PARKING | SRC-WORKBOOK-CARPARKS | The Site identity explicitly says Mall and no more specific parking physical form is supplied. | HIGH | None |
| 07d89135-6777-597c-9523-0b29756a9086 | PEARL-DRIVE | Pearl Drive | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Site is identified by a drive or location name; no approved evidence establishes an open lot or constructed parking form. | MEDIUM | None |
| 922704c2-3bab-5b16-9a92-43868cec7950 | SM-MPLACE-BASEMENT | SM MPlace Basement | STRUCTURED_PARKING | SRC-WORKBOOK-CARPARKS | The explicit Basement qualifier establishes parking within a constructed structure. | HIGH | None |
| bc791bf8-6a3e-5618-82f6-ee15daf78db3 | SM-MPLACE-STREET | SM MPlace Street | OTHER | SRC-WORKBOOK-CARPARKS | The explicit Street qualifier does not match open-lot or structured-parking semantics and street parking has no dedicated enum value. | HIGH | None |
| 7c80cf1d-a3fc-5f36-9b53-d06cd093b628 | TORDESILLAS | Tordesillas | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Site is identified by a location name; no approved evidence establishes a more specific enum classification. | MEDIUM | None |
| 99999e0d-8edf-525b-99ac-a4722ca83e21 | SM-GREEN-MALL | SM Green Mall | MALL_PARKING | SRC-WORKBOOK-CARPARKS | The Site identity explicitly says Mall and no more specific parking physical form is supplied. | HIGH | None |
| b2885dd9-0424-5251-9769-ebe06c500daa | ESCOLTA | Escolta | OTHER | SRC-WORKBOOK-CARPARKS | The confirmed Site is identified by a district or street name; no approved evidence establishes a more specific enum classification. | MEDIUM | None |
| 75674321-c7d9-51de-9c17-c59c116c6d62 | UN-SQUARE-MALL | UN Square Mall | MALL_PARKING | SRC-WORKBOOK-CARPARKS | The Site identity explicitly says Mall and no more specific parking physical form is supplied. | HIGH | None |
| b4158151-e61b-5410-94ff-7715bffbf62e | BRIDGETOWNE-OPEN-LOT-BLK-15 | Bridgetowne Open Lot Block 15 | OPEN_LOT | SRC-WORKBOOK-CARPARKS;SRC-BRIDGETOWNE-EAST;SRC-PASIG-BRIDGETOWNE | The exact operator Site identity explicitly says Open Lot Block 15; official sources corroborate the Pasig-side estate. | HIGH | None |
| 4d138cea-f7f4-54a7-8f30-de1873153683 | BRIDGETOWNE-OPEN-LOT-BLK-09 | Bridgetowne Open Lot Block 09 | OPEN_LOT | SRC-WORKBOOK-CARPARKS;SRC-BRIDGETOWNE-EAST;SRC-PASIG-BRIDGETOWNE | The exact operator Site identity explicitly says Open Lot Block 09; official sources corroborate the Pasig-side estate. | HIGH | None |
| fb95fc53-3b2c-5920-9304-bbc3f3a51f5b | MACTAN-NEW-TOWN-MCDONALDS | Mactan New Town McDonald's | OTHER | SRC-WORKBOOK-CARPARKS;SRC-MACTAN-NEWTOWN | The confirmed Site serves a restaurant within the township; restaurant parking has no dedicated enum and no physical form is asserted. | HIGH | None |
| 8cd1a8db-4fdc-5509-929d-4d9c2141ce9d | MACTAN-NEW-TOWN-AL-FRESCO | Mactan New Town Al Fresco | MIXED_USE_PROPERTY | SRC-WORKBOOK-CARPARKS;SRC-MACTAN-NEWTOWN | The Al Fresco Site is identified within the officially documented Mactan Newtown mixed-use township, with no more specific physical parking form. | HIGH | None |
| 4d6bbe58-fad5-5068-9000-f5aa843ccf58 | MACTAN-NEW-TOWN-OPEN-LOT-GRAVEL | Mactan New Town Open Lot Gravel | OPEN_LOT | SRC-WORKBOOK-CARPARKS;SRC-MACTAN-NEWTOWN | The exact Site identity explicitly says Open Lot Gravel; physical form overrides township context. | HIGH | None |
| be420081-ed0a-5dbf-8721-71eb20312346 | MACTAN-NEW-TOWN-OPR | Mactan New Town OPR | OTHER | SRC-WORKBOOK-CARPARKS;SRC-MACTAN-NEWTOWN | The approved operator acronym confirms a distinct township Site but does not establish a more specific enum classification or physical form. | MEDIUM | None |
| 615ca612-68d7-546d-9851-71acc6949b41 | MACTAN-NEW-TOWN-BEACH-PARKING | Mactan New Town Beach Parking | OTHER | SRC-WORKBOOK-CARPARKS;SRC-MACTAN-NEWTOWN | The Site explicitly serves a beach amenity, a confirmed context not represented by another enum value; no physical form is asserted. | HIGH | None |
| 0e5e13bb-f2fb-59ae-9ae7-dc5df820a9b9 | MACTAN-NEW-TOWN-MUSEUM | Mactan New Town Museum | OTHER | SRC-WORKBOOK-CARPARKS;SRC-MACTAN-NEWTOWN | The Site explicitly serves a museum, a confirmed facility category not represented by another enum value. | HIGH | None |
| 270aaaff-6e3a-5b22-acb1-144ea95d5e20 | ALABANG-TOWN-CENTER | Alabang Town Center | MALL_PARKING | SRC-WORKBOOK-CARPARKS | The named Town Center retail property is the mall context served by the Site; no more specific physical form is supplied. | MEDIUM | None |
| 92611558-2c89-53ff-9ac7-9fb39c83df79 | TALAMBAN-TIMES-SQUARE | Talamban Times Square | OTHER | SRC-WORKBOOK-CARPARKS;SRC-TALAMBAN | The Site and Cebu City location are confirmed, but the approved evidence does not establish a mall, campus, terminal, mixed-use, or physical-form classification. | MEDIUM | None |
| 0217b14a-5837-5599-99e1-356bcf5ea2cc | ROBINSONS-DAVAO-CITY-DELTA | Robinsons Davao City - Delta | OTHER | SRC-WORKBOOK-CARPARKS;SRC-DAVAO-DELTA | The official developer source confirms the Cybergate Delta office property, but no approved parking physical form or campus boundary is established. | HIGH | None |
| 5360fb17-35f4-5200-b407-22515191e88d | AYALA-OPEN-LOT | Ayala Open Lot | OPEN_LOT | SRC-WORKBOOK-CARPARKS | The Site identity explicitly says Open Lot. | HIGH | None |

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
| Site Groups | `sites.site_groups`; `effective_from` maps directly to the approved controlled timestamp |
| Sites | `sites.sites`; `site_type` and `effective_from` map directly to approved canonical fields |
| Site jurisdiction assignments | `sites.site_jurisdiction_assignments`; `effective_from` maps directly to the same approved controlled timestamp |
| Statutory coverage research | Review input for `discounts.statutory_parking_policy_registries` and controlled policy-version work; not directly executable |
| Source register | Seed provenance and review evidence; implementation mechanism to be approved in the SQL task |

## Future implementation order

1. Merge and pin this reviewed manifest.
2. Reinspect the then-current canonical schema and jurisdiction commit.
3. Implement guarded, atomic canonical Site Group and Site seed SQL in `exitpassdb_v1.2`.
4. Insert all three catalog layers with the approved `2026-08-13T00:00:00+08:00` `effective_from`; never derive it from execution time.
5. Add deterministic read-only validators for IDs, topology, status, and jurisdiction.
6. Keep every realistic Site non-operational.
7. Implement synthetic-fixture migration separately, preserving historical references and rejecting UUID repurposing.
8. Review legal sources before any statutory policy seed or publication.
9. Only after those merges, implement a separate PITX Level 3 HikCentral local activation seed.

Seed conflicts must fail atomically. Rollback means reverting an unapplied source change or executing a separately reviewed forward correction after deployment; it must not rewrite historical transaction identities or repurpose fixture UUIDs.

## Validation and residual risk

`scripts/v1.3/catalog/Test-RealisticCarparkCatalogSeedManifest.ps1` verifies headers, workbook provenance and counts, UUIDv5 values, collisions, referential integrity, canonical jurisdiction mappings, Lapu-Lapu correction, multi-Site topology, policy safety, candidate uniqueness, and repository scope. It has no external module dependency and performs no database, Docker, or network operation.

Identity validation has three independent boundaries. Internal uniqueness rejects duplicate UUIDs within or across the Site Group, Site, and assignment inputs. Deterministic validation recomputes every UUIDv5 from its semantic name. External collision validation inventories tracked identities and excludes only the exact identity inputs being validated: the three catalog CSVs and this manifest, which contains the required 46-row UUID matrix. An occurrence in any unrelated tracked source remains a collision. Executable negative checks cover internal duplication, a changed semantic UUID, an unrelated fixture collision, and the case where an excluded manifest occurrence coexists with an external collision. This separation allows the validator to pass after the manifest is merged without weakening collision detection.

Residual risks requiring later approval are legal-source completeness, operator/legal approval of canonical display identities, ownership/entity metadata absent from the workbook, and controlled fixture migration. Canonical validity and Site type are now supplied; operational fields remain deliberately disabled or non-operational.
