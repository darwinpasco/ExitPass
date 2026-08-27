import { expect, test, type Locator } from "@playwright/test";

const navigationLabels = [
  "Overview",
  "Ticket Lookup",
  "Fiscal Status",
  "Statutory Discounts",
  "Audit / Reporting",
  "Fiscal View Audit",
  "Sales Invoice Void Audit",
  "Vendor Acknowledgments",
  "Projection Health",
  "Policy Import Review"
];

const viewports = [
  { name: "desktop", width: 1440, height: 900 },
  { name: "tablet", width: 768, height: 1024 },
  { name: "mobile", width: 390, height: 844 }
];

test.describe("Operator Console active navigation accessibility", () => {
  for (const viewport of viewports) {
    test(`${viewport.name} navigation keeps current and focused labels readable`, async ({ page }, testInfo) => {
      await page.setViewportSize(viewport);
      await page.goto("/operator-console/statutory-discounts/senior-representative-optional");

      const navigation = page.getByRole("navigation", { name: "Operator Console routes" });
      const items = navigation.getByRole("button");
      await expect(items).toHaveCount(navigationLabels.length);
      await expect(items).toHaveText(navigationLabels);

      const current = navigation.getByRole("button", { name: "Statutory Discounts", current: "page" });
      await expect(current).toBeVisible();
      await expect(navigation.locator('[aria-current="page"]')).toHaveCount(1);

      const normalActive = await measuredColors(current);
      const normalActiveContrast = contrastRatio(normalActive.foreground, normalActive.background);
      expect(normalActiveContrast).toBeGreaterThanOrEqual(4.5);
      await page.screenshot({ path: testInfo.outputPath(`${viewport.name}-active-navigation.png`), fullPage: true });

      await current.focus();
      await expect(current).toBeFocused();
      const focusedActive = await measuredColors(current);
      const focusedActiveContrast = contrastRatio(focusedActive.foreground, focusedActive.background);
      const focusIndicatorContrast = contrastRatio(focusedActive.outline, focusedActive.adjacentBackground);
      expect(focusedActiveContrast).toBeGreaterThanOrEqual(4.5);
      expect(focusIndicatorContrast).toBeGreaterThanOrEqual(3);
      await page.screenshot({ path: testInfo.outputPath(`${viewport.name}-active-navigation-focused.png`), fullPage: true });

      const ticketLookup = navigation.getByRole("button", { name: "Ticket Lookup" });
      await ticketLookup.focus();
      await expect(ticketLookup).toBeFocused();
      const focusedInactive = await measuredColors(ticketLookup);
      const focusedInactiveContrast = contrastRatio(focusedInactive.foreground, focusedInactive.background);
      expect(focusedInactiveContrast).toBeGreaterThanOrEqual(4.5);
      expect(contrastRatio(focusedInactive.outline, focusedInactive.adjacentBackground)).toBeGreaterThanOrEqual(3);

      await items.first().focus();
      for (let index = 0; index < navigationLabels.length; index += 1) {
        await expect(items.nth(index)).toBeFocused();
        if (index < navigationLabels.length - 1) await page.keyboard.press("Tab");
      }

      await ticketLookup.focus();
      await page.keyboard.press("Enter");
      await expect(ticketLookup).toHaveAttribute("aria-current", "page");
      await expect(current).not.toHaveAttribute("aria-current");
      await expect(navigation.locator('[aria-current="page"]')).toHaveCount(1);

      const fiscalStatus = navigation.getByRole("button", { name: "Fiscal Status" });
      await fiscalStatus.focus();
      await page.keyboard.press("Space");
      await expect(fiscalStatus).toHaveAttribute("aria-current", "page");
      await expect(ticketLookup).not.toHaveAttribute("aria-current");
      await expect(navigation.locator('[aria-current="page"]')).toHaveCount(1);

      const measurements = await page.evaluate(() => ({
        documentWidth: document.documentElement.scrollWidth,
        viewportWidth: document.documentElement.clientWidth
      }));
      expect(measurements.documentWidth).toBeLessThanOrEqual(measurements.viewportWidth);

      const contrastEvidence = {
        viewport,
        normalActive: { ...normalActive, ratio: normalActiveContrast },
        focusedActive: { ...focusedActive, ratio: focusedActiveContrast },
        focusedInactive: { ...focusedInactive, ratio: focusedInactiveContrast },
        focusIndicatorRatio: focusIndicatorContrast,
        layout: measurements
      };
      console.log(`${viewport.name} navigation contrast ${JSON.stringify(contrastEvidence)}`);
      await testInfo.attach(`${viewport.name}-navigation-contrast.json`, {
        body: JSON.stringify(contrastEvidence, null, 2),
        contentType: "application/json"
      });

      for (let index = 0; index < navigationLabels.length; index += 1) {
        const bounds = await items.nth(index).boundingBox();
        expect(bounds).not.toBeNull();
        expect(bounds!.x).toBeGreaterThanOrEqual(0);
        expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(viewport.width);
      }
    });
  }
});

async function measuredColors(locator: Locator) {
  return locator.evaluate((element) => {
    const styles = getComputedStyle(element);
    const adjacentStyles = getComputedStyle(element.closest(".moduleRail") ?? document.body);
    return {
      foreground: styles.color,
      background: styles.backgroundColor,
      outline: styles.outlineColor,
      adjacentBackground: adjacentStyles.backgroundColor
    };
  });
}

function contrastRatio(first: string, second: string) {
  const firstLuminance = relativeLuminance(first);
  const secondLuminance = relativeLuminance(second);
  return (Math.max(firstLuminance, secondLuminance) + 0.05) / (Math.min(firstLuminance, secondLuminance) + 0.05);
}

function relativeLuminance(color: string) {
  const channels = color.match(/[\d.]+/g)?.slice(0, 3).map(Number);
  if (!channels || channels.length !== 3) throw new Error(`Unsupported computed color: ${color}`);
  const linear = channels.map((channel) => {
    const normalized = channel / 255;
    return normalized <= 0.04045 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
}
