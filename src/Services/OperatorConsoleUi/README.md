# ExitPass Operator Console UI

## Human onboarding

New human users receive their exact stored username as a case-sensitive temporary password. The temporary credential expires after 72 hours and remains `CHANGE_REQUIRED`, so it cannot open the Operator Console workspace.

Operator Console signs in with username and password only. A valid temporary-password login creates a restricted session and opens **Change temporary password**. The user enters the current temporary password, an authenticator code, and a new password of at least eight characters. The UI calls the shared `POST /v1/human-authentication/password/change` authority with the session cookie and CSRF token. Success clears the form and restricted runtime state, returns to sign in, and requires a fresh login with the permanent password.

Password and TOTP values remain in component memory only and are never placed in URLs, browser storage, logs, or analytics.

## Fiscal Reporting

`/operator-console/fiscal-reporting` provides Site-scoped Electronic Journal, X Reading, and Z Reading access. The browser calls the Operator Console Central PMS boundary; it never calls POS Server directly, calculates fiscal totals, chooses a Z period/sequence, or creates an EJ file.

Access uses six independent permissions: `fiscal-reporting.ej.read`, `fiscal-reporting.ej.export`, `fiscal-reporting.x.read`, `fiscal-reporting.x.generate`, `fiscal-reporting.z.read`, and `fiscal-reporting.z.generate`. Z generation is separately privileged and requires an explicit close confirmation.

For local visual validation only, append `operatorFiscalReportingScenario=ready`. The fixture is guarded by `import.meta.env.DEV` and is absent from production behavior.
