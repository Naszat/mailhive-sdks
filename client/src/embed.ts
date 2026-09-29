// The drop-in script: any <form data-mailhive="mhp_…"> on the page sends
// through Mailhive, with the anti-spam check, errors and success message
// handled. No other code needed.
//
//   <script src="https://cdn.jsdelivr.net/npm/@mailhive/client@0/dist/embed.js" defer></script>
//   <form data-mailhive="mhp_…" data-mailhive-success="Thanks, we'll be in touch.">
//     <input name="name" required> <input name="email" type="email" required>
//     <textarea name="message" required></textarea> <button>Send</button>
//   </form>
//
// Optional markup: [data-mailhive-error-for="email"] shows that field's
// error; [data-mailhive-error] shows other errors; [data-mailhive-success]
// (hidden) is shown instead of the default thank-you. Events on the form:
// mailhive:success and mailhive:error (both cancelable, to handle them
// yourself), with the result or the FormError in event.detail.

import { createForm, type MailhiveForm } from "./client.js";
import { FormError } from "./errors.js";
import { VERSION } from "./version.js";

const HONEYPOT = "_honey";
const wired = new WeakMap<HTMLFormElement, MailhiveForm>();

function addHoneypot(form: HTMLFormElement): void {
  if (form.querySelector(`[name="${HONEYPOT}"]`)) return;
  const input = document.createElement("input");
  input.type = "text";
  input.name = HONEYPOT;
  input.tabIndex = -1;
  input.autocomplete = "off";
  input.setAttribute("aria-hidden", "true");
  input.style.cssText = "position:absolute!important;left:-10000px!important;width:1px;height:1px;opacity:0";
  form.appendChild(input);
}

function setBusy(form: HTMLFormElement, busy: boolean): void {
  form.toggleAttribute("aria-busy", busy);
  form.querySelectorAll<HTMLButtonElement | HTMLInputElement>('button:not([type="button"]), input[type="submit"]').forEach((button) => {
    button.disabled = busy;
  });
}

function clearErrors(form: HTMLFormElement): void {
  form.querySelectorAll("[aria-invalid]").forEach((el) => el.removeAttribute("aria-invalid"));
  form.querySelectorAll<HTMLElement>("[data-mailhive-error-for], [data-mailhive-error]").forEach((el) => {
    el.textContent = "";
    el.hidden = true;
  });
  form.querySelectorAll(".mailhive-error[data-mailhive-generated]").forEach((el) => el.remove());
}

function generalErrorElement(form: HTMLFormElement): HTMLElement {
  const existing = form.querySelector<HTMLElement>("[data-mailhive-error]");
  if (existing) return existing;
  const el = document.createElement("p");
  el.className = "mailhive-error";
  el.setAttribute("role", "alert");
  el.setAttribute("data-mailhive-generated", "");
  form.appendChild(el);
  return el;
}

function showErrors(form: HTMLFormElement, error: FormError): void {
  const general: string[] = [];
  for (const { field, message } of error.fieldErrors) {
    const named = field ? form.elements.namedItem(field) : null;
    const input = named instanceof Element ? named : named instanceof RadioNodeList ? (named[0] as Element | undefined) : null;
    input?.setAttribute("aria-invalid", "true");
    const slot = field
      ? Array.from(form.querySelectorAll<HTMLElement>("[data-mailhive-error-for]")).find(
          (el) => el.getAttribute("data-mailhive-error-for") === field,
        )
      : undefined;
    if (slot) {
      slot.textContent = message;
      slot.hidden = false;
    } else {
      general.push(message);
    }
  }
  if (error.fieldErrors.length === 0) {
    general.push(
      error.code === "rate_limited" && error.retryAfter
        ? `Too many attempts. Please try again in ${Math.ceil(error.retryAfter / 60)} minute${error.retryAfter > 60 ? "s" : ""}.`
        : error.message,
    );
  }
  if (general.length) {
    const el = generalErrorElement(form);
    el.textContent = general.join(" ");
    el.hidden = false;
  }
  form.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
}

function showSuccess(form: HTMLFormElement): void {
  const custom = form.querySelector<HTMLElement>("[data-mailhive-success]");
  if (custom) {
    Array.from(form.children).forEach((child) => {
      if (child !== custom) (child as HTMLElement).hidden = true;
    });
    custom.hidden = false;
    custom.setAttribute("role", "status");
    return;
  }
  const message = document.createElement("p");
  message.className = "mailhive-success";
  message.setAttribute("role", "status");
  message.textContent = form.getAttribute("data-mailhive-success") || "Thanks! Your message has been sent.";
  form.replaceWith(message);
}

function emit(form: HTMLFormElement, name: string, detail: unknown): boolean {
  return form.dispatchEvent(new CustomEvent(name, { detail, cancelable: true, bubbles: true }));
}

export function wire(form: HTMLFormElement): MailhiveForm | undefined {
  const key = form.getAttribute("data-mailhive");
  if (!key || wired.has(form)) return wired.get(form);
  let client: MailhiveForm;
  try {
    const baseUrl = form.getAttribute("data-mailhive-base-url") ?? undefined;
    client = createForm(key, baseUrl ? { baseUrl } : {});
  } catch (error) {
    console.error(`[Mailhive] ${(error as Error).message}`);
    return undefined;
  }
  wired.set(form, client);
  addHoneypot(form);
  // Starting the anti-spam check as soon as someone begins typing makes
  // submitting feel instant.
  form.addEventListener("focusin", () => client.prepare(), { once: true });
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (form.hasAttribute("aria-busy")) return;
    const values: Record<string, string> = {};
    new FormData(form).forEach((value, name) => {
      if (typeof value === "string") values[name] = value;
    });
    clearErrors(form);
    setBusy(form, true);
    try {
      const result = await client.submit(values);
      if (!emit(form, "mailhive:success", result)) return;
      if (result.redirectUrl) {
        window.location.assign(result.redirectUrl);
        return;
      }
      showSuccess(form);
    } catch (caught) {
      const error =
        caught instanceof FormError
          ? caught
          : new FormError({ code: "unknown", message: "Something went wrong. Please try again.", status: 0 });
      if (emit(form, "mailhive:error", error)) showErrors(form, error);
    } finally {
      setBusy(form, false);
    }
  });
  return client;
}

/** Wires every <form data-mailhive> on the page. Call again after adding
 * forms dynamically; already-wired forms are skipped. */
export function scan(root: ParentNode = document): void {
  root.querySelectorAll<HTMLFormElement>("form[data-mailhive]").forEach(wire);
}

export { VERSION };

if (typeof document !== "undefined") {
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", () => scan());
  else scan();
}
