import { WebhookVerificationError } from "./errors.js";
import type { WebhookEvent } from "./types.js";

export interface VerifyWebhookOptions {
  /** The raw request body, exactly as received — not parsed JSON. */
  payload: string | Uint8Array | ArrayBuffer;
  /** The `Mailhive-Signature` header. */
  signature: string | null | undefined;
  /** The endpoint's signing secret (`whsec_…`). */
  secret: string;
  /** How old (or far in the future) the timestamp may be. Default 300 seconds. */
  tolerance?: number;
  /** Unix seconds to verify at; for tests. */
  now?: number;
}

const encoder = new TextEncoder();

function toBytes(payload: VerifyWebhookOptions["payload"]): Uint8Array {
  if (typeof payload === "string") return encoder.encode(payload);
  if (payload instanceof Uint8Array) return payload;
  if (payload instanceof ArrayBuffer) return new Uint8Array(payload);
  throw new TypeError(
    "verifyWebhook needs the raw request body (a string or bytes), not parsed JSON: " +
      "re-serializing changes the bytes, so the signature can't match.",
  );
}

function parseHeader(header: string): { timestamp: number; signatures: string[] } | null {
  let timestamp: number | null = null;
  const signatures: string[] = [];
  for (const part of header.split(",")) {
    const index = part.indexOf("=");
    if (index < 0) continue;
    const key = part.slice(0, index).trim();
    const value = part.slice(index + 1).trim();
    if (key === "t" && /^\d+$/.test(value)) timestamp = Number(value);
    else if (key === "v1" && /^[0-9a-f]{64}$/i.test(value)) signatures.push(value.toLowerCase());
  }
  return timestamp === null || signatures.length === 0 ? null : { timestamp, signatures };
}

function timingSafeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

async function hmacHex(secret: string, message: Uint8Array): Promise<string> {
  const key = await crypto.subtle.importKey("raw", encoder.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const digest = new Uint8Array(await crypto.subtle.sign("HMAC", key, message));
  return Array.from(digest, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

/**
 * Checks a webhook's `Mailhive-Signature` header — HMAC-SHA256 of
 * `"<t>.<raw body>"` with the endpoint's secret — and returns the parsed
 * event. Throws WebhookVerificationError if anything doesn't match.
 * Several `v1=` values are accepted, so secrets can be rotated.
 */
export async function verifyWebhook(options: VerifyWebhookOptions): Promise<WebhookEvent> {
  const { secret, tolerance = 300 } = options;
  const body = toBytes(options.payload);
  const parsed = options.signature ? parseHeader(options.signature) : null;
  if (!parsed) {
    throw new WebhookVerificationError("Missing or malformed Mailhive-Signature header.", "header");
  }
  const now = options.now ?? Math.floor(Date.now() / 1000);
  if (Math.abs(now - parsed.timestamp) > tolerance) {
    throw new WebhookVerificationError(
      `The webhook's timestamp is more than ${tolerance} seconds from now; it may be a replay.`,
      "timestamp",
    );
  }
  const prefix = encoder.encode(`${parsed.timestamp}.`);
  const message = new Uint8Array(prefix.length + body.length);
  message.set(prefix);
  message.set(body, prefix.length);
  const expected = await hmacHex(secret, message);
  if (!parsed.signatures.some((candidate) => timingSafeEqual(candidate, expected))) {
    throw new WebhookVerificationError("The webhook's signature doesn't match. Check the endpoint's signing secret.", "signature");
  }
  return JSON.parse(new TextDecoder().decode(body)) as WebhookEvent;
}
