import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { Mailhive, WebhookVerificationError, verifyWebhook } from "../src/index.js";

const vectors = JSON.parse(readFileSync(new URL("../../spec/webhook-vectors.json", import.meta.url), "utf-8")) as {
  tolerance_seconds: number;
  cases: { name: string; payload: string; header: string; secret: string; now: number; valid: boolean; reason?: string }[];
};

describe("webhook vectors (signed by the Mailhive backend)", () => {
  for (const v of vectors.cases) {
    it(v.name, async () => {
      const verify = verifyWebhook({ payload: v.payload, signature: v.header, secret: v.secret, now: v.now });
      if (v.valid) {
        await expect(verify).resolves.toMatchObject({ type: "email.delivered" });
      } else {
        const error = await verify.catch((e) => e);
        expect(error).toBeInstanceOf(WebhookVerificationError);
        expect(error.reason).toBe(v.reason);
      }
    });
  }
});

describe("verifyWebhook", () => {
  const valid = vectors.cases[0]!;

  it("accepts the body as bytes", async () => {
    const payload = new TextEncoder().encode(valid.payload);
    await expect(verifyWebhook({ payload, signature: valid.header, secret: valid.secret, now: valid.now })).resolves.toBeTruthy();
  });

  it("refuses parsed JSON with a helpful message", async () => {
    const payload = JSON.parse(valid.payload);
    await expect(verifyWebhook({ payload, signature: valid.header, secret: valid.secret, now: valid.now })).rejects.toThrow(
      /raw request body/,
    );
  });

  it("is also available on the client", async () => {
    const client = new Mailhive("mhs_x");
    await expect(client.webhooks.verify({ payload: valid.payload, signature: valid.header, secret: valid.secret, now: valid.now })).resolves.toBeTruthy();
  });
});
