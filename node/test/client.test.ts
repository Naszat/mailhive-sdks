import { inspect } from "node:util";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ConnectionError, Mailhive, MailhiveError, RateLimitError, VERSION } from "../src/index.js";
import pkg from "../package.json" with { type: "json" };

function jsonResponse(status: number, body: unknown, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});

describe("Mailhive client", () => {
  it("reads the key and base URL from the environment", async () => {
    vi.stubEnv("MAILHIVE_API_KEY", "mhs_fromenv");
    vi.stubEnv("MAILHIVE_BASE_URL", "https://api-beta.mailhive.africa/v1/");
    const fetch = vi.fn().mockResolvedValue(jsonResponse(200, { id: "m1", status: "queued", suppressed: [], test: false }));
    await new Mailhive({ fetch }).emails.send({ from: "a@acme.com", to: "b@example.com", subject: "s", text: "t" });
    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe("https://api-beta.mailhive.africa/v1/send/emails");
    expect(init.headers.Authorization).toBe("Bearer mhs_fromenv");
  });

  it("needs a key", () => {
    vi.stubEnv("MAILHIVE_API_KEY", "");
    expect(() => new Mailhive()).toThrow(/No API key/);
  });

  it("refuses to run with a secret key in a browser", () => {
    vi.stubGlobal("window", {});
    vi.stubGlobal("document", {});
    expect(() => new Mailhive("mhs_secret")).toThrow(/secret_key_in_browser/);
  });

  it("never shows the key when logged", () => {
    const client = new Mailhive("mhs_supersecretvalue");
    expect(inspect(client, { depth: 5 })).not.toContain("supersecret");
  });

  it("base64-encodes byte attachments", async () => {
    const fetch = vi.fn().mockResolvedValue(jsonResponse(200, { id: "m1", status: "queued", suppressed: [], test: false }));
    await new Mailhive("mhs_x", { fetch }).emails.send({
      from: "a@acme.com",
      to: "b@example.com",
      subject: "s",
      text: "t",
      attachments: [{ filename: "a.txt", content: new TextEncoder().encode("hello") }, { filename: "b.txt", content: "aGk=" }],
    });
    const body = JSON.parse(fetch.mock.calls[0]![1].body);
    expect(body.attachments.map((a: { content: string }) => a.content)).toEqual(["aGVsbG8=", "aGk="]);
  });

  it("gives up on a Retry-After longer than a minute instead of hanging", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(jsonResponse(429, { error: { code: "rate_limited", message: "slow down" } }, { "Retry-After": "3600" }));
    const error = await new Mailhive("mhs_x", { fetch }).emails.get("m1").catch((e) => e);
    expect(error).toBeInstanceOf(RateLimitError);
    expect(error.retryAfter).toBe(3600);
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("reports a timeout as a ConnectionError after its retries", async () => {
    const fetch = vi.fn((_url: string, init: RequestInit) =>
      new Promise((_resolve, reject) => init.signal!.addEventListener("abort", () => reject(init.signal!.reason))),
    );
    const client = new Mailhive("mhs_x", { fetch: fetch as unknown as typeof globalThis.fetch, timeout: 20, maxRetries: 0 });
    const error = await client.emails.get("m1").catch((e) => e);
    expect(error).toBeInstanceOf(ConnectionError);
    expect(error.message).toMatch(/didn't answer within 20 ms/);
  });

  it("stops at once when the caller aborts", async () => {
    const controller = new AbortController();
    const fetch = vi.fn(() => {
      controller.abort(new Error("cancelled by caller"));
      return Promise.reject(new DOMException("aborted", "AbortError"));
    });
    const client = new Mailhive("mhs_x", { fetch: fetch as unknown as typeof globalThis.fetch });
    await expect(client.emails.get("m1", { signal: controller.signal })).rejects.toThrow("cancelled by caller");
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("every error is a MailhiveError", async () => {
    const fetch = vi.fn().mockResolvedValue(jsonResponse(404, { error: { code: "not_found", message: "Email not found." } }));
    await expect(new Mailhive("mhs_x", { fetch }).emails.get("x")).rejects.toBeInstanceOf(MailhiveError);
  });

  it("keeps VERSION in step with package.json", () => {
    expect(VERSION).toBe(pkg.version);
  });
});
