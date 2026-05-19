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
  paymentMethod: "QRPH",
  selectedProviderCode: "AUB",
  fallbackProviderCode: "PAYMONGO",
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
}) {
  const fetchMock = vi.fn(async (url: string, _init?: RequestInit) => {
    const isResolve = url.includes("/v1/webpay/parking-session");
    return {
      ok: isResolve ? options?.resolveOk ?? true : options?.intentOk ?? true,
      status: isResolve ? options?.resolveStatus ?? 200 : options?.intentStatus ?? 200,
      json: async () => (isResolve ? options?.resolvePayload ?? successResponse : options?.intentPayload ?? successResponse)
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
});

describe("ExitPass WebPay UI", () => {
  async function resolveTicket(ticketReference = "TICKET-001") {
    await userEvent.type(screen.getByLabelText(/ticket reference/i), ticketReference);
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));
    await screen.findByText("125.00");
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
    expect(screen.getByText("125.00")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toContain("/v1/webpay/parking-session");
  });

  it("WebPay_WhenSummaryResolved_ContinueToPaymentCreatesPaymentIntent", async () => {
    const fetchMock = stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-TEST-027");
    await continueToPayment();

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(fetchMock.mock.calls[0][0]).toContain("/v1/webpay/parking-session");
    expect(fetchMock.mock.calls[1][0]).toContain("/v1/webpay/payment-intents");
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
    expect(screen.getByText("PHP")).toBeInTheDocument();
    expect(screen.getAllByText("PENDING_PROVIDER").length).toBeGreaterThan(0);
    expect(screen.getByText("125.00")).toBeInTheDocument();
  });

  it("WebPay_WhenApiReturnsSessionSummary_DisplaysParkingSessionSummary", async () => {
    stubWebPayFetch();

    render(<App />);

    await resolveTicket("TICKET-TEST-023");

    expect(await screen.findByRole("heading", { name: /mactan newtown parking/i })).toBeInTheDocument();
    expect(screen.getByText("Parking Session Summary")).toBeInTheDocument();
    expect(screen.getByText("TICKET-TEST-023")).toBeInTheDocument();
    expect(screen.getByText("ABC 1234")).toBeInTheDocument();
    expect(screen.getByText("Weekend Rate")).toBeInTheDocument();
    expect(screen.getByText("125.00")).toBeInTheDocument();
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

    expect(await screen.findByText("Payment Status")).toBeInTheDocument();
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
  });

  it("WebPay_WhenPlateEntered_SubmitsPlateNumber", async () => {
    const fetchMock = stubWebPayFetch({ intentPayload: { ...successResponse, paymentMethod: "GCASH" } });

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /plate/i }));
    await userEvent.type(screen.getByLabelText(/plate number/i), "abc 1234");
    await userEvent.click(screen.getByLabelText(/GCash/i));
    await userEvent.click(screen.getByRole("button", { name: /^continue$/i }));
    await screen.findByText("Parking Session Summary");
    await continueToPayment();

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    const intentCall = fetchMock.mock.calls[1];
    expect(intentCall).toBeDefined();
    const body = JSON.parse((intentCall?.[1] as RequestInit).body as string);
    expect(body).toEqual({
      plateNumber: "ABC 1234",
      paymentMethod: "GCASH",
      siteGroupId: "11111111-1111-1111-1111-111111111111",
      siteId: "22222222-2222-2222-2222-222222222222",
      vendorSystemId: "HIKCENTRAL"
    });
  });
});
