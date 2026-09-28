/** Every error the SDK throws is a MailhiveError. API errors carry the
 * HTTP status, Mailhive's stable error `code` and the `requestId` to quote
 * to support. */
export class MailhiveError extends Error {
  override name = "MailhiveError";
}

export interface ApiErrorInit {
  status: number;
  code: string;
  message: string;
  details?: unknown;
  requestId?: string | null;
  headers?: Headers;
}

/** An error response from the API. Check `code`, not `message`. */
export class ApiError extends MailhiveError {
  override name = "ApiError";
  readonly status: number;
  readonly code: string;
  readonly details: unknown;
  readonly requestId: string | null;
  readonly headers: Headers | undefined;

  constructor(init: ApiErrorInit) {
    super(init.message);
    this.status = init.status;
    this.code = init.code;
    this.details = init.details ?? null;
    this.requestId = init.requestId ?? null;
    this.headers = init.headers;
  }
}

/** 401: missing, unknown or revoked API key. */
export class AuthenticationError extends ApiError {
  override name = "AuthenticationError";
}
/** 402: Mailhive Send is paused over an unpaid invoice. */
export class BillingError extends ApiError {
  override name = "BillingError";
}
/** 403: Send isn't activated, or the stream is paused. */
export class PermissionError extends ApiError {
  override name = "PermissionError";
}
/** 404 */
export class NotFoundError extends ApiError {
  override name = "NotFoundError";
}
/** 409: e.g. an Idempotency-Key reused for a different request. */
export class ConflictError extends ApiError {
  override name = "ConflictError";
}
/** 422: the request is invalid; `details` lists every problem. */
export class ValidationError extends ApiError {
  override name = "ValidationError";
}
/** 429: `rate_limited` (retried for you), or `monthly_quota_reached` /
 * `daily_cap_reached` (not retried: waiting won't help). */
export class RateLimitError extends ApiError {
  override name = "RateLimitError";
  /** Seconds to wait, from the Retry-After header, if given. */
  get retryAfter(): number | null {
    const value = this.headers?.get("retry-after");
    return value == null || value === "" || Number.isNaN(Number(value)) ? null : Number(value);
  }
}

/** The API couldn't be reached, or didn't answer in time. */
export class ConnectionError extends MailhiveError {
  override name = "ConnectionError";
  constructor(message: string, options?: { cause?: unknown }) {
    super(message, options);
  }
}

/** A webhook's signature didn't check out. `reason` says why. */
export class WebhookVerificationError extends MailhiveError {
  override name = "WebhookVerificationError";
  constructor(
    message: string,
    readonly reason: "header" | "timestamp" | "signature",
  ) {
    super(message);
  }
}

export function errorFor(init: ApiErrorInit): ApiError {
  switch (init.status) {
    case 401:
      return new AuthenticationError(init);
    case 402:
      return new BillingError(init);
    case 403:
      return new PermissionError(init);
    case 404:
      return new NotFoundError(init);
    case 409:
      return new ConflictError(init);
    case 400:
    case 422:
      return new ValidationError(init);
    case 429:
      return new RateLimitError(init);
    default:
      return new ApiError(init);
  }
}
