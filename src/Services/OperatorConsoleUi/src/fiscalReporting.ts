export const operatorFiscalReportingRoute =
    "/operator-console/fiscal-reporting";
export const compactCanonicalSalesInvoiceFixture =
    "PROFESSIONAL PARKING MANAGEMENT\r\n" +
    "PITX, Paranaque City\r\n" +
    "VAT REG TIN          123-456-789-000\r\n" +
    "MIN                          MIN-001\r\n" +
    "S/N                       POS-SN-001\r\n" +
    "BRANCH / SITE                   PITX\r\n" +
    "PARKING LOCATION       PITX Parking\r\n" +
    "\r\n" +
    "           SALES INVOICE\r\n" +
    "              ORIGINAL\r\n" +
    "SI NO                  SI-00000001\r\n" +
    "ISSUED            2026-09-10 09:00\r\n" +
    "BUSINESS DATE           2026-09-10\r\n" +
    "CASHIER             Juan Dela Cruz\r\n" +
    "\r\n" +
    "TICKET                  TICKET-0001\r\n" +
    "PLATE                      ABC-1234\r\n" +
    "ENTRY             2026-09-10 08:00\r\n" +
    "PAYMENT           2026-09-10 09:00\r\n" +
    "DURATION                    01:00:00\r\n" +
    "-----------------------------------\r\n" +
    "PARKING FEE\r\n" +
    "1 x PHP 100.00              100.00\r\n" +
    "-----------------------------------\r\n" +
    "SUBTOTAL                    112.00\r\n" +
    "DISCOUNT                      0.00\r\n" +
    "VATABLE SALES               100.00\r\n" +
    "VAT AMOUNT                   12.00\r\n" +
    "VAT EXEMPT SALES              0.00\r\n" +
    "ZERO RATED SALES              0.00\r\n" +
    "TOTAL                       112.00\r\n" +
    "\r\n" +
    "PAYMENT\r\n" +
    "TYPE                           CASH\r\n" +
    "AMOUNT DUE                  112.00\r\n" +
    "TOTAL PAID                  112.00\r\n" +
    "TENDERED                    112.00\r\n" +
    "CHANGE                        0.00\r\n" +
    "\r\n" +
    "-----------------------------------\r\n" +
    "THIS SERVES AS YOUR SALES INVOICE\r\n" +
    "PRINT DATE        2026-09-10 09:00\r\n" +
    "\r\n" +
    "CUSTOMER INFORMATION\r\n" +
    "NAME                 Juan Dela Cruz\r\n" +
    "ADDRESS           123 Sample Street\r\n" +
    "TIN              123-456-789-000\r\n" +
    "BUS. STYLE                  Retail\r\n" +
    "\r\n" +
    "POS SOFTWARE SUPPLIER / DEVELOPER\r\n" +
    "PROFESSIONAL PARKING MANAGEMENT\r\n" +
    "THANK YOU FOR CHOOSING OUR SERVICE\r\n" +
    "===== NOTHING FOLLOWS =====\r\n";
