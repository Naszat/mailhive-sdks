import type { Mailhive } from "../client.js";
import type { Audience, ReadOptions, RequestOptions } from "../types.js";

/** Audiences: the lists your broadcasts go to. Needs an API key with Full access. */
export class Audiences {
  constructor(private readonly client: Mailhive) {}

  list(options: ReadOptions = {}): Promise<Audience[]> {
    return this.client.request<Audience[]>("GET", "/send/audiences", options);
  }

  create(name: string, options: RequestOptions = {}): Promise<Audience> {
    return this.client.request<Audience>("POST", "/send/audiences", { body: { name }, ...options });
  }

  rename(id: string, name: string, options: ReadOptions = {}): Promise<Pick<Audience, "id" | "name">> {
    return this.client.request("PATCH", `/send/audiences/${encodeURIComponent(id)}`, { body: { name }, ...options });
  }
}
