// Field names match the REST API exactly (snake_case), in every Mailhive
// SDK, so the API reference applies as written.

export type Recipients = string | string[];

export interface Attachment {
  filename: string;
  /** The file: base64 text, or raw bytes (encoded for you). */
  content: string | Uint8Array | ArrayBuffer;
  content_type?: string;
}

export interface SendEmailRequest {
  /** At a verified sending domain: `hello@example.com` or `Acme <hello@example.com>`. */
  from: string;
  to: Recipients;
  cc?: Recipients;
  bcc?: Recipients;
  /** Required unless `template_id` is given. */
  subject?: string;
  html?: string;
  text?: string;
  /** A template's id, used instead of subject, html and text. */
  template_id?: string;
  variables?: Record<string, string | number | boolean | null>;
  reply_to?: Recipients;
  headers?: Record<string, string>;
  tags?: Record<string, string>;
  attachments?: Attachment[];
}

export interface RequestOptions {
  /** Sent as `Idempotency-Key`. Generated for you when omitted, and reused
   * on every retry of the same call, so a retry never sends twice. */
  idempotencyKey?: string;
  /** Aborts the call (and any retries). */
  signal?: AbortSignal;
}

export interface AcceptedEmail {
  id: string;
  status: "queued" | "suppressed";
  /** Recipients dropped because they're on the suppression list. */
  suppressed: string[];
  /** True when sent with a test key (`mhs_test_…`): delivery is simulated. */
  test: boolean;
}

export type EmailStatus =
  | "queued"
  | "sent"
  | "delivered"
  | "delayed"
  | "bounced"
  | "complained"
  | "suppressed"
  | "failed";

export interface Email {
  id: string;
  status: EmailStatus;
  stream: "transactional" | "broadcast";
  test: boolean;
  from: string;
  to: string[];
  cc: string[];
  bcc: string[];
  subject: string;
  suppressed: string[];
  tags: Record<string, string>;
  template_id: string | null;
  template_version: number | null;
  created_at: string | null;
  sent_at: string | null;
  last_event_at: string | null;
}

export type WebhookEventType =
  | "email.sent"
  | "email.delivered"
  | "email.delivery_delayed"
  | "email.bounced"
  | "email.complained"
  | "email.opened"
  | "email.clicked"
  | "contact.unsubscribed";

export interface WebhookEvent {
  type: WebhookEventType;
  created_at: string;
  /** Present and true on events that aren't real: the dashboard's test
   * event, and events for mail sent with a test key. */
  test?: boolean;
  data: Record<string, unknown>;
}
