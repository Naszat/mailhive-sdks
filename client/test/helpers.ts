import { vi } from "vitest";
import type { Challenge } from "../src/pow.js";
import powVectors from "../../spec/pow-vectors.json" with { type: "json" };

export const vectors = powVectors as { cases: { challenge: Challenge; number: number }[] };

export const KEY = "mhp_testkey123";

/** A fetch that answers the challenge and submit calls like the API. */
export function fakeApi(submit: () => Response = () => Response.json({ ok: true, id: "sub_1", redirect_url: null })) {
  let issued = 0;
  const fetch = vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) => {
    const url = String(input);
    if (url.endsWith("/challenge")) return Response.json(vectors.cases[issued++ % vectors.cases.length]!.challenge);
    return submit();
  });
  return fetch;
}

export function submitted(fetch: ReturnType<typeof fakeApi>) {
  const call = fetch.mock.calls.find(([url]) => String(url).endsWith("/submit"))!;
  return { url: call[0], body: JSON.parse(String(call[1]!.body)) };
}