export const fiscalReportingPermissions = {
    ejRead: "fiscal-reporting.ej.read",
    ejExport: "fiscal-reporting.ej.export",
    xRead: "fiscal-reporting.x.read",
    xGenerate: "fiscal-reporting.x.generate",
    zRead: "fiscal-reporting.z.read",
    zGenerate: "fiscal-reporting.z.generate",
} as const;
export interface FiscalPeriod {
    fiscalReportingPeriodId: string;
    businessDayDate: string;
    periodStartAt: string;
    periodEndAt: string;
    currencyCode: string;
    periodSequence: number;
    status: string;
    expectedZStateVersion: number;
}
export interface Amounts {
    grossSalesAmountMinorUnits: number;
    netSalesAmountMinorUnits: number;
    vatableSalesAmountMinorUnits: number;
    vatAmountMinorUnits: number;
    vatExemptSalesAmountMinorUnits: number;
    zeroRatedSalesAmountMinorUnits: number;
    discountAmountMinorUnits: number;
    voidAmountMinorUnits: number;
    adjustmentAmountMinorUnits: number;
}
export interface Tender {
    classification: string;
    transactionCount: number;
    amountMinorUnits: number;
    currencyCode: string;
}
export interface Discount {
    classification: string;
    qualifyingDocumentCount: number;
    discountAmountMinorUnits: number;
    vatExemptionAmountMinorUnits: number;
    currencyCode: string;
}
export interface Reading {
    reportReference: string;
    reportKind: "X_READING" | "Z_READING";
    fiscalReportingPeriodId: string;
    businessDayDate: string;
    periodStartAt: string;
    periodEndAt: string;
    generatedAt: string;
    transactionCount: number;
    amounts: Amounts;
    currencyCode: string;
    periodSequence: number;
    reportStatus: string;
    tenders: Tender[];
    discounts: Discount[];
}
export interface History {
    currentPeriod?: FiscalPeriod;
    readings: Reading[];
}
export interface BirSalesSummaryAmounts extends Amounts {
    seniorCitizenDiscountAmountMinorUnits: number;
    pwdDiscountAmountMinorUnits: number;
    otherStatutoryDiscountAmountMinorUnits: number;
    vatExemptionAmountMinorUnits: number;
    refundAmountMinorUnits: number;
    returnAmountMinorUnits: number;
}
export interface BirSalesSummaryCounterSnapshot {
    previousResetCounterValue: number;
    resultingResetCounterValue: number;
    previousZCounterValue: number;
    resultingZCounterValue: number;
    previousGrandTotalAmountMinorUnits: number;
    resultingGrandTotalAmountMinorUnits: number;
}
export interface BirSalesSummary {
    birSalesSummaryReportId: string;
    governingZReadingReference: string;
    fiscalReportingPeriodId: string;
    businessDayDate: string;
    beginningSalesInvoiceReference?: string;
    endingSalesInvoiceReference?: string;
    transactionCount: number;
    amounts: BirSalesSummaryAmounts;
    counterSnapshot: BirSalesSummaryCounterSnapshot;
    currencyCode: string;
    generatedAt: string;
    reportStatus: string;
    supportReference: string;
}
export interface AnnexE1Workbook {
    workbookId: string;
    annexEReference: string;
    revision: number;
    calendarYear: number;
    calendarMonth: number;
    currencyCode: string;
    fileName: string;
    generatedAt: string;
    status: string;
}
export type StatutorySalesBookType = "e2" | "e3";
export interface StatutorySalesBookRow {
    fiscalDocumentId: string;
    transactionDate: string;
    beneficiaryFullName: string;
    statutoryIdNumber: string;
    beneficiaryTin?: string;
    salesInvoiceNumber: string;
    salesInclusiveVatMinorUnits: number;
    vatAmountMinorUnits: number;
    vatExemptSalesMinorUnits: number;
    discount5MinorUnits: number;
    discount20MinorUnits: number;
    netSalesMinorUnits: number;
}
export interface StatutorySalesBookReport {
    header: {
        serialNumber: string;
        machineIdentificationNumber: string;
        posServerCode: string;
    };
    summary: {
        type: "AnnexE2" | "AnnexE3";
        sitePosServerId: string;
        fiscalIdentityId: string;
        currencyCode: "PHP";
        calendarYear: number;
        calendarMonth: number;
        posServerCode: string;
        qualifyingTransactionCount: number;
        salesInclusiveVatMinorUnits: number;
        vatAmountMinorUnits: number;
        vatExemptSalesMinorUnits: number;
        discount5MinorUnits: number;
        discount20MinorUnits: number;
        netSalesMinorUnits: number;
        reportAsOf: string;
        warnings: string[];
        reportStatus: "PRELIMINARY" | "FINAL";
        isEmpty: boolean;
        scopedFiscalPeriodCount: number;
        closedFiscalPeriodCount: number;
        committedZPeriodCount: number;
    };
    rows: StatutorySalesBookRow[];
}
export interface MonthlyESalesRow {
    machineIdentificationNumber: string;
    calendarMonth: number;
    calendarYear: number;
    transactionCount: number;
    monthlySalesAmountMinorUnits: number;
    monthlySalesDisplay: string;
    endingSalesInvoiceReference?: string;
    vatableSalesAmountMinorUnits: number;
    vatAmountMinorUnits: number;
    vatExemptSalesAmountMinorUnits: number;
    zeroRatedSalesAmountMinorUnits: number;
    sourcePeriodCount: number;
    firstBusinessDate: string;
    lastBusinessDate: string;
}
export interface MonthlyESalesReport {
    sitePosServerId: string;
    fiscalIdentityId: string;
    calendarYear: number;
    calendarMonth: number;
    currencyCode: string;
    taxpayerTin: string;
    submissionDueDate?: string;
    submissionDueDateBasis?: string;
    submissionReadiness: string;
    submissionReadinessMessage: string;
    coverageStatus: "MONTH_TO_DATE" | "COMPLETE_MONTH";
    coverageMessage: string;
    firstCoveredBusinessDate: string;
    lastCoveredBusinessDate: string;
    coverageAsOf: string;
    rows: MonthlyESalesRow[];
}
export interface MonthlyESalesReadResult {
    status: "FINAL" | "INCOMPLETE";
    report?: MonthlyESalesReport;
    code?: string;
    message?: string;
}
export interface EjInvoice {
    fiscalDocumentId: string;
    fiscalDocumentNumber: string;
    businessDayDate: string;
    issuedAt: string;
    printableText: string;
}
export interface AuditTrailActionDefinition {
    code: string;
    displayName: string;
    definition: string;
}
export interface AuditTrailRow {
    auditReference: string;
    occurredAt: string;
    terminal?: string;
    userId?: string;
    userRole?: string;
    actionCode: string;
    actionCategory: string;
    actionDescription: string;
    salesInvoiceNumber?: string;
    beforeValue?: string;
    afterValue?: string;
    reasonRemarks?: string;
    result: string;
}
export interface AuditTrailReport {
    from: string;
    to: string;
    actionCodes: AuditTrailActionDefinition[];
    rows: AuditTrailRow[];
}
export interface AuditTrailFilters {
    actionCode?: string;
    actor?: string;
    result?: string;
    reference?: string;
}
export interface FiscalReportingSite {
    siteId: string;
    siteCode: string;
    siteName: string;
}
export interface CloseableFiscalBusinessDate {
    fiscalReportingPeriodId: string;
    fiscalBusinessDate: string;
    periodStart: string;
    periodEnd: string;
    status: string;
    transactionCount: number;
    expectedStateVersion: number;
}
export interface CloseableFiscalBusinessDatesSnapshot {
    fiscalBusinessDates: CloseableFiscalBusinessDate[];
}
export interface CloseAllFiscalBusinessDatesResult {
    succeeded: boolean;
    closedCount: number;
    birSalesSummaryPendingCount: number;
    message?: string;
    stoppedAt?: string;
}
export interface BirSalesSummaryCompletion {
    status: "COMMITTED" | "PENDING";
    summaryId?: string;
    code: string;
    retryable: boolean;
    message?: string;
}
export interface CloseFiscalBusinessDateResult {
    birSalesSummaryCompletion?: BirSalesSummaryCompletion;
}
export interface OperatorFiscalReportingClient {
    listAuthorizedSites?(): Promise<FiscalReportingSite[]>;
    readEj(
        site: string,
        start: string,
        end: string,
        search?: string,
    ): Promise<EjInvoice[]>;
    ejUrl(site: string, start: string, end: string, search?: string): string;
    readAuditTrail(site: string, start: string, end: string, filters?: AuditTrailFilters): Promise<AuditTrailReport>;
    auditTrailUrl(site: string, start: string, end: string, format: "pdf" | "csv" | "json", filters?: AuditTrailFilters): string;
    readX(site: string): Promise<History>;
    generateX(site: string): Promise<void>;
    readZ(site: string): Promise<History>;
    readCloseablePeriods(
        site: string,
    ): Promise<CloseableFiscalBusinessDatesSnapshot>;
    closeBusinessDate(
        site: string,
        period: CloseableFiscalBusinessDate,
    ): Promise<CloseFiscalBusinessDateResult>;
    closeAllBusinessDates(
        site: string,
    ): Promise<CloseAllFiscalBusinessDatesResult>;
    readingUrl(site: string, kind: "x" | "z", reference: string, format: "pdf" | "json"): string;
    generateBirSalesSummary(site: string, reading: Reading): Promise<BirSalesSummary>;
    readBirSalesSummary(site: string, summaryId: string): Promise<BirSalesSummary>;
    birSalesSummaryUrl(site: string, summaryId: string, format: "json" | "csv" | "pdf"): string;
    generateAnnexE1(site: string, calendarYear: number, calendarMonth: number): Promise<AnnexE1Workbook>;
    readCurrentAnnexE1(site: string, calendarYear: number, calendarMonth: number): Promise<AnnexE1Workbook | undefined>;
    readAnnexE1(site: string, workbookId: string): Promise<AnnexE1Workbook>;
    annexE1Url(site: string, workbookId: string, format?: "xlsx" | "pdf"): string;
    readStatutorySalesBook(site: string, type: StatutorySalesBookType, calendarYear: number, calendarMonth: number): Promise<StatutorySalesBookReport>;
    statutorySalesBookUrl(site: string, type: StatutorySalesBookType, calendarYear: number, calendarMonth: number, format: "xlsx" | "pdf"): string;
    readMonthlyESales(site: string, calendarYear: number, calendarMonth: number): Promise<MonthlyESalesReadResult>;
    monthlyESalesUrl(site: string, calendarYear: number, calendarMonth: number, format: "json" | "csv" | "pdf"): string;
}

