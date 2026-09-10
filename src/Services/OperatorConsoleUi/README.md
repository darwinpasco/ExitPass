# ExitPass Operator Console UI

## Fiscal Reporting

`/operator-console/fiscal-reporting` provides Site-scoped Electronic Journal, X Reading, and Z Reading access. The browser calls the Operator Console Central PMS boundary; it never calls POS Server directly, calculates fiscal totals, chooses a Z period/sequence, or creates an EJ file.

Access uses six independent permissions: `fiscal-reporting.ej.read`, `fiscal-reporting.ej.export`, `fiscal-reporting.x.read`, `fiscal-reporting.x.generate`, `fiscal-reporting.z.read`, and `fiscal-reporting.z.generate`. Z generation is separately privileged and requires an explicit close confirmation.

For local visual validation only, append `operatorFiscalReportingScenario=ready`. The fixture is guarded by `import.meta.env.DEV` and is absent from production behavior.
