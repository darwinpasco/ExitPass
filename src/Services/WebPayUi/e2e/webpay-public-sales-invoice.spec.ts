import { expect, test } from "@playwright/test";

const canonicalText = [
  "Professional Parking Management",
  "SALES INVOICE",
  "SI NO                   SI-00000105",
  "CUSTOMER INFORMATION",
  "ADDRESS       123 Doña Juana",
  "              Townhomes, Pasig City",
  "===== NOTHING FOLLOWS ====="
].join("\r\n");

test("public customer capability displays and downloads the canonical Sales Invoice without login", async ({ page }) => {
  let requestBody: unknown;
  let authorizationHeader: string | undefined;
  await page.route("**/v1/webpay/digital-sales-invoices/presentation", async (route) => {
    requestBody = route.request().postDataJSON();
    authorizationHeader = route.request().headers().authorization;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        fiscalDocumentNumber: "SI-00000105",
        presentation: { documentTitle: "Sales Invoice" },
        canonicalText
      })
    });
  });

  await page.goto("/webpay/sales-invoice#access=opaque-capability");

  await expect(page.getByLabel("Printable Sales Invoice").locator("pre")).toHaveText(canonicalText);
  await expect(page.getByRole("button", { name: "Download Sales Invoice" })).toBeVisible();
  await expect(page.getByText(/sign in|log in/i)).toHaveCount(0);
  expect(requestBody).toEqual({ accessToken: "opaque-capability" });
  expect(authorizationHeader).toBeUndefined();
  expect(new URL(page.url()).hash).toBe("");

  const downloadPromise = page.waitForEvent("download");
  await page.getByRole("button", { name: "Download Sales Invoice" }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe("SI-00000105.pdf");
});

test("invalid customer capability shows only the safe unavailable state", async ({ page }) => {
  await page.route("**/v1/webpay/digital-sales-invoices/presentation", (route) =>
    route.fulfill({ status: 404, body: "" }));

  await page.goto("/webpay/sales-invoice#access=tampered-capability");

  await expect(page.getByText("Sales Invoice unavailable or link expired.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Download Sales Invoice" })).toHaveCount(0);
  await expect(page.getByText(/sign in|log in/i)).toHaveCount(0);
});
