import { expect, test, type Page } from "@playwright/test";

const scenarios = {
  seniorRepresentative: "/operator-console/statutory-discounts/senior-representative-optional",
  pwdRepresentative: "/operator-console/statutory-discounts/pwd-representative-unspecified",
  residencyRequired: "/operator-console/statutory-discounts/residency-required",
  driverRequired: "/operator-console/statutory-discounts/driver-required",
  passengerRequired: "/operator-console/statutory-discounts/passenger-required",
  missingEvidence: "/operator-console/statutory-discounts/missing-evidence",
  malformed: "/operator-console/statutory-discounts/malformed-authority",
  unsupported: "/operator-console/statutory-discounts/unsupported-effect",
  paranaque: "/operator-console/statutory-discounts/paranaque-operational",
  approved: "/operator-console/statutory-discounts/approved-request",
  rejected: "/operator-console/statutory-discounts/rejected-request"
};

// Retired v1.2 draft-workspace assertions. The active canonical Central PMS path is
// covered by operator-console-central-pms-statutory-review.spec.ts.
test.describe.skip("Operator Console statutory review UX browser smoke (obsolete draft path)", () => {
  test("Senior Citizen representative transaction is operational and presence optional", async ({ page }) => {
    await page.goto(scenarios.seniorRepresentative);

    await expect(page.locator("h2").filter({ hasText: "Senior Citizen Parking Privilege" })).toBeVisible();
    await expect(page.getByText("Benefit").locator("..")).toContainText("Parking discount");
    await expect(page.getByText("Location eligibility").locator("..")).toContainText("Confirmed");
    await expect(page.getByText("Valid Senior Citizen ID").first()).toBeVisible();
    await expect(page.getByText("Beneficiary must be present")).toHaveCount(0);
    await expectOperationalMetadataHidden(page);
    await expectTechnicalSectionsAbsent(page);

    const approve = page.getByRole("button", { name: "Approve" });
    await expect(approve).toBeDisabled();
    await expect(page.getByLabel("Decision").getByText("Blocked")).toBeVisible();
    await page.getByLabel(/I verified the required beneficiary documents/i).check();
    await expect(page.getByLabel("Decision").getByText("Ready")).toBeVisible();
    await expect(approve).toBeEnabled();
    await expect(page.getByRole("button", { name: "Reject" })).toBeEnabled();
    await expectNoUnsafeDisclosure(page);
    await page.screenshot({ path: test.info().outputPath("senior-representative-optional.png"), fullPage: true });
  });

  test("PWD representative transaction remains approvable when presence is unspecified", async ({ page }) => {
    await page.goto(scenarios.pwdRepresentative);

    await expect(page.locator("h2").filter({ hasText: "PWD Parking Privilege" })).toBeVisible();
    await expect(page.getByText("Valid PWD ID").first()).toBeVisible();
    await expect(page.getByText("Beneficiary must be present")).toHaveCount(0);
    await page.getByLabel(/I verified the required beneficiary documents/i).check();
    await expect(page.getByRole("button", { name: "Approve" })).toBeEnabled();
    await expectOperationalMetadataHidden(page);
    await expectTechnicalSectionsAbsent(page);
    await expectNoUnsafeDisclosure(page);
    await page.screenshot({ path: test.info().outputPath("pwd-representative-unspecified.png"), fullPage: true });
  });

  test("Residency requirement remains visible and blocks approval when missing", async ({ page }) => {
    await page.goto(scenarios.residencyRequired);

    await expect(page.getByRole("strong").filter({ hasText: "Proof of residency" })).toBeVisible();
    await expect(page.getByText(/proof of residency is missing/i)).toBeVisible();
    await expect(page.locator("body")).not.toContainText("Ready for review");
    await expect(page.getByLabel("Decision").getByText("Blocked")).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve" })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Reject" })).toBeEnabled();
    await expectOperationalMetadataHidden(page);
    await expectTechnicalSectionsAbsent(page);
    await page.screenshot({ path: test.info().outputPath("residency-required.png"), fullPage: true });
  });

  test("Driver and passenger requirements are policy-driven blockers", async ({ page }) => {
    await page.goto(scenarios.driverRequired);
    await expect(page.getByText("Beneficiary must be the driver").first()).toBeVisible();
    await expect(page.getByText(/required driver condition is verified/i)).toBeVisible();
    await expect(page.locator("body")).not.toContainText("Ready for review");
    await expect(page.getByLabel("Decision").getByText("Blocked")).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve" })).toBeDisabled();

    await page.goto(scenarios.passengerRequired);
    await expect(page.getByText("Beneficiary must be a passenger").first()).toBeVisible();
    await expect(page.getByText(/required passenger condition is verified/i)).toBeVisible();
    await expect(page.locator("body")).not.toContainText("Ready for review");
    await expect(page.getByLabel("Decision").getByText("Blocked")).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve" })).toBeDisabled();
    await expectNoUnsafeDisclosure(page);
    await expectTechnicalSectionsAbsent(page);
    await page.screenshot({ path: test.info().outputPath("passenger-required.png"), fullPage: true });
  });

  test("Missing evidence, malformed authority, and unsupported benefit effect fail closed", async ({ page }) => {
    await page.goto(scenarios.missingEvidence);
    await expect(page.getByText(/required documents are missing or need review/i)).toBeVisible();
    await expect(page.locator("body")).not.toContainText("Ready for review");
    await expect(page.getByLabel("Decision").getByText("Blocked")).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve" })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Reject" })).toBeEnabled();

    await page.goto(scenarios.malformed);
    await expect(page.getByLabel("Decision").getByText(/location eligibility record is incomplete/i)).toBeVisible();
    await expect(page.getByText("Parking-location eligibility could not be confirmed.")).toBeVisible();
    await expect(page.getByText("Reject the request or ask support to refresh the parking-location record.")).toBeVisible();
    await expect(page.locator("body")).not.toContainText("This parking location is eligible for the requested privilege.");
    await expect(page.locator("body")).not.toContainText("Ready for review");
    await expect(page.getByLabel("Decision").getByText("Blocked")).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve" })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Reject" })).toBeEnabled();

    await page.goto(scenarios.unsupported);
    await expect(page.getByText("Free parking")).toBeVisible();
    await expect(page.getByLabel("Decision").getByText(/parking benefit is not supported/i)).toBeVisible();
    await expect(page.locator("body")).not.toContainText("Ready for review");
    await expect(page.getByLabel("Decision").getByText("Blocked")).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve" })).toBeDisabled();
    await expect(page.locator("body")).not.toContainText(/20% discount/i);
    await expectOperationalMetadataHidden(page);
    await expectTechnicalSectionsAbsent(page);
    await expectNoUnsafeDisclosure(page);
    await page.screenshot({ path: test.info().outputPath("unsupported-effect.png"), fullPage: true });
  });

  test("Paranaque verified operational free-parking review stays operational", async ({ page }) => {
    await page.goto(scenarios.paranaque);

    await expect(page.locator("h2").filter({ hasText: "Senior Citizen Parking Privilege" })).toBeVisible();
    await expect(page.getByText("Free parking")).toBeVisible();
    await expect(page.getByText("Proof of residency").first()).toBeVisible();
    await expect(page.getByText("Valid Senior Citizen ID").first()).toBeVisible();
    await expect(page.locator("body")).not.toContainText(/PROPOSED|NO_LOCAL_RULE_FOUND|nonexistent/i);
    await expect(page.locator("body")).not.toContainText(/PWD Ordinance|20% discount|PARANAQUE_SC_OPERATIONAL|PH-137604000/i);
    await expectNoUnsafeDisclosure(page);
    await expectTechnicalSectionsAbsent(page);
    await page.screenshot({ path: test.info().outputPath("paranaque-operational.png"), fullPage: true });
  });

  test("approved and rejected states stay compact and operational", async ({ page }) => {
    await page.goto(scenarios.approved);
    await expect(page.getByText("Parking privilege approved")).toBeVisible();
    await expect(page.getByText(/Central PMS will apply the approved privilege when the customer proceeds with payment through WebPay or the Cashier-Assisted Terminal/i)).toBeVisible();
    await expect(page.locator("body")).not.toContainText("Ready for review");
    await expect(page.getByLabel("Decision")).not.toContainText("Ready");
    await expect(page.getByText("Approved", { exact: true }).first()).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Reject" })).toHaveCount(0);
    await expect(page.locator("body")).not.toContainText(/Amount update|Update parking amount|application id|original tariff snapshot|applied tariff snapshot/i);
    await expectTechnicalSectionsAbsent(page);

    await page.goto(scenarios.rejected);
    await expect(page.getByText("Parking privilege rejected")).toBeVisible();
    await expect(page.getByText("Document is invalid")).toBeVisible();
    await expect(page.locator("body")).not.toContainText("Ready for review");
    await expect(page.getByLabel("Decision")).not.toContainText("Ready");
    await expect(page.getByText("Rejected", { exact: true }).first()).toBeVisible();
    await expect(page.locator("body")).not.toContainText("ID_NOT_VALID");
    await expect(page.getByRole("button", { name: "Approve" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Reject" })).toHaveCount(0);
    await expectTechnicalSectionsAbsent(page);
    await page.screenshot({ path: test.info().outputPath("rejected-request.png"), fullPage: true });
  });

  test("keyboard path reaches attestation and preserves readable decision controls", async ({ page }) => {
    await page.goto(scenarios.seniorRepresentative);

    const attestation = page.getByLabel(/I verified the required beneficiary documents/i);
    await attestation.focus();
    await expect(attestation).toBeFocused();
    await page.keyboard.press("Space");
    await expect(attestation).toBeChecked();

    const approve = page.getByRole("button", { name: "Approve" });
    const reject = page.getByRole("button", { name: "Reject" });
    await expect(approve).toBeEnabled();
    await expect(reject).toBeEnabled();
    await approve.focus();
    await expect(approve).toBeFocused();
    await expectNoUnsafeDisclosure(page);
    await expectTechnicalSectionsAbsent(page);
    await page.screenshot({ path: test.info().outputPath("keyboard-attestation.png"), fullPage: true });
  });
});

async function expectOperationalMetadataHidden(page: Page) {
  const body = page.locator("body");
  await expect(body).not.toContainText(/ordinance number|jurisdiction code|policy version|publication status|verification status/i);
  await expect(body).not.toContainText(/ACTIVE_FOR_TRANSACTION_USE|VERIFIED_OFFICIAL|VERIFIED_ACTIVE_OPERATIONAL|STATUTORY_DISCOUNT_VAT_EXEMPT/i);
  await expect(body).not.toContainText(/8a000000-0000-0000-0000|QC_PWD_PARKING_2026|policy-resolution/i);
}

async function expectNoUnsafeDisclosure(page: Page) {
  const body = page.locator("body");
  await expect(body).not.toContainText(/appsecret|bearer\s+[a-z0-9]|connection string|database password|npgsql|stack trace|raw response json|base64,|raw evidence image|credential-secret/i);
  await expect(page.getByRole("button", { name: /raw evidence|raw id|select policy|select ordinance|change jurisdiction/i })).toHaveCount(0);
}

async function expectTechnicalSectionsAbsent(page: Page) {
  const body = page.locator("body");
  await expect(body).not.toContainText(/Draft summary|Parking session context|Entitlement context|Workflow state|Audit activity placeholder/i);
  await expect(body).not.toContainText(/Operator readiness state|Readiness dimensions|Evidence count|Storage reference|Metadata-only/i);
  await expect(body).not.toContainText(/Final verification|Applied tariff snapshot ID|Payable basis status|Correlation ID/i);
}