function siteList(value: unknown): FiscalReportingSite[] {
    if (!Array.isArray(value)) throw bad();
    return value.map((item) => {
        const site = rec(item);
        return {
            siteId: str(site.siteId),
            siteCode: str(site.siteCode),
            siteName: str(site.siteName),
        };
    });
}

export function createOperatorFiscalReportingClient(
    fetchImpl: typeof fetch = fetch,
    csrfToken?: () => string | null,
): OperatorFiscalReportingClient {
    const base = "/v1/ops/operator-console/fiscal-reporting";
    const root = `${base}/sites`;
    const p = (s: string, x: string) => `${root}/${encodeURIComponent(s)}${x}`;
    async function responseJson(response: Response): Promise<unknown> {
        const contentType = response.headers.get("content-type") ?? "";
        const text = await response.text();
        if (!contentType.toLowerCase().includes("application/json")) {
            throw new Error(response.ok
                ? "The fiscal reporting service returned an invalid response."
                : "The fiscal reporting operation failed safely.");
        }
        try {
            return text ? JSON.parse(text) : {};
        } catch {
            throw new Error(response.ok
                ? "The fiscal reporting service returned an invalid response."
                : "The fiscal reporting operation failed safely.");
        }
    }
    async function json(path: string, init?: RequestInit) {
        const r = await fetchImpl(path, {
            credentials: "same-origin",
            cache: "no-store",
            ...init,
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json",
                "X-Correlation-Id": crypto.randomUUID(),
                ...init?.headers,
            },
        });
        const v = await responseJson(r);
        if (!r.ok)
            throw new Error(
                safeErrorMessage(v) || "Fiscal reporting failed safely.",
            );
        return rec(v);
    }
    const mutation = (body: unknown): RequestInit => {
        const token = csrfToken?.();
        if (!token)
            throw new Error(
                "A current anti-forgery token is required for fiscal report generation.",
            );
        return {
            method: "POST",
            headers: { "X-CSRF-Token": token },
            body: JSON.stringify(body),
        };
    };
    return {
        async listAuthorizedSites() {
            const r = await fetchImpl(`${base}/authorized-sites`, {
                credentials: "same-origin",
                cache: "no-store",
                headers: {
                    Accept: "application/json",
                    "X-Correlation-Id": crypto.randomUUID(),
                },
            });
            const v = await responseJson(r);
            if (!r.ok)
                throw new Error(
                    safeErrorMessage(v) ||
                        "Fiscal reporting Sites are unavailable.",
                );
            return siteList(v);
        },
        async readEj(s, a, b, q) {
            const x = new URLSearchParams({ periodStart: a, periodEnd: b });
            if (q?.trim()) x.set("search", q.trim());
            const v = await json(p(s, `/electronic-journal?${x}`));
            if (!Array.isArray(v.invoices)) throw bad();
            return v.invoices.map((i) => {
                const o = rec(i);
                return {
                    fiscalDocumentId: str(o.fiscalDocumentId),
                    fiscalDocumentNumber: str(o.fiscalDocumentNumber),
                    businessDayDate: str(o.businessDayDate),
                    issuedAt: str(o.issuedAt),
                    printableText: str(o.printableText),
                };
            });
        },
        ejUrl(s, a, b, q) {
            const x = new URLSearchParams({ periodStart: a, periodEnd: b });
            if (q?.trim()) x.set("search", q.trim());
            return p(s, `/electronic-journal/download?${x}`);
        },
        async readAuditTrail(s, a, b, filters) {
            const value = await json(p(s, `/audit-trail?${auditTrailQuery(a, b, filters)}`));
            return auditTrailReport(rec(value.report));
        },
        auditTrailUrl(s, a, b, format, filters) {
            const query = auditTrailQuery(a, b, filters);
            query.set("format", format);
            return p(s, `/audit-trail/download?${query}`);
        },
        async readX(s) {
            return hist(await json(p(s, "/x-readings")), "X_READING");
        },
        async generateX(s) {
            await json(
                p(s, "/x-readings"),
                mutation({ operationKey: crypto.randomUUID() }),
            );
        },
        async readZ(s) {
            return hist(await json(p(s, "/z-readings")), "Z_READING");
        },
        async readCloseablePeriods(s) {
            return closeable(await json(p(s, "/z-readings/closeable-periods")));
        },
        async closeBusinessDate(s, period) {
            const value = await json(
                p(
                    s,
                    `/z-readings/closeable-periods/${encodeURIComponent(period.fiscalReportingPeriodId)}/close`,
                ),
                mutation({
                    operationKey: crypto.randomUUID(),
                    expectedStateVersion: period.expectedStateVersion,
                    confirmClose: true,
                }),
            );
            return {
                birSalesSummaryCompletion: birCompletion(
                    value.birSalesSummaryCompletion,
                ),
            };
        },
        async closeAllBusinessDates(s) {
            const value = await json(
                p(s, "/z-readings/closeable-periods/close-all"),
                mutation({
                    operationKey: crypto.randomUUID(),
                    confirmClose: true,
                }),
            );
            return {
                succeeded: value.succeeded === true,
                closedCount: num(value.closedCount),
                birSalesSummaryPendingCount:
                    typeof value.birSalesSummaryPendingCount === "number"
                        ? num(value.birSalesSummaryPendingCount)
                        : 0,
                message:
                    typeof value.message === "string"
                        ? value.message
                        : undefined,
                stoppedAt:
                    typeof value.stoppedAt === "string"
                        ? value.stoppedAt
                        : undefined,
            };
        },
        readingUrl(s, k, r, format) {
            return p(s, `/${k}-readings/${encodeURIComponent(r)}/download?format=${format}`);
        },
        async generateBirSalesSummary(s, reading) {
            const value = await json(
                p(s, "/bir-sales-summaries"),
                mutation({
                    operationKey: crypto.randomUUID(),
                    fiscalReportingPeriodId: reading.fiscalReportingPeriodId,
                    governingZReadingReference: reading.reportReference,
                }),
            );
            return birSummary(rec(value.summary));
        },
        async readBirSalesSummary(s, summaryId) {
            const value = await json(p(s, `/bir-sales-summaries/${encodeURIComponent(summaryId)}`));
            return birSummary(rec(value.summary));
        },
        birSalesSummaryUrl(s, summaryId, format) {
            return p(s, `/bir-sales-summaries/${encodeURIComponent(summaryId)}/download?format=${format}`);
        },
        async generateAnnexE1(s, calendarYear, calendarMonth) {
            const value = await json(
                p(s, "/annex-e/e1/workbooks"),
                mutation({ operationKey: crypto.randomUUID(), calendarYear, calendarMonth }),
            );
            return annexWorkbook(rec(value.workbook));
        },
        async readCurrentAnnexE1(s, calendarYear, calendarMonth) {
            const query = new URLSearchParams({
                calendarYear: String(calendarYear),
                calendarMonth: String(calendarMonth),
            });
            const response = await fetchImpl(
                p(s, `/annex-e/e1/workbooks/current?${query}`),
                {
                    credentials: "same-origin",
                    cache: "no-store",
                    headers: {
                        Accept: "application/json",
                        "X-Correlation-Id": crypto.randomUUID(),
                    },
                },
            );
            const value = await responseJson(response);
            if (response.status === 404) return undefined;
            if (!response.ok)
                throw new Error(
                    safeErrorMessage(value) || "Annex E-1 current workbook could not be loaded.",
                );
            return annexWorkbook(rec(rec(value).workbook));
        },
        async readAnnexE1(s, workbookId) {
            const value = await json(p(s, `/annex-e/e1/workbooks/${encodeURIComponent(workbookId)}`));
            return annexWorkbook(rec(value.workbook));
        },
        annexE1Url(s, workbookId, format = "xlsx") {
            return p(s, `/annex-e/e1/workbooks/${encodeURIComponent(workbookId)}/download?format=${format}`);
        },
        async readStatutorySalesBook(s, type, calendarYear, calendarMonth) {
            const query = new URLSearchParams({ calendarYear: String(calendarYear), calendarMonth: String(calendarMonth) });
            const value = await json(p(s, `/annex-e/${type}/statutory-sales-book?${query}`));
            return statutorySalesBook(rec(value), type, calendarYear, calendarMonth);
        },
        statutorySalesBookUrl(s, type, calendarYear, calendarMonth, format) {
            const query = new URLSearchParams({
                calendarYear: String(calendarYear),
                calendarMonth: String(calendarMonth),
                format,
            });
            return p(s, `/annex-e/${type}/statutory-sales-book/download?${query}`);
        },
        async readMonthlyESales(s, calendarYear, calendarMonth) {
            const query = new URLSearchParams({ calendarYear: String(calendarYear), calendarMonth: String(calendarMonth) });
            const response = await fetchImpl(p(s, `/monthly-esales?${query}`), {
                credentials: "same-origin",
                cache: "no-store",
                headers: { Accept: "application/json", "X-Correlation-Id": crypto.randomUUID() },
            });
            const value = await responseJson(response);
            const envelope = rec(value);
            if (response.ok) return { status: "FINAL", report: monthlyESalesReport(rec(envelope.report)) };
            const code = typeof envelope.code === "string" ? envelope.code : undefined;
            if (code === "monthly_esales_month_not_complete" || code === "monthly_esales_source_missing" || code === "monthly_esales_sources_incomplete") {
                return { status: "INCOMPLETE", code, message: safeErrorMessage(value) };
            }
            throw new Error(safeErrorMessage(value) || "Monthly eSales could not be loaded.");
        },
        monthlyESalesUrl(s, calendarYear, calendarMonth, format) {
            const query = new URLSearchParams({ calendarYear: String(calendarYear), calendarMonth: String(calendarMonth), format });
            return p(s, `/monthly-esales/download?${query}`);
        },
    };
}
export function createOperatorFiscalReportingFixture(): OperatorFiscalReportingClient {
    const period: FiscalPeriod = {
        fiscalReportingPeriodId: "91000000-0000-4000-8000-000000000001",
        businessDayDate: "2026-09-10",
        periodStartAt: "2026-09-09T16:00:00Z",
        periodEndAt: "2026-09-10T16:00:00Z",
        currencyCode: "PHP",
        periodSequence: 91,
        status: "OPEN",
        expectedZStateVersion: 14,
    };
    const amounts: Amounts = {
        grossSalesAmountMinorUnits: 11200,
        netSalesAmountMinorUnits: 11200,
        vatableSalesAmountMinorUnits: 10000,
        vatAmountMinorUnits: 1200,
        vatExemptSalesAmountMinorUnits: 0,
        zeroRatedSalesAmountMinorUnits: 0,
        discountAmountMinorUnits: 0,
        voidAmountMinorUnits: 0,
        adjustmentAmountMinorUnits: 0,
    };
    const r = (k: "X_READING" | "Z_READING"): Reading => ({
        reportReference: `${k[0]}-20260910-001`,
        reportKind: k,
        fiscalReportingPeriodId: period.fiscalReportingPeriodId,
        businessDayDate: period.businessDayDate,
        periodStartAt: period.periodStartAt,
        periodEndAt: period.periodEndAt,
        generatedAt: "2026-09-10T08:15:00Z",
        transactionCount: 1,
        amounts,
        currencyCode: "PHP",
        periodSequence: 91,
        reportStatus: "COMMITTED",
        tenders: [
            {
                classification: "cash",
                transactionCount: 1,
                amountMinorUnits: 11200,
                currencyCode: "PHP",
            },
        ],
        discounts: [],
    });
    const ej: EjInvoice = {
        fiscalDocumentId: "92000000-0000-4000-8000-000000000001",
        fiscalDocumentNumber: "SI-00000001",
        businessDayDate: "2026-09-10",
        issuedAt: "2026-09-10T01:00:00Z",
        printableText: compactCanonicalSalesInvoiceFixture,
    };
    const summary: BirSalesSummary = {
        birSalesSummaryReportId: "93000000-0000-4000-8000-000000000001",
        governingZReadingReference: "Z-20260910-001",
        fiscalReportingPeriodId: period.fiscalReportingPeriodId,
        businessDayDate: period.businessDayDate,
        beginningSalesInvoiceReference: "SI-00000001",
        endingSalesInvoiceReference: "SI-00000001",
        transactionCount: 1,
        amounts: {
            ...amounts,
            seniorCitizenDiscountAmountMinorUnits: 0,
            pwdDiscountAmountMinorUnits: 0,
            otherStatutoryDiscountAmountMinorUnits: 0,
            vatExemptionAmountMinorUnits: 0,
            refundAmountMinorUnits: 0,
            returnAmountMinorUnits: 0,
        },
        counterSnapshot: {
            previousResetCounterValue: 0,
            resultingResetCounterValue: 0,
            previousZCounterValue: 90,
            resultingZCounterValue: 91,
            previousGrandTotalAmountMinorUnits: 100000,
            resultingGrandTotalAmountMinorUnits: 111200,
        },
        currencyCode: "PHP",
        generatedAt: "2026-09-10T08:16:00Z",
        reportStatus: "COMMITTED",
        supportReference: "BIRSS-93000000",
    };
    const workbook: AnnexE1Workbook = {
        workbookId: "94000000-0000-4000-8000-000000000001",
        annexEReference: "ANNEX-E1-202609-001",
        revision: 1,
        calendarYear: 2026,
        calendarMonth: 9,
        currencyCode: "PHP",
        fileName: "annex-e1-2026-09.xlsx",
        generatedAt: "2026-10-01T00:00:00Z",
        status: "COMMITTED",
    };
    const statutorySalesBook = (type: StatutorySalesBookType): StatutorySalesBookReport => ({
        header: {
            serialNumber: "EP-PITX-L3-POS-0001",
            machineIdentificationNumber: "MIN-PITX-L3-01",
            posServerCode: "PITX-L3-POS-01",
        },
        summary: {
            type: type === "e2" ? "AnnexE2" : "AnnexE3",
            sitePosServerId: "3a138565-1b88-55f8-c83d-5380db6edccc",
            fiscalIdentityId: "ad02beb5-b8cd-4545-9ff2-5586782c686a",
            currencyCode: "PHP",
            calendarYear: 2026,
            calendarMonth: 9,
            posServerCode: "PITX-L3-POS-01",
            qualifyingTransactionCount: 0,
            salesInclusiveVatMinorUnits: 0,
            vatAmountMinorUnits: 0,
            vatExemptSalesMinorUnits: 0,
            discount5MinorUnits: 0,
            discount20MinorUnits: 0,
            netSalesMinorUnits: 0,
            reportAsOf: "2026-10-01T00:00:00Z",
            warnings: [],
            reportStatus: "PRELIMINARY",
            isEmpty: true,
            scopedFiscalPeriodCount: 30,
            closedFiscalPeriodCount: 29,
            committedZPeriodCount: 29,
        },
        rows: [],
    });
    const monthlyESales: MonthlyESalesReport = {
        sitePosServerId: "3a138565-1b88-55f8-c83d-5380db6edccc",
        fiscalIdentityId: "ad02beb5-b8cd-4545-9ff2-5586782c686a",
        calendarYear: 2026,
        calendarMonth: 9,
        currencyCode: "PHP",
        taxpayerTin: "999-999-999-000",
        submissionDueDate: "2026-10-10",
        submissionDueDateBasis: "ODD_TIN",
        submissionReadiness: "TECHNICAL_UAT",
        submissionReadinessMessage: "UAT fiscal identifier - not for BIR submission.",
        coverageStatus: "COMPLETE_MONTH",
        coverageMessage: "Complete calendar month from 2026-09-10 through 2026-09-10.",
        firstCoveredBusinessDate: "2026-09-10",
        lastCoveredBusinessDate: "2026-09-10",
        coverageAsOf: "2026-09-11T00:00:00Z",
        rows: [{
            machineIdentificationNumber: "MIN-TEST-PITX-L3-001",
            calendarMonth: 9,
            calendarYear: 2026,
            transactionCount: 1,
            monthlySalesAmountMinorUnits: 11200,
            monthlySalesDisplay: "112.00",
            endingSalesInvoiceReference: "SI-00000001",
            vatableSalesAmountMinorUnits: 10000,
            vatAmountMinorUnits: 1200,
            vatExemptSalesAmountMinorUnits: 0,
            zeroRatedSalesAmountMinorUnits: 0,
            sourcePeriodCount: 1,
            firstBusinessDate: "2026-09-10",
            lastBusinessDate: "2026-09-10",
        }],
    };
    return {
        listAuthorizedSites: async () => [
            {
                siteId: "SITE-PITX",
                siteCode: "PITX-TEST",
                siteName: "PITX Test Site",
            },
            {
                siteId: "10000000-0000-4000-8000-000000000001",
                siteCode: "PITX-L3",
                siteName: "PITX Level 3",
            },
        ],
        readEj: async () => [ej],
        ejUrl: (s) =>
            `/v1/ops/operator-console/fiscal-reporting/sites/${s}/electronic-journal/download`,
        readAuditTrail: async () => ({
            from: "2026-09-10T00:00:00Z",
            to: "2026-09-11T00:00:00Z",
            actionCodes: [{ code: "fiscal_document_committed", displayName: "SALES TRANSACTION", definition: "A Sales Invoice was committed." }],
            rows: [{
                auditReference: "EJ-0001",
                occurredAt: "2026-09-10T01:00:00Z",
                terminal: "PITX-L3-WEBPAY-01",
                userId: "pos-server-fiscal-document-runtime",
                userRole: "SYSTEM",
                actionCode: "fiscal_document_committed",
                actionCategory: "SALES TRANSACTION",
                actionDescription: "Plate:ABC1234\nPayment:CASH",
                salesInvoiceNumber: "SI-00000001",
                beforeValue: "P 0.00",
                afterValue: "P 112.00",
                reasonRemarks: "-",
                result: "SUCCESS",
            }],
        }),
        auditTrailUrl: (s, a, b, format) =>
            `/v1/ops/operator-console/fiscal-reporting/sites/${s}/audit-trail/download?from=${encodeURIComponent(a)}&to=${encodeURIComponent(b)}&format=${format}`,
        readX: async () => ({
            currentPeriod: period,
            readings: [r("X_READING")],
        }),
        generateX: async () => {},
        readZ: async () => ({
            currentPeriod: period,
            readings: [r("Z_READING")],
        }),
        readCloseablePeriods: async () => ({
            fiscalBusinessDates: [
                {
                    fiscalReportingPeriodId: period.fiscalReportingPeriodId,
                    fiscalBusinessDate: period.businessDayDate,
                    periodStart: period.periodStartAt,
                    periodEnd: period.periodEndAt,
                    status: "CLOSEABLE",
                    transactionCount: 1,
                    expectedStateVersion: period.expectedZStateVersion,
                },
            ],
        }),
        closeBusinessDate: async () => ({
            birSalesSummaryCompletion: {
                status: "COMMITTED",
                summaryId: "95000000-0000-4000-8000-000000000001",
                code: "bir_sales_summary_committed",
                retryable: false,
            },
        }),
        closeAllBusinessDates: async () => ({
            succeeded: true,
            closedCount: 1,
            birSalesSummaryPendingCount: 0,
        }),
        readingUrl: (s, k, x, format) =>
            `/v1/ops/operator-console/fiscal-reporting/sites/${s}/${k}-readings/${x}/download?format=${format}`,
        generateBirSalesSummary: async () => summary,
        readBirSalesSummary: async () => summary,
        birSalesSummaryUrl: (s, id, format) =>
            `/v1/ops/operator-console/fiscal-reporting/sites/${s}/bir-sales-summaries/${id}/download?format=${format}`,
        generateAnnexE1: async () => workbook,
        readCurrentAnnexE1: async () => workbook,
        readAnnexE1: async () => workbook,
        annexE1Url: (s, id, format = "xlsx") =>
            `/v1/ops/operator-console/fiscal-reporting/sites/${s}/annex-e/e1/workbooks/${id}/download?format=${format}`,
        readStatutorySalesBook: async (_s, type) => statutorySalesBook(type),
        statutorySalesBookUrl: (s, type, year, month, format) =>
            `/v1/ops/operator-console/fiscal-reporting/sites/${s}/annex-e/${type}/statutory-sales-book/download?calendarYear=${year}&calendarMonth=${month}&format=${format}`,
        readMonthlyESales: async () => ({ status: "FINAL", report: monthlyESales }),
        monthlyESalesUrl: (s, year, month, format) =>
            `/v1/ops/operator-console/fiscal-reporting/sites/${s}/monthly-esales/download?calendarYear=${year}&calendarMonth=${month}&format=${format}`,
    };
}

