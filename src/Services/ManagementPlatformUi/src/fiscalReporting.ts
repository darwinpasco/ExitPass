import type { CentralPmsApiClient } from "./types";

export const fiscalReportingRoute = "/management-platform/fiscal-reporting";
export const fiscalReportingPermissions = {
  ejRead: "fiscal-reporting.ej.read", ejExport: "fiscal-reporting.ej.export",
  xRead: "fiscal-reporting.x.read", xGenerate: "fiscal-reporting.x.generate",
  zRead: "fiscal-reporting.z.read", zGenerate: "fiscal-reporting.z.generate"
} as const;

export interface FiscalReportingPeriod { fiscalReportingPeriodId:string; businessDayDate:string; periodStartAt:string; periodEndAt:string; currencyCode:string; periodSequence:number; status:string; expectedZStateVersion:number }
export interface FiscalReadingAmounts { grossSalesAmountMinorUnits:number; netSalesAmountMinorUnits:number; vatableSalesAmountMinorUnits:number; vatAmountMinorUnits:number; vatExemptSalesAmountMinorUnits:number; zeroRatedSalesAmountMinorUnits:number; discountAmountMinorUnits:number; seniorCitizenDiscountAmountMinorUnits:number; pwdDiscountAmountMinorUnits:number; otherStatutoryDiscountAmountMinorUnits:number; voidAmountMinorUnits:number; adjustmentAmountMinorUnits:number }
export interface FiscalTenderBreakdown { classification:string; transactionCount:number; amountMinorUnits:number; currencyCode:string }
export interface FiscalDiscountBreakdown { classification:string; qualifyingDocumentCount:number; discountAmountMinorUnits:number; vatExemptionAmountMinorUnits:number; currencyCode:string }
export interface FiscalReading { reportReference:string; reportKind:"X_READING"|"Z_READING"; fiscalReportingPeriodId:string; businessDayDate:string; periodStartAt:string; periodEndAt:string; generatedAt:string; transactionCount:number; amounts:FiscalReadingAmounts; currencyCode:string; periodSequence:number; reportStatus:string; tenders:FiscalTenderBreakdown[]; discounts:FiscalDiscountBreakdown[] }
export interface FiscalReadingHistory { currentPeriod?:FiscalReportingPeriod; readings:FiscalReading[] }
export interface ElectronicJournalInvoice { fiscalDocumentId:string; fiscalDocumentNumber:string; businessDayDate:string; issuedAt:string; printableText:string }
export interface ElectronicJournalResult { invoices:ElectronicJournalInvoice[] }
export interface FiscalReportingClient {
  readElectronicJournal(siteId:string,start:string,end:string,search?:string):Promise<ElectronicJournalResult>;
  electronicJournalDownloadUrl(siteId:string,start:string,end:string,search?:string):string;
  readXHistory(siteId:string):Promise<FiscalReadingHistory>;
  generateX(siteId:string):Promise<FiscalReading>;
  readingDownloadUrl(siteId:string,kind:"x"|"z",reference:string):string;
  readZHistory(siteId:string):Promise<FiscalReadingHistory>;
  generateZ(siteId:string):Promise<FiscalReading>;
}

export function createFiscalReportingClient(api:CentralPmsApiClient):FiscalReportingClient {
  const root="/v1/management-platform/fiscal-reporting/sites";
  const path=(siteId:string,suffix:string)=>`${root}/${encodeURIComponent(siteId)}${suffix}`;
  return {
    async readElectronicJournal(siteId,start,end,search){const q=new URLSearchParams({periodStart:start,periodEnd:end});if(search?.trim())q.set("search",search.trim());return parseEj(await api.request(path(siteId,`/electronic-journal?${q}`)));},
    electronicJournalDownloadUrl(siteId,start,end,search){const q=new URLSearchParams({periodStart:start,periodEnd:end});if(search?.trim())q.set("search",search.trim());return path(siteId,`/electronic-journal/download?${q}`);},
    async readXHistory(siteId){return parseHistory(await api.request(path(siteId,"/x-readings")),"X_READING");},
    async generateX(siteId){return parseGenerated(await api.request(path(siteId,"/x-readings"),{method:"POST",body:{operationKey:crypto.randomUUID()}}),"xReading","X_READING");},
    readingDownloadUrl(siteId,kind,reference){return path(siteId,`/${kind}-readings/${encodeURIComponent(reference)}/download`);},
    async readZHistory(siteId){return parseHistory(await api.request(path(siteId,"/z-readings")),"Z_READING");},
    async generateZ(siteId){return parseGenerated(await api.request(path(siteId,"/z-readings"),{method:"POST",body:{operationKey:crypto.randomUUID(),confirmClose:true}}),"zReading","Z_READING");}
  };
}

