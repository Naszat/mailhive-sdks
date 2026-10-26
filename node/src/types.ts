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

// ---- audiences and contacts (an API key with Full access) ----

export interface Audience {
  id: string;
  name: string;
  created_at?: string;
  /** People in the audience. */
  contacts?: number;
  /** Of those, who a broadcast would reach. */
  mailable?: number;
}

export type ContactStatus = "subscribed" | "unsubscribed" | "pending";

export interface Contact {
  id: string;
  audience_id: string;
  email: string;
  first_name: string | null;
  last_name: string | null;
  fields: Record<string, string>;
  status: ContactStatus;
  /** On the suppression list (bounce, spam complaint or unsubscribe): never mailed. */
  suppressed: boolean;
  /** Subscribed and not suppressed: a broadcast would reach them. */
  mailable: boolean;
  source: "manual" | "import" | "api" | "form";
  created_at: string;
  unsubscribed_at: string | null;
  /** True when a test key made the call: nothing was saved or sent. */
  test?: boolean;
}

export interface ContactDetail extends Contact {
  /** The latest emails sent to them. */
  history: Email[];
}

/** Email the person a confirmation link first: they're subscribed only if
 * they click it. Until then they're `pending` and never mailed. */
export interface DoubleOptIn {
  /** On a verified sending domain, e.g. `Acme <news@example.com>`. */
  from: string;
  subject?: string;
}

export interface ContactInput {
  email: string;
  /** Leave out to keep it; `""` clears it. */
  first_name?: string | null;
  last_name?: string | null;
  /** Merged into the contact's fields; an empty value removes one. */
  fields?: Record<string, string>;
  /** Defaults to subscribed for a new contact. Someone who unsubscribed or
   * reported spam can't be set to subscribed: use `double_opt_in`. */
  status?: "subscribed" | "unsubscribed";
  double_opt_in?: DoubleOptIn;
}

export interface ContactUpdate {
  first_name?: string | null;
  last_name?: string | null;
  /** Replaces the contact's fields. */
  fields?: Record<string, string>;
  unsubscribe?: boolean;
}

export interface ListContactsParams {
  /** One person, by exact address. */
  email?: string;
  q?: string;
  status?: ContactStatus;
  page?: number;
  page_size?: number;
}

export interface Page<T> {
  items: T[];
  page: number;
  page_size: number;
  total: number;
}

export interface ContactBatchEntry {
  index: number;
  email: string;
  ok: boolean;
  created?: boolean;
  contact?: Contact;
  error?: { code: string; message: string };
}

export interface ContactBatchResult {
  created: number;
  updated: number;
  failed: number;
  /** One per contact sent, in order. A failure here doesn't throw. */
  results: ContactBatchEntry[];
  test?: boolean;
}

/** For calls that are safe to repeat, so there's no idempotency key. */
export type ReadOptions = Pick<RequestOptions, "signal">;