function auditTrailQuery(start: string, end: string, filters?: AuditTrailFilters) {
    const query = new URLSearchParams({ from: start, to: end });
    if (filters?.actionCode?.trim()) query.set("actionCode", filters.actionCode.trim());
    if (filters?.actor?.trim()) query.set("actor", filters.actor.trim());
    if (filters?.result?.trim()) query.set("result", filters.result.trim());
    if (filters?.reference?.trim()) query.set("reference", filters.reference.trim());
    return query;
}

function auditTrailReport(value: Record<string, unknown>): AuditTrailReport {
    if (!Array.isArray(value.actionCodes) || !Array.isArray(value.rows)) throw bad();
    return {
        from: str(value.from),
        to: str(value.to),
        actionCodes: value.actionCodes.map((item) => {
            const action = rec(item);
            return { code: str(action.code), displayName: str(action.displayName), definition: str(action.definition) };
        }),
        rows: value.rows.map((item) => {
            const row = rec(item);
            return {
                auditReference: str(row.auditReference),
                occurredAt: str(row.occurredAt),
                terminal: optionalString(row.terminal),
                userId: optionalString(row.userId),
                userRole: optionalString(row.userRole),
                actionCode: str(row.actionCode),
                actionCategory: str(row.actionCategory),
                actionDescription: str(row.actionDescription),
                salesInvoiceNumber: optionalString(row.salesInvoiceNumber),
                beforeValue: optionalString(row.beforeValue),
                afterValue: optionalString(row.afterValue),
                reasonRemarks: optionalString(row.reasonRemarks),
                result: str(row.result),
            };
        }),
    };
}

