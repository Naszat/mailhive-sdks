import { describe, expect, it } from "vitest";
import { FormError, createForm, solve } from "../src/index.js";
import { KEY, fakeApi, submitted, vectors } from "./helpers.js";

describe("proof of work", () => {
  for (const [index, v] of vectors.cases.entries()) {
    it(`solves a challenge issued by the backend (${index + 1})`, async () => {
      expect((await solve(v.challenge)).number).toBe(v.number);
    });
  }
});

describe("createForm", () => {
  it("refuses a secret key, loudly", () => {
    expect(() => createForm("mhs_secret")).toThrow(/Never put one in a web page/);
    expect(() => createForm("nope")).toThrow(/starts with mhp_/);
  });

  it("gets a challenge, solves it and sends the values", async () => {
    const fetch = fakeApi();
    const form = createForm(KEY, { fetch, minAgeMs: 0 });
    const result = await form.submit({ name: "Ada", email: "ada@example.com", age: 36, skip: undefined });
    expect(result).toEqual({ id: "sub_1", redirectUrl: null });
    expect(fetch.mock.calls[0]![0]).toBe(`https://api.mailhive.africa/v1/send/client/${KEY}/challenge`);
    const { url, body } = submitted(fetch);
    expect(url).toBe(`https://api.mailhive.africa/v1/send/client/${KEY}/submit`);
    expect(body).toMatchObject({ name: "Ada", email: "ada@example.com", age: "36" });
    expect(body).not.toHaveProperty("skip");
    expect(body._challenge.number).toBe(vectors.cases[0]!.number);
  });

  it("uses a fresh challenge for every submission", async () => {
    const fetch = fakeApi();
    const form = createForm(KEY, { fetch, minAgeMs: 0 });
    await form.submit({ a: "1" });
    await form.submit({ a: "2" });
    expect(fetch.mock.calls.filter(([url]) => String(url).endsWith("/challenge"))).toHaveLength(2);
  });

  it("prepare() fetches the challenge once, ahead of time", async () => {
    const fetch = fakeApi();
    const form = createForm(KEY, { fetch, minAgeMs: 0 });
    form.prepare();
    form.prepare();
    await form.submit({ a: "1" });
    expect(fetch.mock.calls.filter(([url]) => String(url).endsWith("/challenge"))).toHaveLength(1);
  });

  it("waits until the challenge is old enough, so the server doesn't take it for a bot", async () => {
    const form = createForm(KEY, { fetch: fakeApi(), minAgeMs: 300 });
    const started = Date.now();
    await form.submit({ a: "1" });
    expect(Date.now() - started).toBeGreaterThanOrEqual(290);
  });

  it("turns API errors into FormErrors with field messages", async () => {
    const fetch = fakeApi(() =>
      Response.json(
        {
          error: {
            code: "invalid_submission",
            message: "Some fields need attention.",
            details: [{ field: "email", msg: "Email is required." }, { field: null, msg: "This form doesn't have the field x." }],
            request_id: "req_1",
          },
        },
        { status: 422 },
      ),
    );
    const error = await createForm(KEY, { fetch, minAgeMs: 0 }).submit({}).catch((e) => e);
    expect(error).toBeInstanceOf(FormError);
    expect(error).toMatchObject({ code: "invalid_submission", status: 422, requestId: "req_1" });
    expect(error.fieldErrors).toEqual([
      { field: "email", message: "Email is required." },
      { field: null, message: "This form doesn't have the field x." },
    ]);
  });

  it("reports how long to wait when rate limited", async () => {
    const fetch = fakeApi(
      () => new Response(JSON.stringify({ error: { code: "rate_limited", message: "Too many" } }), { status: 429, headers: { "Retry-After": "42" } }),
    );
    const error = await createForm(KEY, { fetch, minAgeMs: 0 }).submit({}).catch((e) => e);
    expect(error.retryAfter).toBe(42);
  });

  it("reports a network failure as a FormError", async () => {
    const fetch = fakeApi();
    fetch.mockImplementation(async (input: RequestInfo | URL) => {
      if (String(input).endsWith("/challenge")) throw new TypeError("Failed to fetch");
      return Response.json({});
    });
    const error = await createForm(KEY, { fetch, minAgeMs: 0 }).submit({}).catch((e) => e);
    expect(error).toMatchObject({ code: "network_error", status: 0 });
  });
});
