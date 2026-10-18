import type { Mailhive } from "../client.js";
import type {
  Contact,
  ContactBatchResult,
  ContactDetail,
  ContactInput,
  ContactUpdate,
  ListContactsParams,
  Page,
  ReadOptions,
  RequestOptions,
} from "../types.js";

const audience = (id: string) => `/send/audiences/${encodeURIComponent(id)}/contacts`;

/** The people in an audience. Needs an API key with Full access.
 *
 * Someone who unsubscribed, or reported your email as spam, can't be made
 * subscribed again by these calls (a 409 `contact_unsubscribed`). Pass
 * `double_opt_in` to email them a confirmation link instead. */
export class Contacts {
  constructor(private readonly client: Mailhive) {}

  /** A page of contacts; `email` finds one person by exact address. */
  list(audienceId: string, params: ListContactsParams = {}, options: ReadOptions = {}): Promise<Page<Contact>> {
    return this.client.request<Page<Contact>>("GET", audience(audienceId), { query: { ...params }, ...options });
  }

  /** Adds one person. A 409 `contact_exists` if they're already in the audience: use `upsert`. */
  add(audienceId: string, contact: ContactInput, options: RequestOptions = {}): Promise<Contact> {
    return this.client.request<Contact>("POST", audience(audienceId), { body: contact, ...options });
  }

  /** Adds the person, or updates them if they're already there. Safe to repeat. */
  upsert(audienceId: string, contact: ContactInput, options: ReadOptions = {}): Promise<Contact> {
    return this.client.request<Contact>("PUT", audience(audienceId), { body: contact, ...options });
  }

  /** Adds or updates up to 100 people. A bad entry doesn't throw: check each result's `ok`. */
  upsertBatch(audienceId: string, contacts: ContactInput[], options: RequestOptions = {}): Promise<ContactBatchResult> {
    return this.client.request<ContactBatchResult>("POST", `${audience(audienceId)}/batch`, { body: { contacts }, ...options });
  }

  /** A contact and the latest emails sent to them. */
  get(id: string, options: ReadOptions = {}): Promise<ContactDetail> {
    return this.client.request<ContactDetail>("GET", `/send/contacts/${encodeURIComponent(id)}`, options);
  }

  /** Edits names and fields (fields are replaced, not merged: use `upsert` to merge). */
  update(id: string, changes: ContactUpdate, options: ReadOptions = {}): Promise<Contact> {
    return this.client.request<Contact>("PATCH", `/send/contacts/${encodeURIComponent(id)}`, { body: changes, ...options });
  }

  /** Unsubscribes them. Only they can undo it, by confirming a double opt-in email. */
  unsubscribe(id: string, options: ReadOptions = {}): Promise<Contact> {
    return this.update(id, { unsubscribe: true }, options);
  }

  /** Deleting someone who unsubscribed keeps their address suppressed. */
  async delete(id: string, options: ReadOptions = {}): Promise<void> {
    await this.client.request("DELETE", `/send/contacts/${encodeURIComponent(id)}`, options);
  }
}