function birSummary(value: Record<string, unknown>): BirSalesSummary {
    const amounts = rec(value.amounts);
    const counters = rec(value.counterSnapshot);
    return {
        birSalesSummaryReportId: str(value.birSalesSummaryReportId),
        governingZReadingReference: str(value.governingZReadingReference),
        fiscalReportingPeriodId: str(value.fiscalReportingPeriodId),
        businessDayDate: str(value.businessDayDate),
        beginningSalesInvoiceReference: optionalString(value.beginningSalesInvoiceReference),
        endingSalesInvoiceReference: optionalString(value.endingSalesInvoiceReference),
        transactionCount: num(value.transactionCount),
        amounts: {
            grossSalesAmountMinorUnits: num(amounts.grossSalesAmountMinorUnits),
            netSalesAmountMinorUnits: num(amounts.netSalesAmountMinorUnits),
            vatableSalesAmountMinorUnits: num(amounts.vatableSalesAmountMinorUnits),
            vatAmountMinorUnits: num(amounts.vatAmountMinorUnits),
            vatExemptSalesAmountMinorUnits: num(amounts.vatExemptSalesAmountMinorUnits),
            zeroRatedSalesAmountMinorUnits: num(amounts.zeroRatedSalesAmountMinorUnits),
            discountAmountMinorUnits: num(amounts.discountAmountMinorUnits),
            seniorCitizenDiscountAmountMinorUnits: num(amounts.seniorCitizenDiscountAmountMinorUnits),
            pwdDiscountAmountMinorUnits: num(amounts.pwdDiscountAmountMinorUnits),
            otherStatutoryDiscountAmountMinorUnits: num(amounts.otherStatutoryDiscountAmountMinorUnits),
            vatExemptionAmountMinorUnits: num(amounts.vatExemptionAmountMinorUnits),
            voidAmountMinorUnits: num(amounts.voidAmountMinorUnits),
            refundAmountMinorUnits: num(amounts.refundAmountMinorUnits),
            returnAmountMinorUnits: num(amounts.returnAmountMinorUnits),
            adjustmentAmountMinorUnits: num(amounts.adjustmentAmountMinorUnits),
        },
        counterSnapshot: {
            previousResetCounterValue: num(counters.previousResetCounterValue),
            resultingResetCounterValue: num(counters.resultingResetCounterValue),
            previousZCounterValue: num(counters.previousZCounterValue),
            resultingZCounterValue: num(counters.resultingZCounterValue),
            previousGrandTotalAmountMinorUnits: num(counters.previousGrandTotalAmountMinorUnits),
            resultingGrandTotalAmountMinorUnits: num(counters.resultingGrandTotalAmountMinorUnits),
        },
        currencyCode: str(value.currencyCode),
        generatedAt: str(value.generatedAt),
        reportStatus: str(value.reportStatus),
        supportReference: str(value.supportReference),
    };
}

