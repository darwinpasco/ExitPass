import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

const stylesheet = readFileSync(join(process.cwd(), "src", "styles.css"), "utf8");

describe("Digital Sales Invoice canonical presentation", () => {
  it("uses a readable 35-character screen preview without reflowing canonical text", () => {
    expect(stylesheet).toMatch(
      /\.canonicalSalesInvoiceReceipt\s*\{[^}]*width:\s*calc\(35ch \+ 8mm\);[^}]*max-width:\s*100%;[^}]*padding:\s*4mm;[^}]*font-family:\s*"Courier New", Courier, monospace;[^}]*font-size:\s*16px;[^}]*line-height:\s*1\.3;[^}]*overflow-x:\s*auto;/s
    );
    expect(stylesheet).not.toMatch(
      /\.canonicalSalesInvoiceReceipt\s*\{[^}]*(?:width:\s*(?:min\(100%,\s*)?80mm|font-size:\s*11px)/s
    );
    expect(stylesheet).toMatch(
      /\.canonicalSalesInvoiceText\s*\{[^}]*white-space:\s*pre;[^}]*overflow-wrap:\s*normal;[^}]*word-break:\s*normal;/s
    );
    expect(stylesheet).not.toMatch(
      /\.canonicalSalesInvoiceText\s*\{[^}]*(?:white-space:\s*pre-wrap|overflow-wrap:\s*anywhere)/s
    );
  });

  it("does not use whole-page print visibility rules", () => {
    expect(stylesheet).not.toMatch(/@media print[\s\S]*canonicalSalesInvoice/);
    expect(stylesheet).not.toMatch(/body \*\s*\{\s*visibility:\s*hidden;/);
  });
});
