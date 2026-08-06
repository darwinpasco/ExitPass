import type {
  ApiError,
  WebPayStatutoryEvidenceChannelResponse,
  WebPayStatutoryEvidenceUploadSessionResponse
} from "./types";
import { createCorrelationId, getApiBaseUrl } from "./webpay";

const evidenceBasePath = "/v1/webpay/statutory-discounts/evidence";
const supportedImageTypes = new Set(["image/jpeg", "image/png"]);

export type StatutoryEvidenceUploadProgress = {
  loaded: number;
  total: number;
  percent: number | null;
};

export class StatutoryEvidenceError extends Error {
  public readonly errorCode?: string;
  public readonly retryable: boolean;
  public readonly correlationId?: string;

  public constructor(errorCode: string | undefined, message: string, retryable: boolean, correlationId?: string) {
    super(message);
    this.name = "StatutoryEvidenceError";
    this.errorCode = errorCode;
    this.retryable = retryable;
    this.correlationId = correlationId;
  }
}

function jsonHeaders(correlationId: string): HeadersInit {
  return {
    "Content-Type": "application/json",
    "X-Correlation-Id": correlationId
  };
}

async function readJson<T>(response: Response): Promise<T> {
  const payload = (await response.json().catch(() => ({}))) as T | ApiError;
  if (!response.ok) {
    const error = payload as ApiError;
    throw new StatutoryEvidenceError(
      error.errorCode,
      toSafeEvidenceMessage(error.errorCode, error.message),
      Boolean(error.retryable),
      error.correlationId
    );
  }

  return payload as T;
}

export async function bootstrapStatutoryEvidence(
  statutoryDiscountDecisionCommandId: string,
  fetchImpl: typeof fetch = fetch,
  signal?: AbortSignal
): Promise<WebPayStatutoryEvidenceChannelResponse> {
  const correlationId = createCorrelationId();
  const response = await fetchImpl(`${getApiBaseUrl()}${evidenceBasePath}/bootstrap`, {
    method: "POST",
    headers: jsonHeaders(correlationId),
    body: JSON.stringify({
      statutoryDiscountDecisionCommandId: statutoryDiscountDecisionCommandId.trim(),
      clientOperationKey: `webpay-evidence-bootstrap:${statutoryDiscountDecisionCommandId.trim()}`
    }),
    signal
  });

  return ensureChannelResponse(await readJson<unknown>(response));
}

export async function retrieveStatutoryEvidenceStatus(
  lookup: { statutoryDiscountDecisionCommandId?: string; evidenceSetReference?: string },
  fetchImpl: typeof fetch = fetch,
  signal?: AbortSignal
): Promise<WebPayStatutoryEvidenceChannelResponse> {
  const decisionId = lookup.statutoryDiscountDecisionCommandId?.trim();
  const setReference = lookup.evidenceSetReference?.trim();
  if (Boolean(decisionId) === Boolean(setReference)) {
    throw new Error("Evidence status requires exactly one authoritative lookup reference.");
  }

  const query = decisionId
    ? `statutoryDiscountDecisionCommandId=${encodeURIComponent(decisionId)}`
    : `evidenceSetReference=${encodeURIComponent(setReference ?? "")}`;
  const correlationId = createCorrelationId();
  const response = await fetchImpl(`${getApiBaseUrl()}${evidenceBasePath}/status?${query}`, {
    method: "GET",
    headers: { "X-Correlation-Id": correlationId },
    signal
  });

  return ensureChannelResponse(await readJson<unknown>(response));
}

export async function computeSha256(file: File): Promise<string> {
  const digest = await globalThis.crypto.subtle.digest("SHA-256", await file.arrayBuffer());
  return Array.from(new Uint8Array(digest), (value) => value.toString(16).padStart(2, "0")).join("");
}

