// A Nodemailer transport that sends through the Mailhive Send API, so an
// app already using Nodemailer switches with one line:
//
//   const transporter = nodemailer.createTransport(mailhiveTransport());
//
// Nodemailer isn't a dependency: this only uses the transport interface.

import { Mailhive, type MailhiveOptions } from "./client.js";
import type { AcceptedEmail, SendEmailRequest } from "./types.js";
import { VERSION } from "./version.js";

type Address = string | { name?: string; address: string };

interface NormalizedMail {
  from?: Address;
  to?: Address | Address[];
  cc?: Address | Address[];
  bcc?: Address | Address[];
  replyTo?: Address | Address[];
  subject?: string;
  html?: string | Uint8Array;
  text?: string | Uint8Array;
  headers?: Record<string, string | string[]> | { key: string; value: string }[];
  attachments?: { filename?: string | false; content?: string | Uint8Array; contentType?: string; encoding?: string }[];
}

interface MailMessage {
  data: { idempotencyKey?: string; tags?: Record<string, string> };
  normalize(callback: (error: Error | null, data?: NormalizedMail) => void): void;
}

export interface MailhiveTransportOptions extends MailhiveOptions {
  /** An existing client to reuse instead of creating one. */
  client?: Mailhive;
}

export interface SentInfo extends AcceptedEmail {
  messageId: string;
  envelope: { from: string; to: string[] };
  accepted: string[];
  rejected: string[];
}

function format(address: Address): string {
  if (typeof address === "string") return address;
  return address.name ? `${JSON.stringify(address.name)} <${address.address}>` : address.address;
}

function list(value: Address | Address[] | undefined): string[] {
  if (value === undefined) return [];
  return (Array.isArray(value) ? value : [value]).map(format);
}

function bare(value: Address | Address[] | undefined): string[] {
  return (value === undefined ? [] : Array.isArray(value) ? value : [value]).map((a) =>
    typeof a === "string" ? a : a.address,
  );
}

function text(value: string | Uint8Array | undefined): string | undefined {
  return value === undefined || typeof value === "string" ? value : new TextDecoder().decode(value);
}

function headers(value: NormalizedMail["headers"]): Record<string, string> | undefined {
  if (!value) return undefined;
  const entries = Array.isArray(value)
    ? value.map(({ key, value: v }) => [key, v] as const)
    : Object.entries(value).map(([key, v]) => [key, Array.isArray(v) ? v.join(", ") : v] as const);
  return entries.length ? Object.fromEntries(entries) : undefined;
}

function toRequest(mail: NormalizedMail, tags?: Record<string, string>): SendEmailRequest {
  if (!mail.from) throw new Error("Mailhive: the email needs a from address.");
  return {
    from: format(mail.from),
    to: list(mail.to),
    ...(mail.cc ? { cc: list(mail.cc) } : {}),
    ...(mail.bcc ? { bcc: list(mail.bcc) } : {}),
    ...(mail.replyTo ? { reply_to: list(mail.replyTo) } : {}),
    ...(mail.subject !== undefined ? { subject: mail.subject } : {}),
    ...(mail.html !== undefined ? { html: text(mail.html) } : {}),
    ...(mail.text !== undefined ? { text: text(mail.text) } : {}),
    ...(headers(mail.headers) ? { headers: headers(mail.headers) } : {}),
    ...(tags ? { tags } : {}),
    ...(mail.attachments?.length
      ? {
          attachments: mail.attachments.map((a, index) => ({
            filename: a.filename || `attachment-${index + 1}`,
            // normalize() hands back Buffers, or strings in `encoding`.
            content:
              typeof a.content === "string"
                ? a.encoding === "base64"
                  ? a.content
                  : new TextEncoder().encode(a.content)
                : (a.content ?? new Uint8Array()),
            ...(a.contentType ? { content_type: a.contentType } : {}),
          })),
        }
      : {}),
  };
}

/** A Nodemailer transport. Pass `idempotencyKey` or `tags` in sendMail's
 * options to use those Mailhive features. */
export function mailhiveTransport(options: MailhiveTransportOptions = {}) {
  const { client: existing, ...clientOptions } = options;
  const client = existing ?? new Mailhive(clientOptions);
  return {
    name: "Mailhive",
    version: VERSION,
    send(mail: MailMessage, callback: (error: Error | null, info?: SentInfo) => void): void {
      mail.normalize((normalizeError, data) => {
        if (normalizeError || !data) return callback(normalizeError ?? new Error("Couldn't read the email."));
        let request: SendEmailRequest;
        try {
          request = toRequest(data, mail.data.tags);
        } catch (error) {
          return callback(error as Error);
        }
        const recipients = [...bare(data.to), ...bare(data.cc), ...bare(data.bcc)];
        client.emails
          .send(request, mail.data.idempotencyKey ? { idempotencyKey: mail.data.idempotencyKey } : {})
          .then((accepted) =>
            callback(null, {
              ...accepted,
              messageId: accepted.id,
              envelope: { from: bare(data.from)[0] ?? "", to: recipients },
              accepted: recipients.filter((r) => !accepted.suppressed.includes(r.toLowerCase())),
              rejected: recipients.filter((r) => accepted.suppressed.includes(r.toLowerCase())),
            }),
          )
          .catch((error: Error) => callback(error));
      });
    },
  };
}
