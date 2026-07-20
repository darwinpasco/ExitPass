import type { CentralPmsApiClient, ManagementPlatformUiError, ApiRequestOptions } from "./types";

const forbiddenHeaderNames = new Set(["authorization", "proxy-authorization", "cookie", "set-cookie"]);

export function createCentralPmsApiClient(options: { basePath?: string; fetchImpl?: typeof fetch } = {}): CentralPmsApiClient {
  const basePath = normalizeBasePath(options.basePath ?? "");
  const fetchImpl = options.fetchImpl ?? fetch;

  return {
    async request<TResponse>(path: string, requestOptions: ApiRequestOptions = {}): Promise<TResponse> {
      const method = requestOptions.method ?? "GET";
      const url = toCentralPmsPath(basePath, path);
      const correlationId = requestOptions.correlationId ?? createCorrelationId();
      const headers = new Headers();
      headers.set("X-Correlation-Id", correlationId);

      let body: BodyInit | undefined;
      if (requestOptions.body !== undefined) {
        headers.set("Content-Type", "application/json");
        body = JSON.stringify(requestOptions.body);
      }

      if (requestOptions.headers) {
        for (const [name, value] of new Headers(requestOptions.headers).entries()) {
          assertBrowserSafeHeaderName(name, correlationId);
          headers.set(name, value);
        }
      }

      for (const headerName of headers.keys()) {
        assertBrowserSafeHeaderName(headerName, correlationId);
      }

      let response: Response;
      try {
        response = await fetchImpl(url, {
          method,
          headers,
          body,
          signal: requestOptions.signal,
          credentials: "same-origin"
        });
      } catch (error) {
        if (isAbortError(error)) {
          throw createUiError("timeout", "MANAGEMENT_PLATFORM_REQUEST_CANCELLED", "The request was cancelled safely.", correlationId, undefined, false, method !== "GET");
        }

        throw createUiError("integration-unavailable", "MANAGEMENT_PLATFORM_API_UNAVAILABLE", "Central PMS is unavailable.", correlationId, undefined, true, method !== "GET");
      }

      const responseCorrelationId = response.headers.get("X-Correlation-Id") ?? correlationId;
      if (response.status === 204) {
        return undefined as TResponse;
      }

      const text = await response.text();
      const parsed = parseResponseBody(text, responseCorrelationId);

      if (!response.ok) {
        throw mapErrorResponse(response.status, parsed, responseCorrelationId, method !== "GET");
      }

      return parsed as TResponse;
    }
  };
}

export function createCorrelationId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `mp-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

export function toCentralPmsPath(basePath: string, path: string): string {
  if (!path.startsWith("/")) {
    throw createUiError("validation", "MANAGEMENT_PLATFORM_RELATIVE_PATH_REQUIRED", "Central PMS API paths must be relative.");
  }

  if (/^\/\//.test(path) || /^https?:\/\//i.test(path)) {
    throw createUiError("validation", "MANAGEMENT_PLATFORM_UNSUPPORTED_API_PATH", "The browser can call only Central PMS Management Platform routes.");
  }

  if (!path.startsWith("/v1/management-platform")) {
    throw createUiError("validation", "MANAGEMENT_PLATFORM_ROUTE_REQUIRED", "The browser can call only Management Platform API routes.");
  }

  const normalizedBasePath = normalizeBasePath(basePath);
  return `${normalizedBasePath}${path}`;
}

export function assertBrowserSafeHeaderName(headerName: string, correlationId?: string): void {
  const normalizedName = normalizeHeaderName(headerName);
  if (forbiddenHeaderNames.has(normalizedName) || normalizedName.startsWith("x-posserver-")) {
    throw createUiError("unknown", "MANAGEMENT_PLATFORM_FORBIDDEN_HEADER", "The request contains an unsupported browser header.", correlationId);
  }
}

export function mapErrorResponse(status: number, body: unknown, correlationId: string, mutation: boolean): ManagementPlatformUiError {
  const safeCode = readSafeString(body, "code") ?? readSafeString(body, "errorCode") ?? `HTTP_${status}`;
  const serverMessage = readSafeString(body, "message");

  if (safeCode === "SALES_INVOICE_PROFILE_ADMINISTRATION_DISABLED") {
    return createUiError("feature-disabled", safeCode, "This administrative feature is not enabled for this environment.", correlationId, status, false, false);
  }

  switch (status) {
    case 400:
    case 422:
      return createUiError("validation", safeCode, serverMessage ?? "Review the submitted fields.", correlationId, status, false, false);
    case 401:
      return createUiError("authentication-required", safeCode, "Authentication is required or expired.", correlationId, status, false, false);
    case 403:
      return createUiError("permission-denied", safeCode, "You do not have permission for this Management Platform action.", correlationId, status, false, false);
    case 404:
      return createUiError("not-found", safeCode, "The requested resource is unavailable.", correlationId, status, false, false);
    case 409:
      return createUiError("conflict", safeCode, "The requested change conflicts with the authoritative state.", correlationId, status, false, false);
    case 429:
      return createUiError("throttled", safeCode, "The service is temporarily throttled.", correlationId, status, true, mutation);
    case 502:
    case 503:
      return createUiError("integration-unavailable", safeCode, "Central PMS or its downstream administration boundary is unavailable.", correlationId, status, true, mutation);
    case 504:
      return createUiError("timeout", safeCode, mutation ? "The request timed out. Refresh and verify the authoritative state before trying again." : "The request timed out.", correlationId, status, true, mutation);
    default:
      return createUiError("unknown", safeCode, "The request failed safely.", correlationId, status, false, mutation);
  }
}

export function createUiError(
  kind: ManagementPlatformUiError["kind"],
  code: string,
  message: string,
  correlationId?: string,
  httpStatus?: number,
  retryable = false,
  mutationUncertain = false
): ManagementPlatformUiError {
  return { kind, code, message, correlationId, httpStatus, retryable, mutationUncertain };
}

function normalizeBasePath(basePath: string): string {
  const trimmed = basePath.trim().replace(/\/$/, "");
  if (/^https?:\/\//i.test(trimmed)) {
    throw createUiError("validation", "MANAGEMENT_PLATFORM_RELATIVE_BASE_REQUIRED", "Central PMS API base path must be relative.");
  }

  return trimmed;
}

function normalizeHeaderName(headerName: string): string {
  return headerName.trim().toLowerCase();
}

function parseResponseBody(text: string, correlationId: string): unknown {
  if (!text.trim()) {
    return {};
  }

  try {
    return JSON.parse(text) as unknown;
  } catch {
    throw createUiError("malformed-response", "MANAGEMENT_PLATFORM_MALFORMED_RESPONSE", "The server response could not be read safely.", correlationId);
  }
}

function readSafeString(value: unknown, propertyName: string): string | undefined {
  if (typeof value !== "object" || value === null || !(propertyName in value)) {
    return undefined;
  }

  const candidate = (value as Record<string, unknown>)[propertyName];
  return typeof candidate === "string" && candidate.trim() ? candidate : undefined;
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === "AbortError";
}
