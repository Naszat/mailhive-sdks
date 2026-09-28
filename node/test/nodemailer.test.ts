import nodemailer, { type SendMailOptions } from "nodemailer";
import { describe, expect, it, vi } from "vitest";
import { Mailhive, ValidationError } from "../src/index.js";
import { mailhiveTransport } from "../src/nodemailer.js";

function setup(response: Response = Response.json({ id: "m1", status: "queued", suppressed: ["gone@example.com"], test: false })) {
  const fetch = vi.fn().mockResolvedValue(response);
  const transporter = nodemailer.createTransport(mailhiveTransport({ client: new Mailhive("mhs_x", { fetch }) }));
  return { fetch, transporter };
}

describe("Nodemailer transport", () => {
  it("sends Nodemailer's message through the API", async () => {
    const { fetch, transporter } = setup();
    const info = await transporter.sendMail({
      from: { name: "Acme, Inc", address: "hello@acme.com" },
      to: ["ada@example.com", "gone@example.com"],
      cc: "Grace <grace@example.com>",
      replyTo: "support@acme.com",
      subject: "Your receipt",
      html: "<p>Thanks</p>",
      text: "Thanks",
      headers: { "X-Order": "1042" },
      attachments: [
        { filename: "receipt.txt", content: "Paid" },
        { filename: "logo.png", content: Buffer.from([1, 2, 3]), contentType: "image/png" },
      ],
      idempotencyKey: "order-1042",
      tags: { type: "receipt" },
    } as SendMailOptions);

    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe("https://api.mailhive.africa/v1/send/emails");
    expect(init.headers["Idempotency-Key"]).toBe("order-1042");
    expect(JSON.parse(init.body)).toEqual({
      from: '"Acme, Inc" <hello@acme.com>',
      to: ["ada@example.com", "gone@example.com"],
      cc: ['"Grace" <grace@example.com>'],
      reply_to: ["support@acme.com"],
      subject: "Your receipt",
      html: "<p>Thanks</p>",
      text: "Thanks",
      headers: { "X-Order": "1042" },
      tags: { type: "receipt" },
      attachments: [
        { filename: "receipt.txt", content: "UGFpZA==", content_type: "text/plain" },
        { filename: "logo.png", content: "AQID", content_type: "image/png" },
      ],
    });
    expect(info.messageId).toBe("m1");
    expect(info.accepted).toEqual(["ada@example.com", "grace@example.com"]);
    expect(info.rejected).toEqual(["gone@example.com"]);
  });

  it("passes API errors to sendMail", async () => {
    const { transporter } = setup(
      Response.json({ error: { code: "domain_not_verified", message: "acme.com isn't verified" } }, { status: 422 }),
    );
    await expect(transporter.sendMail({ from: "hello@acme.com", to: "a@example.com", subject: "s", text: "t" })).rejects.toBeInstanceOf(
      ValidationError,
    );
  });
});