export async function requestStatutoryEvidenceUploadSession(
  channel: WebPayStatutoryEvidenceChannelResponse,
  file: File,
  checksumSha256: string,
  fetchImpl: typeof fetch = fetch
): Promise<WebPayStatutoryEvidenceUploadSessionResponse> {
  if (!channel.evidenceSetReference || !channel.evidenceItemReference) {
    throw new Error("Evidence capture is not ready. Refresh the evidence status.");
  }

  const correlationId = createCorrelationId();
  const response = await fetchImpl(`${getApiBaseUrl()}${evidenceBasePath}/upload-sessions`, {
    method: "POST",
    headers: jsonHeaders(correlationId),
    body: JSON.stringify({
      evidenceSetReference: channel.evidenceSetReference,
      evidenceItemReference: channel.evidenceItemReference,
      declaredContentType: file.type,
      declaredContentLength: file.size,
      declaredChecksumSha256: checksumSha256,
      clientOperationKey: `webpay-evidence-upload:${channel.evidenceItemReference}:${createCorrelationId()}`
    })
  });

  return ensureUploadSessionResponse(await readJson<unknown>(response));
}

export function uploadStatutoryEvidence(
  opaqueUploadSessionReference: string,
  file: File,
  onProgress: (progress: StatutoryEvidenceUploadProgress) => void,
  signal?: AbortSignal,
  xhrFactory: () => XMLHttpRequest = () => new XMLHttpRequest()
): Promise<void> {
  return new Promise((resolve, reject) => {
    const xhr = xhrFactory();
    const correlationId = createCorrelationId();
    xhr.open("PUT", `${getApiBaseUrl()}${evidenceBasePath}/upload-sessions/${encodeURIComponent(opaqueUploadSessionReference)}`);
    xhr.setRequestHeader("Content-Type", file.type);
    xhr.setRequestHeader("X-Correlation-Id", correlationId);
    xhr.upload.onprogress = (event) => {
      const total = event.lengthComputable ? event.total : file.size;
      onProgress({ loaded: event.loaded, total, percent: total > 0 ? Math.round((event.loaded / total) * 100) : null });
    };
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        onProgress({ loaded: file.size, total: file.size, percent: 100 });
        resolve();
        return;
      }

      const payload = safeParseError(xhr.responseText);
      reject(new StatutoryEvidenceError(
        payload.errorCode,
        toSafeEvidenceMessage(payload.errorCode, payload.message),
        Boolean(payload.retryable),
        payload.correlationId
      ));
    };
    xhr.onerror = () => reject(new StatutoryEvidenceError(
      "WEBPAY_STATUTORY_EVIDENCE_TEMPORARILY_UNAVAILABLE",
      "The photo upload was interrupted. Please try again.",
      true,
      correlationId
    ));
    xhr.onabort = () => reject(new DOMException("The photo upload was cancelled.", "AbortError"));
    signal?.addEventListener("abort", () => xhr.abort(), { once: true });
    xhr.send(file);
  });
}

export async function finalizeStatutoryEvidenceUpload(
  opaqueUploadSessionReference: string,
  fetchImpl: typeof fetch = fetch
): Promise<WebPayStatutoryEvidenceChannelResponse> {
  const correlationId = createCorrelationId();
  const response = await fetchImpl(
    `${getApiBaseUrl()}${evidenceBasePath}/upload-sessions/${encodeURIComponent(opaqueUploadSessionReference)}/finalize`,
    {
      method: "POST",
      headers: jsonHeaders(correlationId),
      body: JSON.stringify({ clientOperationKey: `webpay-evidence-finalize:${opaqueUploadSessionReference}` })
    }
  );

  return ensureChannelResponse(await readJson<unknown>(response));
}

export function validateStatutoryEvidenceFile(
  file: File | null,
  channel: Pick<WebPayStatutoryEvidenceChannelResponse, "allowedContentTypes" | "maximumContentLengthBytes">
): string | null {
  if (!file) {
    return "Choose one photo to upload.";
  }
  if (file.size <= 0) {
    return "The selected photo is empty. Choose a JPEG or PNG image.";
  }
  if (!supportedImageTypes.has(file.type) || !channel.allowedContentTypes.includes(file.type)) {
    return "Choose a JPEG or PNG image. PDF files are not accepted.";
  }
  if (channel.maximumContentLengthBytes > 0 && file.size > channel.maximumContentLengthBytes) {
    return `The selected photo is too large. Choose a file smaller than ${formatBytes(channel.maximumContentLengthBytes)}.`;
  }
  return null;
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024 * 1024) {
    return `${Math.max(1, Math.ceil(bytes / 1024))} KB`;
  }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function safeParseError(raw: string): ApiError {
  try {
    return JSON.parse(raw) as ApiError;
  } catch {
    return {};
  }
}