function annexWorkbook(value: Record<string, unknown>): AnnexE1Workbook {
    return {
        workbookId: str(value.workbookId),
        annexEReference: str(value.annexEReference),
        revision: num(value.revision),
        calendarYear: num(value.calendarYear),
        calendarMonth: num(value.calendarMonth),
        currencyCode: str(value.currencyCode),
        fileName: str(value.fileName),
        generatedAt: str(value.generatedAt),
        status: str(value.status),
    };
}
function statutorySalesBook(
    value: Record<string, unknown>,
    requestedType: StatutorySalesBookType,
    requestedYear: number,
    requestedMonth: number,
): StatutorySalesBookReport {
    if (value.succeeded !== true || value.code !== "statutory_sales_book_read") throw bad();
    const report = rec(value.report);
    const header = rec(report.header);
    const summary = rec(report.summary);
    const expectedType = requestedType === "e2" ? "AnnexE2" : "AnnexE3";
    if (summary.type !== expectedType || summary.currencyCode !== "PHP" || !Array.isArray(report.rows) ||
        !Array.isArray(summary.warnings) || !summary.warnings.every((warning) => typeof warning === "string")) throw bad();
    const reportStatus = str(summary.reportStatus);
    if (reportStatus !== "PRELIMINARY" && reportStatus !== "FINAL") throw bad();
    const rows = report.rows.map((item): StatutorySalesBookRow => {
        const row = rec(item);
        return {
            fiscalDocumentId: uuid(row.fiscalDocumentId),
            transactionDate: str(row.transactionDate),
            beneficiaryFullName: str(row.beneficiaryFullName),
            statutoryIdNumber: str(row.statutoryIdNumber),
            beneficiaryTin: optionalString(row.beneficiaryTin),
            salesInvoiceNumber: str(row.salesInvoiceNumber),
            salesInclusiveVatMinorUnits: nonNegative(row.salesInclusiveVatMinorUnits),
            vatAmountMinorUnits: nonNegative(row.vatAmountMinorUnits),
            vatExemptSalesMinorUnits: nonNegative(row.vatExemptSalesMinorUnits),
            discount5MinorUnits: nonNegative(row.discount5MinorUnits),
            discount20MinorUnits: nonNegative(row.discount20MinorUnits),
            netSalesMinorUnits: nonNegative(row.netSalesMinorUnits),
        };
    });
    const result: StatutorySalesBookReport = {
        header: {
            serialNumber: str(header.serialNumber),
            machineIdentificationNumber: str(header.machineIdentificationNumber),
            posServerCode: str(header.posServerCode),
        },
        summary: {
            type: expectedType,
            sitePosServerId: uuid(summary.sitePosServerId),
            fiscalIdentityId: uuid(summary.fiscalIdentityId),
            currencyCode: "PHP",
            calendarYear: nonNegative(summary.calendarYear),
            calendarMonth: nonNegative(summary.calendarMonth),
            posServerCode: str(summary.posServerCode),
            qualifyingTransactionCount: nonNegative(summary.qualifyingTransactionCount),
            salesInclusiveVatMinorUnits: nonNegative(summary.salesInclusiveVatMinorUnits),
            vatAmountMinorUnits: nonNegative(summary.vatAmountMinorUnits),
            vatExemptSalesMinorUnits: nonNegative(summary.vatExemptSalesMinorUnits),
            discount5MinorUnits: nonNegative(summary.discount5MinorUnits),
            discount20MinorUnits: nonNegative(summary.discount20MinorUnits),
            netSalesMinorUnits: nonNegative(summary.netSalesMinorUnits),
            reportAsOf: str(summary.reportAsOf),
            warnings: summary.warnings as string[],
            reportStatus,
            isEmpty: summary.isEmpty === true,
            scopedFiscalPeriodCount: nonNegative(summary.scopedFiscalPeriodCount),
            closedFiscalPeriodCount: nonNegative(summary.closedFiscalPeriodCount),
            committedZPeriodCount: nonNegative(summary.committedZPeriodCount),
        },
        rows,
    };
    if (result.header.posServerCode !== result.summary.posServerCode ||
        result.summary.calendarYear !== requestedYear ||
        result.summary.calendarMonth !== requestedMonth ||
        result.summary.qualifyingTransactionCount !== rows.length ||
        result.summary.isEmpty !== (rows.length === 0) ||
        result.summary.calendarMonth < 1 || result.summary.calendarMonth > 12) throw bad();
    return result;
}
function monthlyESalesReport(value: Record<string, unknown>): MonthlyESalesReport {
    if (!Array.isArray(value.rows)) throw bad();
    return {
        sitePosServerId: str(value.sitePosServerId),
        fiscalIdentityId: str(value.fiscalIdentityId),
        calendarYear: num(value.calendarYear),
        calendarMonth: num(value.calendarMonth),
        currencyCode: str(value.currencyCode),
        taxpayerTin: str(value.taxpayerTin),
        submissionDueDate: optionalString(value.submissionDueDate),
        submissionDueDateBasis: optionalString(value.submissionDueDateBasis),
        submissionReadiness: str(value.submissionReadiness),
        submissionReadinessMessage: str(value.submissionReadinessMessage),
        coverageStatus: monthlyCoverageStatus(value.coverageStatus),
        coverageMessage: str(value.coverageMessage),
        firstCoveredBusinessDate: str(value.firstCoveredBusinessDate),
        lastCoveredBusinessDate: str(value.lastCoveredBusinessDate),
        coverageAsOf: str(value.coverageAsOf),
        rows: value.rows.map((item) => {
            const row = rec(item);
            return {
                machineIdentificationNumber: str(row.machineIdentificationNumber),
                calendarMonth: num(row.calendarMonth),
                calendarYear: num(row.calendarYear),
                transactionCount: num(row.transactionCount),
                monthlySalesAmountMinorUnits: num(row.monthlySalesAmountMinorUnits),
                monthlySalesDisplay: str(row.monthlySalesDisplay),
                endingSalesInvoiceReference: optionalString(row.endingSalesInvoiceReference),
                vatableSalesAmountMinorUnits: num(row.vatableSalesAmountMinorUnits),
                vatAmountMinorUnits: num(row.vatAmountMinorUnits),
                vatExemptSalesAmountMinorUnits: num(row.vatExemptSalesAmountMinorUnits),
                zeroRatedSalesAmountMinorUnits: num(row.zeroRatedSalesAmountMinorUnits),
                sourcePeriodCount: num(row.sourcePeriodCount),
                firstBusinessDate: str(row.firstBusinessDate),
                lastBusinessDate: str(row.lastBusinessDate),
            };
        }),
    };
}
function monthlyCoverageStatus(value: unknown): "MONTH_TO_DATE" | "COMPLETE_MONTH" {
    const status = str(value);
    if (status !== "MONTH_TO_DATE" && status !== "COMPLETE_MONTH") throw bad();
    return status;
}
function hist(
    v: Record<string, unknown>,
    kind: Reading["reportKind"],
): History {
    if (!Array.isArray(v.readings)) throw bad();
    return {
        currentPeriod: v.currentPeriod ? per(rec(v.currentPeriod)) : undefined,
        readings: v.readings.map((x) => read(rec(x), kind)),
    };
}
function birCompletion(value: unknown): BirSalesSummaryCompletion | undefined {
    if (value === undefined || value === null) return undefined;
    const completion = rec(value);
    const status = str(completion.status);
    if (status !== "COMMITTED" && status !== "PENDING") throw bad();
    return {
        status,
        summaryId:
            typeof completion.summaryId === "string"
                ? completion.summaryId
                : undefined,
        code: str(completion.code),
        retryable: completion.retryable === true,
        message:
            typeof completion.message === "string"
                ? completion.message
                : undefined,
    };
}

