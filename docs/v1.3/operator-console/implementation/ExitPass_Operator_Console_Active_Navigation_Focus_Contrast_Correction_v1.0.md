# ExitPass Operator Console Active Navigation Focus Contrast Correction v1.0

## Purpose and provenance

This focused correction addresses the accessibility defect proven by whole-console acceptance `OPCON-MVP-ACCEPT-20260827T083008Z-CORRECTED-MOBILE-CRITERIA-RERUN` at commit `0ebfe94fb43ce1b5cdc49f3b1a1b44de3929154e`. The retained acceptance evidence remains unchanged. When the active navigation button received keyboard focus, the generic button focus background and active-navigation foreground both computed to `rgb(23, 70, 92)`, producing `1:1` text contrast and visually hiding its label.

This record covers the correction only. It does not mark whole-console runtime and visual acceptance as passed.

## Implementation

The correction is limited to the Operator Console navigation component, component-specific styles, and directly relevant tests:

- Each active module button exposes `aria-current="page"`; inactive buttons omit the attribute, and selection transfers it to the newly active route.
- The current module retains the existing visual treatment and gains a thicker leading edge plus underline, so current-route meaning does not depend on color alone.
- Navigation-only hover and focus selectors render white text on the existing dark action background.
- Navigation focus uses a darker outline that remains visible against the white navigation panel.
- The global `:focus-visible` behavior for all unrelated controls is unchanged.
- Navigation order, labels, routes, permissions, visibility rules, and the always-visible responsive design are unchanged.

## Computed contrast and responsive proof

Production-bundle Chromium measurements at `1440x900`, `768x1024`, and `390x844` returned the same computed values at every viewport:

| State | Foreground | Background or adjacent color | Contrast |
|---|---|---|---:|
| Active, not focused | `rgb(23, 70, 92)` | `rgb(231, 242, 245)` | `8.91:1` |
| Active, keyboard focused | `rgb(255, 255, 255)` | `rgb(23, 70, 92)` | `10.16:1` |
| Inactive, keyboard focused | `rgb(255, 255, 255)` | `rgb(23, 70, 92)` | `10.16:1` |
| Focus outline | `rgb(117, 80, 0)` | `rgb(255, 255, 255)` | `7.23:1` |

All text measurements exceed the `4.5:1` target and the focus indicator exceeds the applicable `3:1` target. At all three viewports, document width equalled viewport width, all ten navigation controls remained visible and keyboard-reachable in their original order, Enter and Space activated the existing button semantics, and exactly one current-page attribute moved with route selection.

Direct screenshot inspection found no hidden label, clipping, overlap, horizontal overflow, or hidden primary action. The existing always-visible mobile navigation remains intact. Drawer-specific requirements remain `NOT_APPLICABLE_BY_DESIGN` because production navigation is always rendered and no authoritative requirement mandates a collapsible mobile navigation component.

## Boundaries

No backend, API, schema, migration, locked v1.2 document, authentication, CSRF, RBAC, Site/Site Group, device, shift, authorization-epoch, statutory lifecycle, PHP, mTLS, or external-client behavior changes. Operator Console remains a same-origin Central PMS client only. No gate-related component is introduced.

## Completion posture

The correction is self-reviewed targeted evidence. Independent review is not performed. Exact external results and screenshots are stored under `D:\SourceCodes\ExitPass.local\operator-console-active-navigation-focus-contrast\OPCON-NAV-FOCUS-20260827T095503Z` and are not committed.

After merge, the next task is **Codex E: Resume Exact-HEAD Operator Console Whole-Console Runtime and Visual Acceptance**.
