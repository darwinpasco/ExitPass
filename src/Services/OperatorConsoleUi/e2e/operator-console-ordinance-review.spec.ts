import { expect, test, type Page } from "@playwright/test";

const scenarios = {
  active: "/operator-console/statutory-discounts/active-authority",
  missing: "/operator-console/statutory-discounts/missing-authority",
  malformed: "/operator-console/statutory-discounts/malformed-authority",
  unsupported: "/operator-console/statutory-discounts/unsupported-effect",
  paranaque: "/operator-console/statutory-discounts/paranaque-operational"
};

test.describe("Operator Console statutory ordinance review browser smoke", () => {
  test("complete active ordinance authority renders read-only facts and attestation gates approval", async ({ page }) => {
    await page.goto(scenarios.active);

    await expect(page.getByRole("heading", { name: "Quezon City" })).toBeVisible();
    await expect(page.getByText("PH-137404000")).toBeVisible();
    await expect(page.getByText("QC_PWD_PARKING_2026").first()).toBeVisible();
    await expect(page.getByText("Quezon City PWD Parking Benefit").first()).toBeVisible();
    await expect(page.getByText("v2 (8a000000)")).toBeVisible();
    await expect(page.getByText("QC Ordinance 2026-04").first()).toBeVisible();
    await expect(page.getByText("Jan 1, 2026, 12:00 AM").first()).toBeVisible();
    await expect(page.getByText("Dec 31, 2026, 11:59 PM").first()).toBeVisible();
    await expect(page.getByText("VERIFIED_OFFICIAL").first()).toBeVisible();
    await expect(page.getByText("ACTIVE_FOR_TRANSACTION_USE")).toBeVisible();
    await expect(page.getByText(/pwd id - required - masked pwd id reference/i)).toBeVisible();
    await expect(page.getByText("Beneficiary Presence - Required - Beneficiary present at review")).toBeVisible();
    await expect(page.getByText(/unrestricted valid id/i).first()).toBeVisible();
    await expect(page.getByText("STATUTORY_DISCOUNT_VAT_EXEMPT").first()).toBeVisible();
    await expect(page.getByText("Supported by current review flow")).toBeVisible();
    await expect(page.getByText("Official source and ordinance text available")).toBeVisible();
    await expect(page.getByText(/Central PMS froze this active city ordinance policy authority/i)).toBeVisible();

    const approve = page.getByRole("button", { name: "Approve" });
    const reject = page.getByRole("button", { name: "Reject" });
    await expect(approve).toBeDisabled();
    await expect(page.getByText(/approval requires reviewer attestation/i)).toBeVisible();
    await expect(reject).toBeEnabled();

    await page.getByLabel(/I confirm the entitlement and evidence were reviewed/i).check();
    await expect(approve).toBeEnabled();
    await expect(reject).toBeEnabled();

    await expect(page.getByRole("textbox", { name: /jurisdiction/i })).toHaveCount(0);
    await expect(page.getByRole("combobox", { name: /policy|ordinance|jurisdiction/i })).toHaveCount(0);
    await expect(page.locator("[contenteditable=true]")).toHaveCount(0);
    await expectNoUnsafeDisclosure(page);
    await page.screenshot({ path: test.info().outputPath("active-authority.png"), fullPage: true });
  });

  test("missing frozen policy authority blocks approval without exposing raw API errors", async ({ page }) => {
    await page.goto(scenarios.missing);

    await expect(page.getByRole("heading", { name: "Frozen policy authority missing" })).toBeVisible();
    await expect(page.getByText(/did not return complete frozen local-ordinance authority/i)).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve" })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Reject" })).toBeEnabled();
    await expect(page.getByRole("combobox", { name: /policy|ordinance|jurisdiction/i })).toHaveCount(0);
    await expectNoUnsafeDisclosure(page);
    await page.screenshot({ path: test.info().outputPath("missing-authority.png"), fullPage: true });
  });

  test("malformed governing-policy readback fails closed without exception details", async ({ page }) => {
    await page.goto(scenarios.malformed);

    await expect(page.getByRole("heading", { name: "Frozen policy authority missing" })).toBeVisible();
    await expect(page.getByText(/approval is disabled/i)).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve" })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Reject" })).toBeEnabled();
    await expectNoUnsafeDisclosure(page);
    await page.screenshot({ path: test.info().outputPath("malformed-authority.png"), fullPage: true });
  });

  test("unsupported benefit effect is displayed honestly and cannot be approved", async ({ page }) => {
    await page.goto(scenarios.unsupported);

    await expect(page.getByRole("heading", { name: "Quezon City" })).toBeVisible();
    await expect(page.getByText("FULL_FEE_EXEMPTION", { exact: true })).toBeVisible();
    await expect(page.getByText("Not supported", { exact: true })).toBeVisible();
    await expect(page.getByText(/benefit effect is not supported/i).first()).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve" })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Reject" })).toBeEnabled();
    await expect(page.locator("body")).not.toContainText(/20% discount/i);
    await expectNoUnsafeDisclosure(page);
    await page.screenshot({ path: test.info().outputPath("unsupported-effect.png"), fullPage: true });
  });

  test("Paranaque verified operational unavailable-source posture is not mislabeled", async ({ page }) => {
    await page.goto(scenarios.paranaque);

    await expect(page.getByRole("heading", { name: "Para\u00f1aque City" })).toBeVisible();
    await expect(page.getByText("PH-137604000")).toBeVisible();
    await expect(page.getByText("PARANAQUE_SC_OPERATIONAL").first()).toBeVisible();
    await expect(page.getByText("Paranaque resident Senior Citizen free parking operational authority").first()).toBeVisible();
    await expect(page.getByText("VERIFIED_ACTIVE_OPERATIONAL").first()).toBeVisible();
    await expect(page.getByText("Unavailable").first()).toBeVisible();
    await expect(page.getByText("Verified active operational policy; online ordinance text unavailable")).toBeVisible();
    await expect(page.getByText("Senior Citizen").first()).toBeVisible();
    await expect(page.getByText(/resident only/i).first()).toBeVisible();
    await expect(page.getByText(/senior citizen id - required - masked statutory id reference/i)).toBeVisible();
    await expect(page.getByText(/residency evidence - required - paranaque residency evidence/i)).toBeVisible();
    await expect(page.locator("body")).not.toContainText(/PROPOSED|NO_LOCAL_RULE_FOUND|nonexistent/i);
    await expect(page.locator("body")).not.toContainText(/PWD Ordinance/i);
    await expectNoUnsafeDisclosure(page);
    await page.screenshot({ path: test.info().outputPath("paranaque-operational.png"), fullPage: true });
  });

  test("keyboard path reaches attestation and preserves readable decision controls", async ({ page }) => {
    await page.goto(scenarios.active);

    const attestation = page.getByLabel(/I confirm the entitlement and evidence were reviewed/i);
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
    await expect(page.getByText(/Jurisdiction, ordinance, policy version, benefit effect, and payable-basis snapshot are read-only/i)).toBeVisible();
    await expectNoUnsafeDisclosure(page);
    await page.screenshot({ path: test.info().outputPath("keyboard-attestation.png"), fullPage: true });
  });
});

async function expectNoUnsafeDisclosure(page: Page) {
  const body = page.locator("body");
  await expect(body).not.toContainText(/appsecret|bearer\s+[a-z0-9]|connection string|database password|npgsql|stack trace|raw response json|base64,|raw evidence image|credential-secret/i);
  await expect(page.getByRole("button", { name: /raw evidence|raw id|select policy|select ordinance|change jurisdiction/i })).toHaveCount(0);
}