export function createFiscalReportingFixtureClient():FiscalReportingClient {
  const amounts:FiscalReadingAmounts={grossSalesAmountMinorUnits:11200,netSalesAmountMinorUnits:11200,vatableSalesAmountMinorUnits:10000,vatAmountMinorUnits:1200,vatExemptSalesAmountMinorUnits:0,zeroRatedSalesAmountMinorUnits:0,discountAmountMinorUnits:0,seniorCitizenDiscountAmountMinorUnits:0,pwdDiscountAmountMinorUnits:0,otherStatutoryDiscountAmountMinorUnits:0,voidAmountMinorUnits:0,adjustmentAmountMinorUnits:0};
  const period:FiscalReportingPeriod={fiscalReportingPeriodId:"91000000-0000-4000-8000-000000000001",businessDayDate:"2026-09-10",periodStartAt:"2026-09-09T16:00:00Z",periodEndAt:"2026-09-10T16:00:00Z",currencyCode:"PHP",periodSequence:91,status:"OPEN",expectedZStateVersion:14};
  const reading=(kind:"X_READING"|"Z_READING",suffix:string):FiscalReading=>({reportReference:`${kind[0]}-20260910-${suffix}`,reportKind:kind,fiscalReportingPeriodId:period.fiscalReportingPeriodId,businessDayDate:period.businessDayDate,periodStartAt:period.periodStartAt,periodEndAt:period.periodEndAt,generatedAt:"2026-09-10T08:15:00Z",transactionCount:1,amounts,currencyCode:"PHP",periodSequence:91,reportStatus:"COMMITTED",tenders:[{classification:"cash",transactionCount:1,amountMinorUnits:11200,currencyCode:"PHP"}],discounts:[]});
  const invoice:ElectronicJournalInvoice={fiscalDocumentId:"92000000-0000-4000-8000-000000000001",fiscalDocumentNumber:"SI-00000001",businessDayDate:"2026-09-10",issuedAt:"2026-09-10T01:00:00Z",printableText:"                 SALES INVOICE\r\nExitPass Parking Corporation\r\nSI No.             : SI-00000001\r\nCustomer Information\r\nCustomer Name      : Juan Dela Cruz\r\nAddress            : 123 Sample Street\r\nTIN                : 123-456-789-000\r\nBusiness Style     : Retail\r\nOSCA ID No.        : OSCA-0001\r\nCustomer Signature : ____________________\r\nVATable Sales      : PHP 100.00\r\nVAT Amount         : PHP 12.00\r\nVAT Exempt Sales   : PHP 0.00\r\nZero Rated Sales   : PHP 0.00\r\nTOTAL              : PHP 112.00\r\n"};
  return {readElectronicJournal:async()=>({invoices:[invoice]}),electronicJournalDownloadUrl:(site,start,end)=>`/v1/management-platform/fiscal-reporting/sites/${site}/electronic-journal/download?periodStart=${encodeURIComponent(start)}&periodEnd=${encodeURIComponent(end)}`,readXHistory:async()=>({currentPeriod:period,readings:[reading("X_READING","001")]}),generateX:async()=>reading("X_READING","002"),readingDownloadUrl:(site,kind,ref)=>`/v1/management-platform/fiscal-reporting/sites/${site}/${kind}-readings/${ref}/download`,readZHistory:async()=>({currentPeriod:period,readings:[reading("Z_READING","001")]}),generateZ:async()=>reading("Z_READING","002")};
}