function ensureChannelResponse(value: unknown): WebPayStatutoryEvidenceChannelResponse {
  if (!isRecord(value) ||
    typeof value.classification !== "string" ||
    typeof value.retryable !== "boolean" ||
    typeof value.correlationId !== "string" ||
    typeof value.evidenceRequired !== "boolean" ||
    !Array.isArray(value.allowedContentTypes) ||
    value.allowedContentTypes.some((item) => typeof item !== "string") ||
    typeof value.maximumContentLengthBytes !== "number" ||
    typeof value.lifecycleClassification !== "string" ||
    typeof value.replacementPosture !== "string" ||
    typeof value.readyForReview !== "boolean") {
    throw new StatutoryEvidenceError(
      "WEBPAY_STATUTORY_EVIDENCE_MALFORMED_RESPONSE",
      "Evidence status is temporarily unavailable. Please refresh and try again.",
      true
    );
  }

  return value as WebPayStatutoryEvidenceChannelResponse;
}

function ensureUploadSessionResponse(value: unknown): WebPayStatutoryEvidenceUploadSessionResponse {
  if (!isRecord(value) ||
    typeof value.classification !== "string" ||
    typeof value.retryable !== "boolean" ||
    typeof value.correlationId !== "string" ||
    typeof value.opaqueUploadSessionReference !== "string" ||
    value.method !== "PUT" ||
    typeof value.maximumContentLengthBytes !== "number") {
    throw new StatutoryEvidenceError(
      "WEBPAY_STATUTORY_EVIDENCE_MALFORMED_RESPONSE",
      "Evidence upload authorization was incomplete. Refresh and try again.",
      true
    );
  }

  return value as WebPayStatutoryEvidenceUploadSessionResponse;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}

function toSafeEvidenceMessage(errorCode?: string, _serverMessage?: string): string {
  switch ((errorCode ?? "").toUpperCase()) {
    case "WEBPAY_STATUTORY_EVIDENCE_FILE_TYPE_INVALID":
    case "WEBPAY_STATUTORY_EVIDENCE_FILE_INVALID":
      return "Choose a JPEG or PNG image. PDF files are not accepted.";
    case "WEBPAY_STATUTORY_EVIDENCE_FILE_TOO_LARGE":
    case "WEBPAY_STATUTORY_EVIDENCE_FILE_SIZE_INVALID":
      return "The selected photo is too large. Choose a smaller JPEG or PNG image.";
    case "WEBPAY_STATUTORY_EVIDENCE_UPLOAD_EXPIRED":
      return "The photo upload expired. Request a new upload and try again.";
    case "WEBPAY_STATUTORY_EVIDENCE_CONFLICT":
      return "The evidence status changed. Refresh the status before trying again.";
    case "WEBPAY_STATUTORY_EVIDENCE_SERVICE_UNAVAILABLE":
      return "Evidence upload is temporarily unavailable. Please try again later or ask a parking attendant for assistance.";
    case "WEBPAY_STATUTORY_EVIDENCE_TEMPORARILY_UNAVAILABLE":
      return "We could not process the photo right now. Please try again.";
    case "WEBPAY_STATUTORY_EVIDENCE_FILE_VERIFICATION_FAILED":
      return "The uploaded photo could not be verified. Choose the photo again.";
    case "WEBPAY_STATUTORY_EVIDENCE_CONTEXT_NOT_FOUND":
      return "The evidence request could not be found. Refresh the statutory request status.";
    default:
      return "Evidence could not be processed safely. Please refresh and try again.";
  }
}
