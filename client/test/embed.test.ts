// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { KEY, fakeApi, submitted } from "./helpers.js";

async function setup(html: string, submit?: () => Response) {
  document.body.innerHTML = html;
  const fetch = fakeApi(submit);
  vi.stubGlobal("fetch", fetch);
  const { scan } = await import("../src/embed.js");
  scan();
  return fetch;
}

function submitForm() {
  const form = document.querySelector("form")!;
  form.dispatchEvent(new Event("submit", { cancelable: true, bubbles: true }));
}

const FORM = `<form data-mailhive="${KEY}" data-mailhive-success="Thanks, Ada">
  <input name="email" value="ada@example.com"><span data-mailhive-error-for="email" hidden></span>
  <button>Send</button></form>`;

beforeEach(() => {
  vi.resetModules();
});
afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe("embed", () => {
  it("adds a hidden honeypot field", async () => {
    await setup(FORM);
    const honeypot = document.querySelector<HTMLInputElement>('input[name="_honey"]')!;
    expect(honeypot.tabIndex).toBe(-1);
    expect(honeypot.getAttribute("aria-hidden")).toBe("true");
  });

  it("submits the form and shows the thank-you message", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const fetch = await setup(FORM);
    submitForm();
    await vi.waitFor(() => expect(document.querySelector('[role="status"]')?.textContent).toBe("Thanks, Ada"), { timeout: 8000 });
    expect(submitted(fetch).body).toMatchObject({ email: "ada@example.com", _honey: "" });
    expect(document.querySelector("form")).toBeNull();
  });

  it("shows field errors next to the field", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    await setup(FORM, () =>
      Response.json({ error: { code: "invalid_submission", message: "x", details: [{ field: "email", msg: "Email is required." }] } }, { status: 422 }),
    );
    submitForm();
    const slot = document.querySelector<HTMLElement>('[data-mailhive-error-for="email"]')!;
    await vi.waitFor(() => expect(slot.textContent).toBe("Email is required."), { timeout: 8000 });
    expect(slot.hidden).toBe(false);
    expect(document.querySelector('input[name="email"]')!.getAttribute("aria-invalid")).toBe("true");
    expect(document.querySelector("button")!.disabled).toBe(false);
  });

  it("lets the page handle success itself", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    await setup(FORM);
    const handler = vi.fn((event: Event) => event.preventDefault());
    document.querySelector("form")!.addEventListener("mailhive:success", handler);
    submitForm();
    await vi.waitFor(() => expect(handler).toHaveBeenCalled(), { timeout: 8000 });
    expect(document.querySelector("form")).not.toBeNull();
  });

  it("logs instead of crashing on a secret key", async () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => {});
    await setup(`<form data-mailhive="mhs_secret"></form>`);
    expect(error.mock.calls[0]![0]).toMatch(/secret API key/);
  });
});