function parseEj(value:unknown):ElectronicJournalResult{const o=record(value);if(!Array.isArray(o.invoices))throw malformed();return{invoices:o.invoices.map(item=>{const x=record(item);return{fiscalDocumentId:text(x.fiscalDocumentId),fiscalDocumentNumber:text(x.fiscalDocumentNumber),businessDayDate:text(x.businessDayDate),issuedAt:text(x.issuedAt),printableText:text(x.printableText)};})};}
function parseHistory(value:unknown,kind:FiscalReading["reportKind"]):FiscalReadingHistory{const o=record(value);if(!Array.isArray(o.readings))throw malformed();return{currentPeriod:o.currentPeriod==null?undefined:period(record(o.currentPeriod)),readings:o.readings.map(x=>reading(record(x),kind))};}
function parseGenerated(value:unknown,key:string,kind:FiscalReading["reportKind"]):FiscalReading{const o=record(value);return reading(record(o[key]),kind);}
function period(o:Record<string,unknown>):FiscalReportingPeriod{return{fiscalReportingPeriodId:text(o.fiscalReportingPeriodId),businessDayDate:text(o.businessDayDate),periodStartAt:text(o.periodStartAt),periodEndAt:text(o.periodEndAt),currencyCode:text(o.currencyCode),periodSequence:num(o.periodSequence),status:text(o.status),expectedZStateVersion:num(o.expectedZStateVersion)};}
function reading(o:Record<string,unknown>,kind:FiscalReading["reportKind"]):FiscalReading{if(o.reportKind!==kind||!Array.isArray(o.tenders)||!Array.isArray(o.discounts))throw malformed();const a=record(o.amounts);return{reportReference:text(o.reportReference),reportKind:kind,fiscalReportingPeriodId:text(o.fiscalReportingPeriodId),businessDayDate:text(o.businessDayDate),periodStartAt:text(o.periodStartAt),periodEndAt:text(o.periodEndAt),generatedAt:text(o.generatedAt),transactionCount:num(o.transactionCount),amounts:{grossSalesAmountMinorUnits:num(a.grossSalesAmountMinorUnits),netSalesAmountMinorUnits:num(a.netSalesAmountMinorUnits),vatableSalesAmountMinorUnits:num(a.vatableSalesAmountMinorUnits),vatAmountMinorUnits:num(a.vatAmountMinorUnits),vatExemptSalesAmountMinorUnits:num(a.vatExemptSalesAmountMinorUnits),zeroRatedSalesAmountMinorUnits:num(a.zeroRatedSalesAmountMinorUnits),discountAmountMinorUnits:num(a.discountAmountMinorUnits),seniorCitizenDiscountAmountMinorUnits:num(a.seniorCitizenDiscountAmountMinorUnits),pwdDiscountAmountMinorUnits:num(a.pwdDiscountAmountMinorUnits),otherStatutoryDiscountAmountMinorUnits:num(a.otherStatutoryDiscountAmountMinorUnits),voidAmountMinorUnits:num(a.voidAmountMinorUnits),adjustmentAmountMinorUnits:num(a.adjustmentAmountMinorUnits)},currencyCode:text(o.currencyCode),periodSequence:num(o.periodSequence),reportStatus:text(o.reportStatus),tenders:o.tenders.map(v=>{const t=record(v);return{classification:text(t.classification),transactionCount:num(t.transactionCount),amountMinorUnits:num(t.amountMinorUnits),currencyCode:text(t.currencyCode)}}),discounts:o.discounts.map(v=>{const d=record(v);return{classification:text(d.classification),qualifyingDocumentCount:num(d.qualifyingDocumentCount),discountAmountMinorUnits:num(d.discountAmountMinorUnits),vatExemptionAmountMinorUnits:num(d.vatExemptionAmountMinorUnits),currencyCode:text(d.currencyCode)}})};}
function record(v:unknown):Record<string,unknown>{if(typeof v!=="object"||v===null||Array.isArray(v))throw malformed();return v as Record<string,unknown>;}function text(v:unknown):string{if(typeof v!=="string"||!v)throw malformed();return v;}function num(v:unknown):number{if(typeof v!=="number"||!Number.isSafeInteger(v))throw malformed();return v;}function malformed(){return new Error("The authoritative fiscal reporting response was malformed.");}
