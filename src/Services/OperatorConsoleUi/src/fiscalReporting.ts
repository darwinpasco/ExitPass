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
    calendarYear: number;
    calendarMonth: number;
    currencyCode: string;
    fileName: string;
    generatedAt: string;
    status: string;
}
export interface EjInvoice {
    fiscalDocumentId: string;
    fiscalDocumentNumber: string;
    businessDayDate: string;
    issuedAt: string;
    printableText: string;
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
    readingUrl(site: string, kind: "x" | "z", reference: string): string;
    generateBirSalesSummary(site: string, reading: Reading): Promise<BirSalesSummary>;
    readBirSalesSummary(site: string, summaryId: string): Promise<BirSalesSummary>;
    birSalesSummaryUrl(site: string, summaryId: string, format: "json" | "csv"): string;
    generateAnnexE1(site: string, calendarYear: number, calendarMonth: number): Promise<AnnexE1Workbook>;
    readAnnexE1(site: string, workbookId: string): Promise<AnnexE1Workbook>;
    annexE1Url(site: string, workbookId: string): string;
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
        readingUrl(s, k, r) {
            return p(s, `/${k}-readings/${encodeURIComponent(r)}/download`);
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
        async readAnnexE1(s, workbookId) {
            const value = await json(p(s, `/annex-e/e1/workbooks/${encodeURIComponent(workbookId)}`));
            return annexWorkbook(rec(value.workbook));
        },
        annexE1Url(s, workbookId) {
            return p(s, `/annex-e/e1/workbooks/${encodeURIComponent(workbookId)}/download`);
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
        calendarYear: 2026,
        calendarMonth: 9,
        currencyCode: "PHP",
        fileName: "annex-e1-2026-09.xlsx",
        generatedAt: "2026-10-01T00:00:00Z",
        status: "COMMITTED",
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
        readingUrl: (s, k, x) =>
            `/v1/ops/operator-console/fiscal-reporting/sites/${s}/${k}-readings/${x}/download`,
        generateBirSalesSummary: async () => summary,
        readBirSalesSummary: async () => summary,
        birSalesSummaryUrl: (s, id, format) =>
            `/v1/ops/operator-console/fiscal-reporting/sites/${s}/bir-sales-summaries/${id}/download?format=${format}`,
        generateAnnexE1: async () => workbook,
        readAnnexE1: async () => workbook,
        annexE1Url: (s, id) =>
            `/v1/ops/operator-console/fiscal-reporting/sites/${s}/annex-e/e1/workbooks/${id}/download`,
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
        calendarYear: num(value.calendarYear),
        calendarMonth: num(value.calendarMonth),
        currencyCode: str(value.currencyCode),
        fileName: str(value.fileName),
        generatedAt: str(value.generatedAt),
        status: str(value.status),
    };
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
