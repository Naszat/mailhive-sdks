import { FormError, type FieldError } from "./errors.js";
import { solve, type Challenge } from "./pow.js";

// What the form's anti-spam check is, from its challenge endpoint. Older
// servers sent only the proof-of-work fields: that means "pow".
type ChallengeInfo = (Challenge & { mode?: "pow" }) | { mode: "turnstile"; site_key: string | null } | { mode: "none" };
const TURNSTILE_FIELD = "cf-turnstile-response";

export const DEFAULT_BASE_URL = "https://api.mailhive.africa/v1";

// The server quietly drops a submission sent sooner than this after its
// challenge was issued (people take longer than that to fill in a form).
// Waiting here means a fast script or an auto-filled form isn't mistaken
// for a bot.
const MIN_AGE_MS = 2500;
// Challenges last five minutes; fetch a new one well before that.
const MAX_AGE_MS = 4 * 60 * 1000;

export interface FormOptions {
  /** Default https://api.mailhive.africa/v1. */
  baseUrl?: string;
  fetch?: typeof fetch;
  /** @internal For tests: how long a challenge must age before submitting. */
  minAgeMs?: number;
}

export interface SubmitOptions {
  signal?: AbortSignal;
  /** For signed-in user forms: the user's ID token from your auth provider
   * (Firebase `user.getIdToken()`, Supabase `session.access_token`, Clerk
   * `getToken()`, Auth0 `getIdTokenClaims().__raw`). */
  idToken?: string;
  /** For forms using Cloudflare Turnstile, if the token isn't already in
   * the values as `cf-turnstile-response` (the widget adds it to forms). */
  turnstileToken?: string;
}

export interface Submitted {
  /** The submission's id, for support. */
  id: string;
  /** Where the form's settings say to send the visitor next, if anywhere. */
  redirectUrl: string | null;
}

export type Values = Record<string, string | number | boolean | null | undefined>;

interface Prepared {
  challenge: Promise<ChallengeInfo>;
  fetchedAt: number;
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) return reject(signal.reason);
    const timer = setTimeout(resolve, ms);
    signal?.addEventListener("abort", () => (clearTimeout(timer), reject(signal.reason)), { once: true });
  });
}

async function errorFrom(response: Response): Promise<FormError> {
  let body: { error?: { code?: string; message?: string; details?: unknown; request_id?: string } } | null = null;
  try {
    body = await response.json();
  } catch {
    body = null;
  }
  const error = body?.error;
  const details = error?.details;
  const fieldErrors: FieldError[] = Array.isArray(details)
    ? details.map((d: { field?: string | null; msg?: string }) => ({ field: d.field ?? null, message: d.msg ?? "" }))
    : [];
  const retryAfter = Number(response.headers.get("retry-after"));
  return new FormError({
    code: error?.code ?? "http_error",
    message: error?.message ?? "The form couldn't be sent. Please try again.",
    status: response.status,
    fieldErrors,
    requestId: error?.request_id ?? response.headers.get("x-request-id"),
    retryAfter: Number.isFinite(retryAfter) && response.headers.has("retry-after") ? retryAfter : null,
  });
}

export class MailhiveForm {
  readonly key: string;
  readonly #base: string;
  readonly #fetch: typeof fetch;
  readonly #minAgeMs: number;
  #prepared: Prepared | null = null;

  constructor(key: string, options: FormOptions = {}) {
    if (key.startsWith("mhs_")) {
      throw new Error(
        "That's a secret API key (mhs_…). Never put one in a web page: anyone can read it. Use the form's publishable key (mhp_…) from Mailhive Send → Forms.",
      );
    }
    if (!key.startsWith("mhp_")) throw new Error("A Mailhive form key starts with mhp_. Copy it from Mailhive Send → Forms.");
    this.key = key;
    this.#base = `${(options.baseUrl ?? DEFAULT_BASE_URL).replace(/\/+$/, "")}/send/client/${encodeURIComponent(key)}`;
    const fetchImpl = options.fetch ?? globalThis.fetch;
    this.#fetch = fetchImpl.bind(globalThis);
    this.#minAgeMs = options.minAgeMs ?? MIN_AGE_MS;
  }

  /** Fetches the anti-spam challenge ahead of time, e.g. when the visitor
   * starts filling in the form, so submitting feels instant. Optional. */
  prepare(): void {
    if (this.#prepared && Date.now() - this.#prepared.fetchedAt < MAX_AGE_MS) return;
    const challenge = this.#fetch(`${this.#base}/challenge`, { method: "POST" }).then(async (response) => {
      if (!response.ok) throw await errorFrom(response);
      return (await response.json()) as ChallengeInfo;
    });
    challenge.catch(() => {
      this.#prepared = null; // try again on the next prepare() or submit()
    });
    this.#prepared = { challenge, fetchedAt: Date.now() };
  }

  /** Sends the values. Resolves when Mailhive has accepted them; rejects
   * with a FormError saying what to fix or when to retry. */
  async submit(values: Values, options: SubmitOptions = {}): Promise<Submitted> {
    this.prepare();
    const prepared = this.#prepared!;
    this.#prepared = null; // a challenge solves one submission only
    let info: ChallengeInfo;
    try {
      info = await prepared.challenge;
    } catch (error) {
      if (error instanceof FormError) throw error;
      throw new FormError({ code: "network_error", message: "Couldn't reach the server. Check your connection and try again.", status: 0 });
    }

    const body: Record<string, unknown> = {};
    for (const [name, value] of Object.entries(values)) {
      if (value !== undefined && value !== null) body[name] = typeof value === "string" ? value : String(value);
    }
    const mode = info.mode ?? "pow";
    if (mode === "pow") {
      body._challenge = await solve(info as Challenge, options.signal);
      const age = Date.now() - prepared.fetchedAt;
      if (age < this.#minAgeMs) await sleep(this.#minAgeMs - age, options.signal);
    } else if (mode === "turnstile") {
      const token = options.turnstileToken ?? (typeof body[TURNSTILE_FIELD] === "string" ? (body[TURNSTILE_FIELD] as string) : "");
      if (!token) {
        throw new FormError({ code: "turnstile_missing", message: "Please complete the anti-spam check.", status: 0 });
      }
      body[TURNSTILE_FIELD] = token;
    }
    if (options.idToken) body._id_token = options.idToken;
    let response: Response;
    try {
      response = await this.#fetch(`${this.#base}/submit`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
        signal: options.signal,
      });
    } catch (error) {
      if (options.signal?.aborted) throw error;
      throw new FormError({ code: "network_error", message: "Couldn't reach the server. Check your connection and try again.", status: 0 });
    }
    if (!response.ok) throw await errorFrom(response);
    const data = (await response.json()) as { id: string; redirect_url: string | null };
    return { id: data.id, redirectUrl: data.redirect_url ?? null };
  }
}

/** A form on your page, by its publishable key (mhp_…). */
export function createForm(key: string, options?: FormOptions): MailhiveForm {
  return new MailhiveForm(key, options);
}
