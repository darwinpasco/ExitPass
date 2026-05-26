import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

vi.mock("@zxing/browser", () => ({
  BrowserQRCodeReader: vi.fn().mockImplementation(() => ({
    decodeFromVideoDevice: vi.fn().mockRejectedValue(new DOMException("Denied", "NotAllowedError"))
  }))
}));

const successResponse = {
  paymentAttemptId: "44444444-4444-4444-4444-444444444444",
  parkingSessionId: "55555555-5555-5555-5555-555555555555",
  tariffSnapshotId: "66666666-6666-6666-6666-666666666666",
  siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
  siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
  vendorSystemId: "45a625de-9034-4fb6-b527-0950d384e51f",
  siteGroupName: "WebPay Test Site Group 2026-05-19",
  amountMinorUnits: 12500,
  currency: "PHP",
  siteName: "Mactan Newtown Parking",
  ticketReference: "TICKET-TEST-023",
  plateNumber: "ABC 1234",
  entryTime: "2026-05-18T10:42:00+08:00",
  currentFeeCalculationTime: "2026-05-18T12:57:00+08:00",
  durationParked: "2h 15m",
  tariffName: "Weekend Rate",
  feeValidUntil: "2026-05-18T13:15:00+08:00",
  parkingStatus: "PaymentRequired",
  paymentStatus: "Not Started",
  paymentMethod: "QRPH",
  selectedProviderCode: "PAYMONGO",
  fallbackProviderCode: null,
  routingReason: "PRIMARY_PROVIDER",
  status: "PENDING_PROVIDER",
  handoff: {
    type: "Redirect",
    handoffUrl: "https://payments.test/handoff",
    qrCodeUrl: "https://payments.test/qr.png"
  },
  correlationId: "77777777-7777-7777-7777-777777777777"
};

const activePaymentAttemptConflict = {
  errorCode: "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
  message: "An active payment attempt already exists for parking session '55555555-5555-5555-5555-555555555555'.",
  retryable: false,
  correlationId: "77777777-7777-7777-7777-777777777777"
};

