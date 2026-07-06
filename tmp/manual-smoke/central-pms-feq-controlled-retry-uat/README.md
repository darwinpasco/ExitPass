# Central PMS FEQ Controlled Retry UAT Helpers

Disposable helper files for the FEQ controlled retry execution UAT runbook.

## Create Evidence Folder

From repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\tmp\manual-smoke\central-pms-feq-controlled-retry-uat\New-UatEvidenceFolder.ps1
```

This creates a local evidence folder under `tmp/manual-smoke/central-pms-feq-controlled-retry-uat/evidence/` and copies the UAT evidence checklist into it.

The helper does not call Central PMS, POS Server, or any database. It does not execute retry.
