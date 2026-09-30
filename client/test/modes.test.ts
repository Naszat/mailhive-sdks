import { describe, expect, it, vi } from "vitest";
import { FormError, createForm } from "../src/index.js";
import { KEY, vectors } from "./helpers.js";

function api(challenge: unknown) {
  return vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) =>
    String(input).endsWith("/challenge") ? Response.json(challenge) : Response.json({ ok: true, id: "s1", redirect_url: null }),
  );
}

function sentBody(fetch: ReturnType<typeof api>) {
  const call = fetch.mock.calls.find(([url]) => String(url).endsWith("/submit"))!;
  return JSON.parse(String(call[1]!.body));
}

describe("anti-spam modes", () => {
  it("a form with no check sends straight away, without proof of work or waiting", async () => {
    const fetch = api({ mode: "none" });
    const started = Date.now();
    await createForm(KEY, { fetch }).submit({ message: "hi" });
    expect(Date.now() - started).toBeLessThan(1000);
    expect(sentBody(fetch)).toEqual({ message: "hi" });
  });

  it("a Turnstile form sends the widget's token", async () => {
    const fetch = api({ mode: "turnstile", site_key: "0x4AAA" });
    await createForm(KEY, { fetch }).submit({ message: "hi", "cf-turnstile-response": "tok" });
    expect(sentBody(fetch)).toEqual({ message: "hi", "cf-turnstile-response": "tok" });
  });

  it("or a token passed in", async () => {
    const fetch = api({ mode: "turnstile", site_key: "0x4AAA" });
    await createForm(KEY, { fetch }).submit({ message: "hi" }, { turnstileToken: "tok2" });
    expect(sentBody(fetch)["cf-turnstile-response"]).toBe("tok2");
  });

  it("refuses to send a Turnstile form without a token", async () => {
    const fetch = api({ mode: "turnstile", site_key: "0x4AAA" });
    const error = await createForm(KEY, { fetch }).submit({ message: "hi" }).catch((e) => e);
    expect(error).toBeInstanceOf(FormError);
    expect(error.code).toBe("turnstile_missing");
    expect(fetch.mock.calls.some(([url]) => String(url).endsWith("/submit"))).toBe(false);
  });

  it("sends a signed-in user's ID token", async () => {
    const fetch = api({ mode: "none" });
    await createForm(KEY, { fetch }).submit({ message: "hi" }, { idToken: "eyJ.token" });
    expect(sentBody(fetch)._id_token).toBe("eyJ.token");
  });

  it("proof of work, with or without the mode field (older servers)", async () => {
    for (const challenge of [{ mode: "pow", ...vectors.cases[0]!.challenge }, vectors.cases[0]!.challenge]) {
      const fetch = api(challenge);
      await createForm(KEY, { fetch, minAgeMs: 0 }).submit({ message: "hi" });
      expect(sentBody(fetch)._challenge.number).toBe(vectors.cases[0]!.number);
    }
  });
});
