// @vitest-environment jsdom
import { act, renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useMailhiveForm } from "../src/react.js";
import { KEY, fakeApi } from "./helpers.js";

describe("useMailhiveForm", () => {
  it("tracks a successful submission", async () => {
    vi.stubGlobal("fetch", fakeApi());
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { result } = renderHook(() => useMailhiveForm(KEY));
    expect(result.current.status).toBe("idle");
    await act(async () => {
      await result.current.submit({ name: "Ada" });
    });
    expect(result.current.status).toBe("success");
    expect(result.current.result?.id).toBe("sub_1");
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("exposes field errors without throwing", async () => {
    vi.stubGlobal(
      "fetch",
      fakeApi(() => Response.json({ error: { code: "invalid_submission", message: "x", details: [{ field: "email", msg: "Bad email." }] } }, { status: 422 })),
    );
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { result } = renderHook(() => useMailhiveForm(KEY));
    await act(async () => {
      expect(await result.current.submit({})).toBeNull();
    });
    expect(result.current.status).toBe("error");
    expect(result.current.fieldError("email")).toBe("Bad email.");
    act(() => result.current.reset());
    expect(result.current.status).toBe("idle");
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });
});
