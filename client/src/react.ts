import { useCallback, useMemo, useState } from "react";
import { createForm, type FormOptions, type Submitted, type Values } from "./client.js";
import { FormError } from "./errors.js";

export type FormStatus = "idle" | "submitting" | "success" | "error";

export interface UseMailhiveForm {
  status: FormStatus;
  error: FormError | null;
  result: Submitted | null;
  /** Sends the values. Never throws: check `status` and `error`. */
  submit: (values: Values) => Promise<Submitted | null>;
  /** Starts the anti-spam check early, e.g. on the first focus. */
  prepare: () => void;
  /** The server's message for one field, if it had a problem. */
  fieldError: (name: string) => string | undefined;
  reset: () => void;
}

/**
 * const form = useMailhiveForm("mhp_…");
 * <form onFocus={form.prepare} onSubmit={(e) => { e.preventDefault(); form.submit(Object.fromEntries(new FormData(e.currentTarget))); }}>
 */
export function useMailhiveForm(key: string, options: FormOptions = {}): UseMailhiveForm {
  const { baseUrl } = options;
  const form = useMemo(() => createForm(key, { baseUrl }), [key, baseUrl]);
  const [state, setState] = useState<{ status: FormStatus; error: FormError | null; result: Submitted | null }>({
    status: "idle",
    error: null,
    result: null,
  });

  const submit = useCallback(
    async (values: Values) => {
      setState({ status: "submitting", error: null, result: null });
      try {
        const result = await form.submit(values);
        setState({ status: "success", error: null, result });
        return result;
      } catch (caught) {
        const error =
          caught instanceof FormError
            ? caught
            : new FormError({ code: "unknown", message: "Something went wrong. Please try again.", status: 0 });
        setState({ status: "error", error, result: null });
        return null;
      }
    },
    [form],
  );

  return {
    ...state,
    submit,
    prepare: useCallback(() => form.prepare(), [form]),
    fieldError: (name: string) => state.error?.fieldErrors.find((f) => f.field === name)?.message,
    reset: useCallback(() => setState({ status: "idle", error: null, result: null }), []),
  };
}
