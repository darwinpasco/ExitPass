# ExitPass I-022 Repository Baseline Evidence Register v1.0

Recorded 2026-08-10 after fetch and before I-022 edits.

| Repository | Branch | HEAD and upstream | Required merged work |
|---|---|---|---|
| Central PMS, `D:\wt\I022` | `feature/cross-application-auth-rbac-scope-integration-proof` | `938c2efbba6668bf51475cb47689d91ada967eae` = `origin/dev`; divergence `0/0` | I-018 through I-021A and H-008 present |
| Management Platform | `develop` | `5b288875ad68c230b713e5fd69a60dda99dc00de` = `origin/develop` | H-006 and H-007 present |
| Assisted Payment Terminal | `develop` | `872fe43fef52124393c564c2ef46f94decc9c242` = `origin/develop` | J-008 present |
| Canonical database | `develop` | `cdb1f6298da29e6db7e5b2ead0c8a7a162e924e4` = `origin/develop` | I-019 and I-021B present |

All four repositories were clean at preflight. External repositories were inspected read-only. Consumer builds run from disposable copies so dependency/build output does not alter those repositories.

The protected stash in the primary source repository remained `stash@{0}: On dev: WIP assisted payment terminal Mode 1 assessment` and was not touched.

