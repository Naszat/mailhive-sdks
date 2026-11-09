import type { Mailhive } from "../client.js";
import type { AcceptedEmail, Attachment, Email, RecipientVerdict, RequestOptions, SendEmailRequest } from "../types.js";

function toBase64(bytes: Uint8Array): string {
  if (typeof Buffer !== "undefined") return Buffer.from(bytes.buffer, bytes.byteOffset, bytes.byteLength).toString("base64");
  let binary = "";
  for (let i = 0; i < bytes.length; i += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  }
  return btoa(binary);
}

function encodeAttachment(attachment: Attachment) {
  const { content } = attachment;
  if (typeof content === "string") return attachment;
  const bytes = content instanceof Uint8Array ? content : new Uint8Array(content);
  return { ...attachment, content: toBase64(bytes) };
}

function wire(email: SendEmailRequest) {
  return email.attachments ? { ...email, attachments: email.attachments.map(encodeAttachment) } : email;
}

export class Emails {
  constructor(private readonly client: Mailhive) {}

  /** Sends one email. Resolves once Mailhive has accepted it. */
  send(email: SendEmailRequest, options: RequestOptions = {}): Promise<AcceptedEmail> {
    return this.client.request<AcceptedEmail>("POST", "/send/emails", { body: wire(email), ...options });
  }

  /** Sends up to 100 independent emails. All are accepted, or none are. */
  async sendBatch(emails: SendEmailRequest[], options: RequestOptions = {}): Promise<AcceptedEmail[]> {
    const response = await this.client.request<{ data: AcceptedEmail[] }>("POST", "/send/emails/batch", {
      body: { emails: emails.map(wire) },
      ...options,
    });
    return response.data;
  }

  /** An email and its current status. */
  get(id: string, options: Pick<RequestOptions, "signal"> = {}): Promise<Email> {
    return this.client.request<Email>("GET", `/send/emails/${encodeURIComponent(id)}`, options);
  }

  /** The checks sending applies, for a form to run while the person can
   * still fix a typo: syntax, mistyped domains (with a suggestion),
   * domains that don't exist or have no mail server, and your suppression
   * list. Up to 100 addresses; nothing is sent or stored. */
  async validate(emails: string[], options: Pick<RequestOptions, "signal"> = {}): Promise<RecipientVerdict[]> {
    const response = await this.client.request<{ data: RecipientVerdict[] }>("POST", "/send/emails/validate", {
      body: { emails },
      ...options,
    });
    return response.data;
  }
}
