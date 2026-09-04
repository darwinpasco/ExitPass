# Persistent Real-Site IST Catalog

This runbook initializes the one persistent, non-production Site catalog used by integrated business-journey runs. Application containers remain disposable; the two PostgreSQL volumes do not.

## Authority and boundaries

- Catalog identity comes only from the reviewed `ExitPass_Realistic_Carpark_*_v1.0.csv` manifests.
- The initializer preserves all 39 Site Group IDs, 46 Site IDs, and 46 assignment IDs.
- Catalog lifecycle and Site-jurisdiction assignments become active. Public lookup, payment, HikCentral, and fiscal capabilities remain independently disabled until authoritative Site configuration is supplied.
- Statutory coverage is stored per jurisdiction and entitlement. Unknown values remain null. Parañaque Senior Citizen and PWD availability is active with operator review required and automatic application disabled.
- `TEST_SITE`, `Site A`, and Restart fixtures are not members of `ist_configuration.real_site_catalog_members` and cannot resolve through its selector.

## Initialize or safely rerun

Set `EXITPASS_IST_DB_PASSWORD` in the operator's private environment, then run:

```powershell
./scripts/v1.3/catalog/Initialize-PersistentRealSiteIstCatalog.ps1 `
  -EvidenceDirectory D:\SourceCodes\ExitPass.local\integrated-system-test\EXITPASS-IST-20260820T052021Z-B580639E\persistent-real-site-ist-catalog
```

The owned persistent resources are:

- network `exitpass-ist-persistent`
- ExitPass database container/volume `exitpass-ist-persistent-db` / `exitpass-ist-persistent-data`
- POS database container/volume `exitpass-pos-ist-persistent-db` / `exitpass-pos-ist-persistent-data`

Rerunning the command is idempotent. A partial or semantically conflicting canonical seed fails before activation.

## Select a Site

Future IST runs set a stable human-facing code, for example:

```powershell
$env:IST_SITE_CODE = 'PITX-LEVEL-3'
```

The runtime resolves it with:

```sql
SELECT * FROM ist_configuration.resolve_real_site('PITX-LEVEL-3');
```

Only the 46 reviewed real Sites can resolve through this function.

## Readiness and cleanup

Export the current 46-row matrix with `Export-PersistentRealSiteIstReadiness.ps1`. Update `ist_configuration.site_operational_capabilities` only after the corresponding governed Site configuration and verification exists.

HikCentral readiness covers two independent hops. The private Site Adapter configuration owns the upstream HikCentral URL and credentials. Central PMS resolves the provider-neutral Site Adapter hop from the canonical `identity.service_identities` and `integration.*` route tables. `hikcentral_target_configured` and `hikcentral_connectivity_verified` do not make a Site ready unless exactly one effective `SITE_ADAPTER_API` route resolves; zero or multiple routes fail closed.

The operational input supplies these routing facts only for Sites whose HikCentral target is configured:

- `site_adapter_base_url`: stable adapter network address, never the HikCentral URL;
- `site_adapter_environment_code`: must match `CentralPms:VendorPms:Environment`;
- `site_adapter_secret_reference`: mounted `file:` reference relative to `CentralPms:VendorPms:AdapterSecretMountRoot`;
- `central_pms_service_identity_id`: existing active Central PMS service identity.

For persistent PITX IST, the stable application-network identity is `http://pitx-site-adapter:8080/`. The Restart Compose/runtime must assign `pitx-site-adapter` as the adapter service name or network alias and explicitly enable task-owned HTTP only in `IntegrationTest`. The adapter independently reaches HikCentral at `https://sys-service.exitpass.test:443` with strict certificate validation. Production adapter routes remain HTTPS-only.

The importer deterministically materializes the adapter identity, vendor system, Central-PMS-owned external credential reference, `SITE_ADAPTER_API` endpoint, and Site mapping. It stores only the `file:` reference; the API key remains under the private persistent IST secret root and is mounted into both runtimes. Rerunning the importer preserves the canonical IDs and exactly-one route cardinality.

Ordinary Restart cleanup must not remove either persistent database container or volume, the persistent network, or governed catalog/configuration rows. Back up both databases before schema migration; migrate forward without truncating transaction history or reseeding Site identities.