function closeable(
    value: Record<string, unknown>,
): CloseableFiscalBusinessDatesSnapshot {
    if (!Array.isArray(value.fiscalBusinessDates)) throw bad();
    return {
        fiscalBusinessDates: value.fiscalBusinessDates.map((item) => {
            const period = rec(item);
            return {
                fiscalReportingPeriodId: str(period.fiscalReportingPeriodId),
                fiscalBusinessDate: str(period.fiscalBusinessDate),
                periodStart: str(period.periodStart),
                periodEnd: str(period.periodEnd),
                status: str(period.status),
                transactionCount: num(period.transactionCount),
                expectedStateVersion: num(period.expectedStateVersion),
            };
        }),
    };
}
function per(o: Record<string, unknown>): FiscalPeriod {
    return {
        fiscalReportingPeriodId: str(o.fiscalReportingPeriodId),
        businessDayDate: str(o.businessDayDate),
        periodStartAt: str(o.periodStartAt),
        periodEndAt: str(o.periodEndAt),
        currencyCode: str(o.currencyCode),
        periodSequence: num(o.periodSequence),
        status: str(o.status),
        expectedZStateVersion: num(o.expectedZStateVersion),
    };
}
function read(o: Record<string, unknown>, k: Reading["reportKind"]): Reading {
    if (
        o.reportKind !== k ||
        !Array.isArray(o.tenders) ||
        !Array.isArray(o.discounts)
    )
        throw bad();
    const a = rec(o.amounts);
    return {
        reportReference: str(o.reportReference),
        reportKind: k,
        fiscalReportingPeriodId: str(o.fiscalReportingPeriodId),
        businessDayDate: str(o.businessDayDate),
        periodStartAt: str(o.periodStartAt),
        periodEndAt: str(o.periodEndAt),
        generatedAt: str(o.generatedAt),
        transactionCount: num(o.transactionCount),
        amounts: {
            grossSalesAmountMinorUnits: num(a.grossSalesAmountMinorUnits),
            netSalesAmountMinorUnits: num(a.netSalesAmountMinorUnits),
            vatableSalesAmountMinorUnits: num(a.vatableSalesAmountMinorUnits),
            vatAmountMinorUnits: num(a.vatAmountMinorUnits),
            vatExemptSalesAmountMinorUnits: num(
                a.vatExemptSalesAmountMinorUnits,
            ),
            zeroRatedSalesAmountMinorUnits: num(
                a.zeroRatedSalesAmountMinorUnits,
            ),
            discountAmountMinorUnits: num(a.discountAmountMinorUnits),
            voidAmountMinorUnits: num(a.voidAmountMinorUnits),
            adjustmentAmountMinorUnits: num(a.adjustmentAmountMinorUnits),
        },
        currencyCode: str(o.currencyCode),
        periodSequence: num(o.periodSequence),
        reportStatus: str(o.reportStatus),
        tenders: o.tenders.map((v) => {
            const t = rec(v);
            return {
                classification: str(t.classification),
                transactionCount: num(t.transactionCount),
                amountMinorUnits: num(t.amountMinorUnits),
                currencyCode: str(t.currencyCode),
            };
        }),
        discounts: o.discounts.map((v) => {
            const d = rec(v);
            return {
                classification: str(d.classification),
                qualifyingDocumentCount: num(d.qualifyingDocumentCount),
                discountAmountMinorUnits: num(d.discountAmountMinorUnits),
                vatExemptionAmountMinorUnits: num(
                    d.vatExemptionAmountMinorUnits,
                ),
                currencyCode: str(d.currencyCode),
            };
        }),
    };
}
function rec(v: unknown): Record<string, unknown> {
    if (typeof v !== "object" || v === null || Array.isArray(v)) throw bad();
    return v as Record<string, unknown>;
}
function str(v: unknown) {
    if (typeof v !== "string" || !v) throw bad();
    return v;
}
function optionalString(v: unknown) {
    return typeof v === "string" && v ? v : undefined;
}
function num(v: unknown) {
    if (typeof v !== "number" || !Number.isSafeInteger(v)) throw bad();
    return v;
}
function nonNegative(v: unknown) {
    const value = num(v);
    if (value < 0) throw bad();
    return value;
}
function uuid(v: unknown) {
    const value = str(v);
    if (!/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value)) throw bad();
    return value;
}
function bad() {
    return new Error(
        "The authoritative fiscal reporting response was malformed.",
    );
}

function safeErrorMessage(value: unknown) {
    if (typeof value !== "object" || value === null || Array.isArray(value)) return undefined;
    const message = (value as Record<string, unknown>).message;
    return typeof message === "string" && message.trim() ? message : undefined;
}
