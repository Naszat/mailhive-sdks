import { ConnectionError, MailhiveError, errorFor, type ApiErrorInit } from "./errors.js";
import { Emails } from "./resources/emails.js";
import { VERSION } from "./version.js";
import { verifyWebhook, type VerifyWebhookOptions } from "./webhooks.js";

export const DEFAULT_BASE_URL = "https://api.mailhive.africa/v1";

export interface MailhiveOptions {
  /** Defaults to the MAILHIVE_API_KEY environment variable. */
  apiKey?: string;
  /** Defaults to MAILHIVE_BASE_URL, then https://api.mailhive.africa/v1. */
  baseUrl?: string;
  /** Per-attempt timeout in milliseconds. Default 30,000. */
  timeout?: number;
  /** Retries after a network error, a 5xx or a 429 rate_limited. Default 2. */
  maxRetries?: number;
  /** A fetch implementation, for runtimes without a global one or for tests. */
  fetch?: typeof fetch;
}

interface RequestInit {
  body?: unknown;
  idempotencyKey?: string;
  signal?: AbortSignal;
}

const MAX_RETRY_AFTER_SECONDS = 60;

function env(name: string): string | undefined {
  // Not every runtime has `process` (Deno, Workers, browsers).
  return typeof process !== "undefined" ? process.env?.[name] : undefined;
}

function runtime(): string {
  if (typeof process !== "undefined" && process.versions?.node) return `node/${process.versions.node}`;
  return "js";
}

function looksLikeBrowser(): boolean {
  return typeof window !== "undefined" && typeof document !== "undefined";
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) return reject(signal.reason);
    const timer = setTimeout(resolve, ms);
    signal?.addEventListener(
      "abort",
      () => {
        clearTimeout(timer);
        reject(signal.reason);
      },
      { once: true },
    );
  });
}

/** Exponential backoff with jitter: about 0.5s, 1s, 2s … up to 8s. */
function backoff(attempt: number): number {
  const ceiling = Math.min(8000, 500 * 2 ** attempt);
  return ceiling / 2 + Math.random() * (ceiling / 2);
}

function retryAfterMs(headers: Headers): number | null {
  const value = headers.get("retry-after");
  if (value == null || value.trim() === "") return null;
  const seconds = Number(value);
  return Number.isFinite(seconds) && seconds >= 0 ? seconds * 1000 : null;
}

export class Mailhive {
  readonly emails: Emails;
  readonly webhooks = {
    /** Checks a webhook's `Mailhive-Signature` and returns the parsed event. */
    verify: (options: VerifyWebhookOptions) => verifyWebhook(options),
  };

  readonly #apiKey: string;
  readonly #baseUrl: string;
  readonly #timeout: number;
  readonly #maxRetries: number;
  readonly #fetch: typeof fetch;

  constructor(apiKeyOrOptions?: string | MailhiveOptions, options: Omit<MailhiveOptions, "apiKey"> = {}) {
    const opts: MailhiveOptions =
      typeof apiKeyOrOptions === "string" ? { ...options, apiKey: apiKeyOrOptions } : { ...options, ...apiKeyOrOptions };
    const apiKey = opts.apiKey ?? env("MAILHIVE_API_KEY");
    if (!apiKey) {
      throw new MailhiveError(
        "No API key. Pass one to new Mailhive(…) or set MAILHIVE_API_KEY. Create keys under Mailhive Send → API keys.",
      );
    }
    if (apiKey.startsWith("mhs_") && looksLikeBrowser()) {
      // A secret key in a web page can be read by anyone who opens it.
      throw new MailhiveError(
        "secret_key_in_browser: Mailhive Send API keys (mhs_…) must only be used on a server. " +
          "Anyone can read a key shipped to a browser. Call the API from your backend instead.",
      );
    }
    this.#apiKey = apiKey;
    this.#baseUrl = (opts.baseUrl ?? env("MAILHIVE_BASE_URL") ?? DEFAULT_BASE_URL).replace(/\/+$/, "");
    this.#timeout = opts.timeout ?? 30_000;
    this.#maxRetries = Math.max(0, opts.maxRetries ?? 2);
    const fetchImpl = opts.fetch ?? globalThis.fetch;
    if (typeof fetchImpl !== "function") {
      throw new MailhiveError("No fetch implementation found. Use Node 20 or later, or pass one as options.fetch.");
    }
    this.#fetch = fetchImpl.bind(globalThis);
    this.emails = new Emails(this);
  }

  /** @internal Used by the resources. */
  async request<T>(method: "GET" | "POST", path: string, init: RequestInit = {}): Promise<T> {
    // One key per call, reused on every retry: a retry never sends twice.
    const idempotencyKey = method === "POST" ? (init.idempotencyKey ?? crypto.randomUUID()) : undefined;
    const headers: Record<string, string> = {
      Authorization: `Bearer ${this.#apiKey}`,
      Accept: "application/json",
      "User-Agent": `mailhive-node/${VERSION} ${runtime()}`,
    };
    if (init.body !== undefined) headers["Content-Type"] = "application/json";
    if (idempotencyKey) headers["Idempotency-Key"] = idempotencyKey;
    const body = init.body === undefined ? undefined : JSON.stringify(init.body);
    const url = `${this.#baseUrl}${path}`;

    for (let attempt = 0; ; attempt++) {
      const canRetry = attempt < this.#maxRetries;
      const timeout = AbortSignal.timeout(this.#timeout);
      const signal = init.signal ? AbortSignal.any([init.signal, timeout]) : timeout;

      let response: Response;
      try {
        response = await this.#fetch(url, { method, headers, body, signal });
      } catch (cause) {
        if (init.signal?.aborted) throw init.signal.reason ?? cause;
        if (canRetry) {
          await sleep(backoff(attempt), init.signal);
          continue;
        }
        throw new ConnectionError(
          timeout.aborted
            ? `The Mailhive API didn't answer within ${this.#timeout} ms.`
            : `Couldn't reach the Mailhive API at ${this.#baseUrl}.`,
          { cause },
        );
      }

      const text = await response.text();
      let data: unknown = null;
      try {
        data = text ? JSON.parse(text) : null;
      } catch {
        data = null;
      }
      if (response.ok) return data as T;

      const error = (data as { error?: Partial<ApiErrorInit> & { request_id?: string } } | null)?.error;
      const init_: ApiErrorInit = {
        status: response.status,
        code: typeof error?.code === "string" ? error.code : "http_error",
        message: typeof error?.message === "string" ? error.message : `HTTP ${response.status} ${response.statusText}`.trim(),
        details: error?.details ?? null,
        requestId: error?.request_id ?? response.headers.get("x-request-id"),
        headers: response.headers,
      };
      // Only a rate limit and server errors are worth retrying: a used-up
      // allowance or a bad request fails the same way again.
      const retryable = response.status >= 500 || (response.status === 429 && init_.code === "rate_limited");
      if (retryable && canRetry) {
        const wait = retryAfterMs(response.headers);
        if (wait === null || wait <= MAX_RETRY_AFTER_SECONDS * 1000) {
          await sleep(wait ?? backoff(attempt), init.signal);
          continue;
        }
      }
      throw errorFor(init_);
    }
  }
}