function stubWebPayFetch(options?: {
  resolvePayload?: unknown;
  resolveOk?: boolean;
  resolveStatus?: number;
  intentPayload?: unknown;
  intentOk?: boolean;
  intentStatus?: number;
  couponPayload?: unknown;
  couponOk?: boolean;
  couponStatus?: number;
  statutoryPayload?: unknown;
  statutoryOk?: boolean;
  statutoryStatus?: number;
}) {
  const fetchMock = vi.fn(async (url: string, _init?: RequestInit) => {
    const isResolve = url.includes("/v1/webpay/parking-session");
    const isCoupon = url.includes("/v1/public/coupons/apply");
    const isStatutory = url.includes("/v1/public/discounts/statutory/validate");
    return {
      ok: isResolve
        ? options?.resolveOk ?? true
        : isCoupon
          ? options?.couponOk ?? true
          : isStatutory
            ? options?.statutoryOk ?? true
            : options?.intentOk ?? true,
      status: isResolve
        ? options?.resolveStatus ?? 200
        : isCoupon
          ? options?.couponStatus ?? 200
          : isStatutory
            ? options?.statutoryStatus ?? 200
            : options?.intentStatus ?? 200,
      json: async () => (
        isResolve
          ? options?.resolvePayload ?? successResponse
          : isCoupon
            ? options?.couponPayload ?? { status: "APPROVED", couponCode: "SAVE50", tariffSnapshotId: successResponse.tariffSnapshotId, originalAmountMinorUnits: 12500, adjustmentMinorUnits: 5000, finalAmountMinorUnits: 7500, currency: "PHP" }
            : isStatutory
              ? options?.statutoryPayload ?? { status: "PENDING_REVIEW", message: "Review pending." }
              : options?.intentPayload ?? successResponse
      )
    };
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

beforeEach(() => {
  vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_GROUP_ID", "11111111-1111-1111-1111-111111111111");
  vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_ID", "22222222-2222-2222-2222-222222222222");
  vi.stubEnv("VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID", "HIKCENTRAL");
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllEnvs();
  window.history.pushState({}, "", "/");
});

describe("ExitPass WebPay UI", () => {
  async function resolveTicket(ticketReference = "TICKET-001", expectedAmount = "125.00") {
    await userEvent.type(screen.getByLabelText(/ticket reference/i), ticketReference);
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));
    expect((await screen.findAllByText(expectedAmount)).length).toBeGreaterThan(0);
  }

  async function continueToPayment() {
    await userEvent.click(screen.getByRole("button", { name: /continue to payment/i }));
  }

  it("WebPay_WhenEnterPressedInTicketInput_ResolvesSessionOnly", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "TICKET-TEST-027{Enter}");

    expect(await screen.findByText("Parking Session Summary")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toContain("/v1/webpay/parking-session");
    expect(fetchMock.mock.calls[0][0]).not.toContain("/v1/webpay/payment-intents");
  });

  it("WebPay_WhenEnterPressedInPlateInput_ResolvesSessionOnly", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /plate/i }));
    await userEvent.type(screen.getByLabelText(/plate number/i), "ABC 1234{Enter}");

    expect(await screen.findByText("Parking Session Summary")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toContain("/v1/webpay/parking-session");
    expect(fetchMock.mock.calls[0][0]).not.toContain("/v1/webpay/payment-intents");
  });

  it("WebPay_WhenInitialContinueClicked_ResolvesSessionBeforePaymentIntent", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "TICKET-TEST-027");
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));

    expect(await screen.findByText("Parking Session Summary")).toBeInTheDocument();
    expect(screen.getAllByText("125.00").length).toBeGreaterThan(0);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toContain("/v1/webpay/parking-session");
  });

  it("WebPay_WhenLookupInputIsInvalid_RejectsClientSide", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "@@");
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Enter a valid ticket reference.");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("WebPay_WhenSummaryResolved_ContinueToPaymentCreatesPaymentIntent", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-TEST-027");
    expect(screen.getByText("Parking Status")).toBeInTheDocument();
    expect(screen.getByText("PaymentRequired")).toBeInTheDocument();
    await continueToPayment();

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(fetchMock.mock.calls[0][0]).toContain("/v1/webpay/parking-session");
    expect(fetchMock.mock.calls[1][0]).toContain("/v1/webpay/payment-intents");
  });

  it("WebPay_WhenCouponInputRenders_AllowsCouponApplicationBeforePayment", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-COUPON-001");
    expect(screen.getByLabelText(/coupon code/i)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText(/coupon code/i), "save50");
    await userEvent.click(screen.getByRole("button", { name: /apply coupon/i }));

    expect((await screen.findAllByText(/coupon applied/i)).length).toBeGreaterThan(0);
    expect(screen.getAllByText("PHP 125.00").length).toBeGreaterThan(0);
    expect(screen.getAllByText("-PHP 50.00").length).toBeGreaterThan(0);
    expect(screen.getAllByText("PHP 75.00").length).toBeGreaterThan(0);
    expect(fetchMock.mock.calls[1][0]).toContain("/v1/public/coupons/apply");
  });

  it("WebPay_WhenInvalidCouponReturned_ShowsDeterministicError", async () => {
    stubWebPayFetch({
      couponOk: false,
      couponStatus: 422,
      couponPayload: { errorCode: "INVALID_COUPON", message: "bad coupon" }
    });

    render(<App />);

    await resolveTicket("TICKET-COUPON-002");
    await userEvent.type(screen.getByLabelText(/coupon code/i), "bad");
    await userEvent.click(screen.getByRole("button", { name: /apply coupon/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Enter a valid coupon code.");
  });

  it("WebPay_WhenStatutoryRequestIsPending_BlocksPaymentInitiation", async () => {
    const fetchMock = stubWebPayFetch({
      statutoryPayload: { status: "PENDING_REVIEW", message: "Review pending." }
    });

    render(<App />);

    await resolveTicket("TICKET-STAT-001");
    expect(screen.getByLabelText(/statutory discount/i)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText(/evidence reference/i), "HASH-ONLY-REF");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));

    expect(await screen.findByText(/pending review/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /discount review pending/i })).toBeDisabled();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("WebPay_WhenStatutoryApproved_UpdatesPayableAmount", async () => {
    stubWebPayFetch({
      statutoryPayload: {
        status: "APPROVED",
        tariffSnapshotId: "77777777-7777-7777-7777-777777777777",
        originalAmountMinorUnits: 12500,
        adjustmentMinorUnits: 2500,
        finalAmountMinorUnits: 10000,
        currency: "PHP"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-STAT-002");
    await userEvent.type(screen.getByLabelText(/evidence reference/i), "HASH-ONLY-REF");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));

    expect((await screen.findAllByText(/statutory discount approved/i)).length).toBeGreaterThan(0);
    expect(screen.getAllByText("-PHP 25.00").length).toBeGreaterThan(0);
    expect(screen.getAllByText("PHP 100.00").length).toBeGreaterThan(0);
  });

  it("WebPay_WhenStatutoryRejectedOrExpired_RendersDeterministicErrors", async () => {
    const fetchMock = stubWebPayFetch({
      statutoryPayload: { status: "REJECTED" }
    });

    render(<App />);

    await resolveTicket("TICKET-STAT-003");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Statutory discount validation was rejected.");

    fetchMock.mockImplementation(async (url: string) => ({
      ok: true,
      status: 200,
      json: async () => url.includes("/v1/public/discounts/statutory/validate")
        ? { status: "EXPIRED" }
        : successResponse
    }));

    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Statutory discount validation has expired.");
  });

  it("WebPay_WhenCouponApplied_PaymentInitiationUsesFinalApprovedBasis", async () => {
    const fetchMock = stubWebPayFetch({
      couponPayload: {
        status: "APPROVED",
        couponCode: "SAVE50",
        tariffSnapshotId: "77777777-7777-7777-7777-777777777777",
        originalAmountMinorUnits: 12500,
        adjustmentMinorUnits: 5000,
        finalAmountMinorUnits: 7500,
        currency: "PHP"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-COUPON-003");
    await userEvent.type(screen.getByLabelText(/coupon code/i), "SAVE50");
    await userEvent.click(screen.getByRole("button", { name: /apply coupon/i }));
    expect((await screen.findAllByText(/coupon applied/i)).length).toBeGreaterThan(0);
    await continueToPayment();

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3));
    const body = JSON.parse((fetchMock.mock.calls[2][1] as RequestInit).body as string);
    expect(body.tariffSnapshotId).toBe("77777777-7777-7777-7777-777777777777");
    expect(body.expectedAmountMinorUnits).toBe(7500);
  });

  it("WebPay_WhenPaymentInitiated_DiscountAndCouponCannotBeChanged", async () => {
    stubWebPayFetch({
      couponPayload: {
        status: "APPROVED",
        couponCode: "SAVE50",
        tariffSnapshotId: "77777777-7777-7777-7777-777777777777",
        originalAmountMinorUnits: 12500,
        adjustmentMinorUnits: 5000,
        finalAmountMinorUnits: 7500,
        currency: "PHP"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-COUPON-004");
    await userEvent.type(screen.getByLabelText(/coupon code/i), "SAVE50");
    await userEvent.click(screen.getByRole("button", { name: /apply coupon/i }));
    expect((await screen.findAllByText(/coupon applied/i)).length).toBeGreaterThan(0);
    await continueToPayment();

    expect(await screen.findByRole("link", { name: /continue to payment/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/coupon code/i)).toBeDisabled();
    expect(screen.getByRole("button", { name: /coupon applied/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /request statutory discount/i })).toBeDisabled();
  });

  it("WebPay_WhenEvidenceReferenceEntered_DoesNotDisplayRawEvidencePayload", async () => {
    stubWebPayFetch({
      statutoryPayload: {
        status: "PENDING_REVIEW",
        rawEvidencePayload: "do-not-render"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-STAT-004");
    await userEvent.type(screen.getByLabelText(/evidence reference/i), "HASH-ONLY-REF");
    await userEvent.click(screen.getByRole("button", { name: /request statutory discount/i }));

    expect(await screen.findByText(/pending review/i)).toBeInTheDocument();
    expect(screen.queryByText(/do-not-render/i)).not.toBeInTheDocument();
  });

  it("WebPay_WhenResolvedSessionIsPaid_DoesNotCreatePaymentIntent", async () => {
    const fetchMock = stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Paid"
      }
    });

    render(<App />);

    await resolveTicket("WEBPAY-20260521-FRESH-005");

    expect(await screen.findByRole("heading", { name: /payment completed/i })).toBeInTheDocument();
    expect(screen.getByText("Payment Status")).toBeInTheDocument();
    expect(screen.getByText("Paid")).toBeInTheDocument();
    expect(screen.getByText("Parking Status")).toBeInTheDocument();
    expect(screen.getByText("Payment Completed")).toBeInTheDocument();
    expect(screen.queryByText("PaymentRequired")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /payment completed/i })).toBeDisabled();

    await userEvent.click(screen.getByRole("button", { name: /payment completed/i }));

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toContain("/v1/webpay/parking-session");
  });

  it("WebPay_WhenResolvedSessionHasContext_UsesResolvedContextForPaymentIntentInsteadOfDefaults", async () => {
    vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_GROUP_ID", "00000000-0000-0000-0000-000000000001");
    vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_ID", "00000000-0000-0000-0000-000000000002");
    vi.stubEnv("VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID", "00000000-0000-0000-0000-000000000003");
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await resolveTicket("WEBPAY-20260519-FRESH-001");
    await continueToPayment();

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    const body = JSON.parse((fetchMock.mock.calls[1][1] as RequestInit).body as string);
    expect(body.siteGroupId).toBe("29b8b4f4-40dd-447b-ac06-dd52e6ad51c5");
    expect(body.siteId).toBe("93bd3cb3-e806-4c5c-ac8c-df6c4addff14");
    expect(body.vendorSystemId).toBe("45a625de-9034-4fb6-b527-0950d384e51f");
    expect(body.siteGroupId).not.toBe("00000000-0000-0000-0000-000000000001");
    expect(body.siteId).not.toBe("00000000-0000-0000-0000-000000000002");
    expect(body.vendorSystemId).not.toBe("00000000-0000-0000-0000-000000000003");
  });

  it("WebPay_WhenMay21SessionResolved_DoesNotUseMockIdsOrRender2030Dates", async () => {
    vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_GROUP_ID", "00000000-0000-0000-0000-000000000001");
    vi.stubEnv("VITE_WEBPAY_DEFAULT_SITE_ID", "00000000-0000-0000-0000-000000000002");
    vi.stubEnv("VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID", "00000000-0000-0000-0000-000000000003");
    const fetchMock = stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        siteGroupId: "d392e487-fba0-4281-bdf4-8d62f923d518",
        siteId: "6c4b92bd-ced4-44e3-a61a-4349aa81f91d",
        vendorSystemId: "25831de5-7144-4a34-a6ea-4ef2bd65c89c",
        siteGroupName: "WebPay Test Site Group 2026-05-21",
        siteName: "WebPay Test Site 2026-05-21",
        ticketReference: "WEBPAY-20260521-FRESH-001",
        plateNumber: "WEBPAY001",
        entryTime: "2026-05-20T18:01:00+00:00",
        currentFeeCalculationTime: "2026-05-21T00:00:00+00:00",
        feeValidUntil: "2026-05-21T15:59:59+00:00",
        amountMinorUnits: 10000,
        parkingStatus: "PaymentRequired",
        paymentStatus: "Not Started"
      },
      intentPayload: {
        ...successResponse,
        siteGroupId: "d392e487-fba0-4281-bdf4-8d62f923d518",
        siteId: "6c4b92bd-ced4-44e3-a61a-4349aa81f91d",
        vendorSystemId: "25831de5-7144-4a34-a6ea-4ef2bd65c89c",
        ticketReference: "WEBPAY-20260521-FRESH-001",
        entryTime: "2026-05-20T18:01:00+00:00",
        currentFeeCalculationTime: "2026-05-21T00:00:00+00:00",
        feeValidUntil: "2026-05-21T15:59:59+00:00",
        amountMinorUnits: 10000
      }
    });

    render(<App />);

    await resolveTicket("WEBPAY-20260521-FRESH-001", "100.00");

    expect(screen.getAllByText("WebPay Test Site 2026-05-21").length).toBeGreaterThan(0);
    expect(screen.getAllByText("WEBPAY-20260521-FRESH-001").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/May 21, 2026/).length).toBeGreaterThan(0);
    expect(screen.queryByText(/Apr 1, 2030/i)).not.toBeInTheDocument();

    await continueToPayment();

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    const body = JSON.parse((fetchMock.mock.calls[1][1] as RequestInit).body as string);
    expect(body.siteGroupId).toBe("d392e487-fba0-4281-bdf4-8d62f923d518");
    expect(body.siteId).toBe("6c4b92bd-ced4-44e3-a61a-4349aa81f91d");
    expect(body.vendorSystemId).toBe("25831de5-7144-4a34-a6ea-4ef2bd65c89c");
    expect(body.siteGroupId).not.toBe("00000000-0000-0000-0000-000000000001");
    expect(body.siteId).not.toBe("00000000-0000-0000-0000-000000000002");
    expect(body.vendorSystemId).not.toBe("00000000-0000-0000-0000-000000000003");
  });

  it("WebPay_WhenTicketChanges_ClearsStalePaymentIntentError", async () => {
    stubWebPayFetch({
      intentOk: false,
      intentStatus: 502,
      intentPayload: { errorCode: "PAYMENT_FAILED", message: "Payment intent creation failed. Please try again." }
    });

    render(<App />);

    await resolveTicket("TICKET-TEST-027");
    await continueToPayment();
    expect(await screen.findByRole("alert")).toHaveTextContent("Payment intent creation failed");

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "8");

    expect(screen.queryByText(/Payment intent creation failed/i)).not.toBeInTheDocument();
  });

  it("WebPay_WhenApiReturnsHandoff_DisplaysContinueToPayment", async () => {
    stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect(await screen.findByRole("link", { name: /continue to payment/i })).toHaveAttribute(
      "href",
      "https://payments.test/handoff"
    );
    expect(screen.getAllByText("PHP").length).toBeGreaterThan(0);
    expect(screen.getAllByText("PENDING_PROVIDER").length).toBeGreaterThan(0);
    expect(screen.getAllByText("125.00").length).toBeGreaterThan(0);
  });

  it("WebPay_WhenApiReturnsSessionSummary_DisplaysParkingSessionSummary", async () => {
    stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-TEST-023");

    expect(await screen.findByRole("heading", { name: /mactan newtown parking/i })).toBeInTheDocument();
    expect(screen.getByText("Parking Session Summary")).toBeInTheDocument();
    expect(screen.getByText("Site Name")).toBeInTheDocument();
    expect(screen.getAllByText("Ticket").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Plate").length).toBeGreaterThan(0);
    expect(screen.getByText("Entry Time")).toBeInTheDocument();
    expect(screen.getByText("Duration")).toBeInTheDocument();
    expect(screen.getByText("Total Fee")).toBeInTheDocument();
    expect(screen.getAllByText("Amount Due").length).toBeGreaterThan(0);
    expect(screen.getByText("Parking Status")).toBeInTheDocument();
    expect(screen.getByText("Payment Status")).toBeInTheDocument();
    expect(screen.getByText("Fee Valid Until")).toBeInTheDocument();
    expect(screen.getByText("TICKET-TEST-023")).toBeInTheDocument();
    expect(screen.getByText("ABC 1234")).toBeInTheDocument();
    expect(screen.getAllByText(/May 18, 2026/).length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("125.00").length).toBeGreaterThan(0);
    expect(screen.getAllByText("PHP 125.00").length).toBeGreaterThan(0);
    expect(screen.queryByText(/Apr 1, 2030/i)).not.toBeInTheDocument();
  });

  it("WebPay_WhenNewParkingSessionIsResolved_ClearsStalePaymentState", async () => {
    const fetchMock = stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: {
        ...activePaymentAttemptConflict,
        handoff: {
          type: "Redirect",
          resumePaymentUrl: "https://payments.test/existing"
        }
      }
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();
    expect(await screen.findByRole("link", { name: /continue existing payment/i })).toBeInTheDocument();

    await userEvent.clear(screen.getByLabelText(/ticket reference/i));
    await userEvent.type(screen.getByLabelText(/ticket reference/i), "TICKET-002");
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));

    expect(await screen.findByText("Parking Session Summary")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /continue existing payment/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /payment already started/i })).not.toBeInTheDocument();
    expect(screen.queryByText("https://payments.test/existing")).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it("WebPay_WhenSiteNameMissing_HidesSiteNameInsteadOfShowingUuid", async () => {
    stubWebPayFetch({ resolvePayload: { ...successResponse, siteName: undefined } });

    render(<App />);

    await resolveTicket("TICKET-TEST-023");

    expect(await screen.findByRole("heading", { name: /^parking session summary$/i })).toBeInTheDocument();
    expect(screen.queryByText("22222222-2222-2222-2222-222222222222")).not.toBeInTheDocument();
  });

  it("WebPay_WhenSummaryRenders_DoesNotExposeInternalUuidFieldsInNormalUi", async () => {
    stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-TEST-023");

    expect(screen.getByText("Parking Session Summary")).toBeInTheDocument();
    expect(screen.queryByText("55555555-5555-5555-5555-555555555555")).not.toBeInTheDocument();
    expect(screen.queryByText("66666666-6666-6666-6666-666666666666")).not.toBeInTheDocument();
    expect(screen.queryByText("44444444-4444-4444-4444-444444444444")).not.toBeInTheDocument();
  });

  it("WebPay_WhenPaymentHandoffRenders_UsesDistinctStatusLabels", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        parkingStatus: "PAYABLE"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-TEST-023");
    await continueToPayment();

    expect((await screen.findAllByText("Payment Status")).length).toBeGreaterThan(0);
    expect(screen.getByText("Parking Status")).toBeInTheDocument();
    expect(screen.queryByText(/^Status$/)).not.toBeInTheDocument();
  });

  it("WebPay_WhenApiReturnsQrCodeUrl_DisplaysQrPaymentInstructions", async () => {
    stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect(await screen.findByText(/QR payment instructions/i)).toBeInTheDocument();
    expect(screen.getByText("https://payments.test/qr.png")).toBeInTheDocument();
  });

  it("WebPay_WhenApiReturnsError_DisplaysFriendlyError", async () => {
    stubWebPayFetch({
      resolveOk: false,
      resolveStatus: 404,
      resolvePayload: { errorCode: "SESSION_NOT_FOUND", message: "Vendor parking session was not found." }
    });

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "UNKNOWN");
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("could not find an active parking session");
  });

  it("WebPay_WhenActivePaymentAttemptConflict_ShowsPaymentAlreadyStarted", async () => {
    const fetchMock = stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: activePaymentAttemptConflict
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect(await screen.findByRole("heading", { name: /payment already started/i })).toBeInTheDocument();
    expect(screen.getByText(/cannot be resumed directly/i)).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("WebPay_WhenActivePaymentAttemptHasHandoff_ShowsContinueExistingPayment", async () => {
    stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: {
        ...activePaymentAttemptConflict,
        handoff: {
          type: "Redirect",
          resumePaymentUrl: "https://payments.test/existing"
        }
      }
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect(await screen.findByRole("link", { name: /continue existing payment/i })).toHaveAttribute(
      "href",
      "https://payments.test/existing"
    );
    expect(screen.getByRole("button", { name: /check status/i })).toBeInTheDocument();
  });

  it("WebPay_WhenActivePaymentAttemptHasNoHandoff_ShowsCheckStatusFallback", async () => {
    stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: activePaymentAttemptConflict
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect((await screen.findAllByRole("button", { name: /check status/i })).length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: /continue existing payment/i })).not.toBeInTheDocument();
    expect(screen.getByText(/cannot be resumed directly/i)).toBeInTheDocument();
  });

  it("WebPay_WhenActivePaymentAttemptVisible_OuterContinueDoesNotCreateDuplicateAttempt", async () => {
    const fetchMock = stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: activePaymentAttemptConflict
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();
    await screen.findAllByRole("button", { name: /check status/i });
    await userEvent.click(screen.getAllByRole("button", { name: /check status/i }).at(-1)!);

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("WebPay_WhenActivePaymentAttemptConflict_DoesNotExposeProviderChoice", async () => {
    stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: {
        ...activePaymentAttemptConflict,
        selectedProviderCode: "AUB",
        fallbackProviderCode: "PAYMONGO"
      }
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();

    expect(await screen.findByRole("heading", { name: /payment already started/i })).toBeInTheDocument();
    expect(screen.queryByText("AUB")).not.toBeInTheDocument();
    expect(screen.queryByText("PAYMONGO")).not.toBeInTheDocument();
  });

  it("WebPay_WhenActivePaymentAttemptConflict_ShowsCorrelationIdInSupportDetails", async () => {
    stubWebPayFetch({
      intentOk: false,
      intentStatus: 409,
      intentPayload: activePaymentAttemptConflict
    });

    render(<App />);

    await resolveTicket("TICKET-001");
    await continueToPayment();
    await userEvent.click(await screen.findByText(/support details/i));

    expect(screen.getByText("77777777-7777-7777-7777-777777777777")).toBeInTheDocument();
    expect(screen.queryByText("55555555-5555-5555-5555-555555555555")).not.toBeInTheDocument();
  });

  it("WebPay_WhenCameraUnavailable_ShowsManualFallback", async () => {
    vi.stubGlobal("navigator", { mediaDevices: undefined });

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /scan qr/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Camera is unavailable");
    expect(screen.getByLabelText(/ticket reference/i)).toBeInTheDocument();
  });

  it("WebPay_WhenCameraDenied_ShowsManualFallback", async () => {
    vi.stubGlobal("navigator", {
      mediaDevices: {
        getUserMedia: vi.fn()
      }
    });

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /scan qr/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/Camera permission was denied|Camera is unavailable/);
    expect(screen.getByLabelText(/ticket reference/i)).toBeInTheDocument();
  });

  it("WebPay_UsesExistingAssetPaths", () => {
    render(<App />);

    expect(screen.getByAltText("ExitPass")).toHaveAttribute("src", "/assets/logo/exitpass-logo.svg");
    expect(screen.getByAltText("Pro Parking")).toHaveAttribute("src", "/assets/logo/proparking-logo.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/qrph.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/cards-visa-mastercard.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/gcash.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/maya.png");
    expect(screen.getByLabelText(/qrph/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/gcash/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/maya/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/card/i)).toBeInTheDocument();
    expect(screen.getByText("Pay using any QRPh-supported bank or e-wallet")).toBeInTheDocument();
    expect(screen.getByText("Pay with GCash through PayMongo Checkout")).toBeInTheDocument();
    expect(screen.getByText("Pay with Maya through PayMongo Checkout")).toBeInTheDocument();
    expect(screen.getByText("Visa or Mastercard through PayMongo Checkout")).toBeInTheDocument();
    expect(screen.queryByText("AUB")).not.toBeInTheDocument();
  });

  it.each([
    ["QRPh", "QRPH"],
    ["GCash", "GCASH"],
    ["Maya", "MAYA"],
    ["Card", "CARD"]
  ])("WebPay_When%sSelected_InitiatesPaymentWithSelectedMethod", async (label, expectedMethod) => {
    const fetchMock = stubWebPayFetch({ intentPayload: { ...successResponse, paymentMethod: expectedMethod } });

    render(<App />);

    await resolveTicket("TICKET-001");
    await userEvent.click(screen.getByLabelText(new RegExp(label, "i")));
    await continueToPayment();

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    const body = JSON.parse((fetchMock.mock.calls[1][1] as RequestInit).body as string);
    expect(body.paymentMethod).toBe(expectedMethod);
  });

  it("WebPay_WhenPlateEntered_SubmitsPlateNumber", async () => {
    const fetchMock = stubWebPayFetch({ intentPayload: { ...successResponse, paymentMethod: "QRPH" } });

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /plate/i }));
    await userEvent.type(screen.getByLabelText(/plate number/i), "abc 1234");
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));
    await screen.findByText("Parking Session Summary");
    await continueToPayment();

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    const intentCall = fetchMock.mock.calls[1];
    expect(intentCall).toBeDefined();
    const body = JSON.parse((intentCall?.[1] as RequestInit).body as string);
    expect(body).toEqual({
      plateNumber: "ABC 1234",
      paymentMethod: "QRPH",
      siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
      siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
      vendorSystemId: "45a625de-9034-4fb6-b527-0950d384e51f",
      tariffSnapshotId: "66666666-6666-6666-6666-666666666666",
      expectedAmountMinorUnits: 12500
    });
  });

  it("WebPayReturnPage_LoadsStatusByTicketReference", async () => {
    const fetchMock = stubWebPayFetch();
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-FRESH-008");

    render(<App />);

    expect(screen.getAllByText("Checking payment status").length).toBeGreaterThan(0);
    expect(await screen.findByText("Parking Session Summary")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toContain("/v1/webpay/parking-session");
    const body = JSON.parse((fetchMock.mock.calls[0][1] as RequestInit).body as string);
    expect(body.ticketReference).toBe("WEBPAY-20260523-FRESH-008");
  });

  it("WebPayReturnPage_DoesNotMarkPaidOnlyBecauseOfRedirect", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Not Started",
        parkingStatus: "PaymentRequired"
      }
    });
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-FRESH-009&result=success");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /payment is still being verified/i })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /payment confirmed/i })).not.toBeInTheDocument();
    expect(screen.getByText("PaymentRequired")).toBeInTheDocument();
  });

  it("WebPayReturnPage_ShowsConfirmedReceiptAfterBackendConfirms", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Paid",
        parkingStatus: "PaymentRequired"
      }
    });
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-FRESH-010");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /payment confirmed/i })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /payment receipt/i })).toBeInTheDocument();
    expect(screen.getByText("Amount Paid")).toBeInTheDocument();
    expect(screen.getAllByText("PHP 125.00").length).toBeGreaterThan(0);
    expect(screen.getByText("Payment Completed")).toBeInTheDocument();
  });

  it("WebPayReturnPage_WhenPaymentFailed_ShowsDeterministicFailure", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Failed"
      }
    });
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-FAILED");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /payment failed/i })).toBeInTheDocument();
    expect(screen.getByText(/could not confirm this payment/i)).toBeInTheDocument();
  });

  it("WebPayReturnPage_WhenPaymentExpired_ShowsDeterministicExpiry", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Expired"
      }
    });
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-EXPIRED");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /payment expired/i })).toBeInTheDocument();
    expect(screen.getByText(/checkout session expired/i)).toBeInTheDocument();
  });

  it("WebPayReturnPage_WhenConfirmedWithExitInstruction_RendersExitGuidance", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Paid",
        exitInstruction: {
          status: "ISSUED",
          message: "Proceed to the exit lane and present your ticket.",
          laneName: "Main Exit",
          exitBy: "2026-05-23T13:15:00+08:00"
        },
        providerRawPayload: {
          secret: "do-not-render"
        }
      }
    });
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-PAID");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /proceed to exit/i })).toBeInTheDocument();
    expect(screen.getByText("Proceed to the exit lane and present your ticket.")).toBeInTheDocument();
    expect(screen.getByText("Lane: Main Exit")).toBeInTheDocument();
    expect(screen.queryByText(/do-not-render/i)).not.toBeInTheDocument();
  });

  it("WebPayReturnPage_WhenConfirmedWithoutExitAuthorization_RendersPreparingState", async () => {
    stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        paymentStatus: "Paid",
        exitInstruction: null,
        exitAuthorizationStatus: null
      }
    });
    window.history.pushState({}, "", "/webpay/payment-return?ticketReference=WEBPAY-20260523-PREPARING");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /preparing exit authorization/i })).toBeInTheDocument();
    expect(screen.getByText(/check status again shortly/i)).toBeInTheDocument();
  });

  it("WebPayCancelledPage_AllowsRetryAndStatusRefresh", async () => {
    const fetchMock = stubWebPayFetch({
      resolvePayload: {
        ...successResponse,
        ticketReference: "WEBPAY-20260523-FRESH-011",
        paymentStatus: "Not Started",
        parkingStatus: "PaymentRequired"
      }
    });
    window.history.pushState({}, "", "/webpay/payment-cancelled?ticketReference=WEBPAY-20260523-FRESH-011");

    render(<App />);

    expect(await screen.findByRole("heading", { name: /payment was cancelled/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /check status/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /retry payment/i })).toHaveAttribute(
      "href",
      "/?ticketReference=WEBPAY-20260523-FRESH-011"
    );

    await userEvent.click(screen.getByRole("button", { name: /check status/i }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
  });
});
