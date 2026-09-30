import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

const stylesheet = readFileSync(join(process.cwd(), "src", "styles.css"), "utf8");

describe("submitted statutory request responsive layout", () => {
  it("uses one aligned label/value pair per desktop row", () => {
    expect(stylesheet).toMatch(
      /\.submittedRequestDetails\s*\{[^}]*grid-template-columns:\s*minmax\(170px, 220px\)\s+minmax\(0, 1fr\);/s
    );
  });

  it("stacks labels and values at the existing mobile breakpoint", () => {
    const mobileStart = stylesheet.indexOf("@media (max-width: 860px)");
    const nextBreakpoint = stylesheet.indexOf("@media (max-width: 480px)", mobileStart);
    const mobileRules = stylesheet.slice(mobileStart, nextBreakpoint);

    expect(mobileStart).toBeGreaterThanOrEqual(0);
    expect(nextBreakpoint).toBeGreaterThan(mobileStart);
    expect(mobileRules).toMatch(/\.submittedRequestDetails\s*\{[^}]*grid-template-columns:\s*1fr;/s);
  });
});
