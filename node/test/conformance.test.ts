// The shared conformance suite (spec/conformance.json), run against the
// shared mock server. Every Mailhive SDK runs these same cases.
import { readFileSync } from "node:fs";
import { afterAll, beforeAll, beforeEach, describe, expect, it } from "vitest";
import { startMockServer } from "../../mock-server/server.mjs";
import {
  ApiError,
  AuthenticationError,
  BillingError,
  ConflictError,
  Mailhive,
  NotFoundError,
  PermissionError,
  RateLimitError,
  ValidationError,
} from "../src/index.js";

interface Case {
  name: string;
  key: string;
  call: "emails.send" | "emails.sendBatch" | "emails.get";
  id?: string;
  idempotencyKey?: string;
  expect: Record<string, any>;
}

const spec = JSON.parse(readFileSync(new URL("../../spec/conformance.json", import.meta.url), "utf-8")) as {
  email: Record<string, string>;
  cases: Case[];
};

const ERROR_CLASSES: Record<string, typeof ApiError> = {
  api: ApiError,
  authentication: AuthenticationError,
  billing: BillingError,
  permission: PermissionError,
  not_found: NotFoundError,
  conflict: ConflictError,
  validation: ValidationError,
  rate_limit: RateLimitError,
};

let server: { url: string; close: () => Promise<void> };

beforeAll(async () => {
  server = await startMockServer();
});
afterAll(() => server.close());
beforeEach(async () => {
  await fetch(`${server.url}/__reset`, { method: "POST" });
});

async function recorded(): Promise<{ method: string; path: string; headers: Record<string, string>; body: any }[]> {
  return (await fetch(`${server.url}/__requests`)).json();
}

function run(client: Mailhive, testCase: Case) {
  const email = spec.email as any;
  const options = testCase.idempotencyKey ? { idempotencyKey: testCase.idempotencyKey } : {};
  if (testCase.call === "emails.send") return client.emails.send(email, options);
  if (testCase.call === "emails.sendBatch") return client.emails.sendBatch([email, email], options);
  return client.emails.get(testCase.id!);
}

describe("conformance", () => {
  for (const testCase of spec.cases) {
    it(testCase.name, async () => {
      const client = new Mailhive(testCase.key, { baseUrl: `${server.url}/v1`, maxRetries: 2 });
      const { expect: want } = testCase;
      const started = Date.now();
      let result: unknown;
      let error: unknown;
      try {
        result = await run(client, testCase);
      } catch (caught) {
        error = caught;
      }
      const elapsed = Date.now() - started;
      const requests = await recorded();

      expect(requests).toHaveLength(want.attempts);
      if (want.result) expect(result).toMatchObject(want.result);
      if (want.resultFields) expect(result).toMatchObject(want.resultFields);
      if (want.minElapsedMs) expect(elapsed).toBeGreaterThanOrEqual(want.minElapsedMs);
      if (want.sameIdempotencyKey) {
        const keys = new Set(requests.map((r) => r.headers["idempotency-key"]));
        expect(keys.size).toBe(1);
        expect([...keys][0]).toBeTruthy();
      }
      if (want.error) {
        expect(error).toBeInstanceOf(ERROR_CLASSES[want.error.kind]);
        const apiError = error as ApiError;
        expect(apiError.status).toBe(want.error.status);
        expect(apiError.code).toBe(want.error.code);
        if (want.error.requestId) expect(apiError.requestId).toBe(want.error.requestId);
        if (want.error.hasDetails) expect(apiError.details).toBeTruthy();
      } else {
        expect(error).toBeUndefined();
      }

      const request = want.request;
      if (request) {
        const first = requests[0]!;
        if (request.method) expect(first.method).toBe(request.method);
        if (request.path) expect(first.path).toBe(request.path);
        if (request.authorization) expect(first.headers.authorization).toBe(request.authorization);
        if (request.contentType) expect(first.headers["content-type"]).toBe(request.contentType);
        if (request.userAgentPattern) expect(first.headers["user-agent"]).toMatch(new RegExp(request.userAgentPattern));
        if (request.idempotencyKeyPattern) expect(first.headers["idempotency-key"]).toMatch(new RegExp(request.idempotencyKeyPattern));
        if (request.idempotencyKey) expect(first.headers["idempotency-key"]).toBe(request.idempotencyKey);
        if (request.noIdempotencyKey) expect(first.headers["idempotency-key"]).toBeUndefined();
        if (request.body) expect(first.body).toEqual(request.body);
        if (request.bodyEmailCount) expect(first.body.emails).toHaveLength(request.bodyEmailCount);
      }
    });
  }
});


// ---- audiences and contacts (spec/conformance.json "contactCases") ----

interface ContactCase {
  name: string;
  key: string;
  call: string;
  args?: Record<string, any>;
  expect: Record<string, any>;
}

const contactSpec = (
  JSON.parse(readFileSync(new URL("../../spec/conformance.json", import.meta.url), "utf-8")) as {
    contactCases: { audienceId: string; contact: any; confirmation: any; cases: ContactCase[] } & Record<string, any>;
  }
).contactCases;

/** A string naming a fixture stands for it; anything else is itself. */
const fixture = (value: any): any => (typeof value === "string" && value in contactSpec ? contactSpec[value] : value);

function runContact(client: Mailhive, testCase: ContactCase) {
  const a = testCase.args ?? {};
  const audienceId = contactSpec.audienceId;
  switch (testCase.call) {
    case "audiences.list":
      return client.audiences.list();
    case "audiences.create":
      return client.audiences.create(a.name);
    case "audiences.rename":
      return client.audiences.rename(a.id, a.name);
    case "contacts.add":
      return client.contacts.add(audienceId, fixture(a.contact));
    case "contacts.upsert": {
      const contact = fixture(a.contact);
      return client.contacts.upsert(audienceId, a.doubleOptIn ? { ...contact, double_opt_in: fixture(a.doubleOptIn) } : contact);
    }
    case "contacts.upsertBatch":
      return client.contacts.upsertBatch(audienceId, a.contacts.map(fixture));
    case "contacts.list": {
      const { pageSize, ...rest } = a;
      return client.contacts.list(audienceId, { ...rest, ...(pageSize !== undefined ? { page_size: pageSize } : {}) });
    }
    case "contacts.get":
      return client.contacts.get(a.id);
    case "contacts.update":
      return client.contacts.update(a.id, a.changes);
    case "contacts.unsubscribe":
      return client.contacts.unsubscribe(a.id);
    case "contacts.delete":
      return client.contacts.delete(a.id);
  }
  throw new Error(`unknown call ${testCase.call}`);
}

const at = (value: any, path: string) => path.split(".").reduce((v, part) => v?.[part], value);

describe("conformance: audiences and contacts", () => {
  for (const testCase of contactSpec.cases) {
    it(testCase.name, async () => {
      const client = new Mailhive(testCase.key, { baseUrl: `${server.url}/v1`, maxRetries: 2 });
      const { expect: want } = testCase;
      const started = Date.now();
      let result: any;
      let error: unknown;
      try {
        result = await runContact(client, testCase);
      } catch (caught) {
        error = caught;
      }
      const elapsed = Date.now() - started;
      const requests = await recorded();

      expect(requests).toHaveLength(want.attempts);
      if (want.resultFields) for (const [path, value] of Object.entries(want.resultFields)) expect(at(result, path), path).toEqual(value);
      if (want.resultCount !== undefined) expect(result).toHaveLength(want.resultCount);
      if (want.resultIsEmpty) expect(result).toBeUndefined();
      if (want.minElapsedMs) expect(elapsed).toBeGreaterThanOrEqual(want.minElapsedMs);
      if (want.error) {
        expect(error).toBeInstanceOf(ERROR_CLASSES[want.error.kind]);
        expect((error as ApiError).status).toBe(want.error.status);
        expect((error as ApiError).code).toBe(want.error.code);
      } else {
        expect(error).toBeUndefined();
      }

      const request = want.request;
      if (request) {
        const first = requests[0] as any;
        if (request.method) expect(first.method).toBe(request.method);
        if (request.path) expect(first.path).toBe(request.path);
        if (request.authorization) expect(first.headers.authorization).toBe(request.authorization);
        if (request.contentType) expect(first.headers["content-type"]).toBe(request.contentType);
        if (request.noIdempotencyKey) expect(first.headers["idempotency-key"]).toBeUndefined();
        if (request.query) expect(first.query).toEqual(request.query);
        if (request.body) expect(first.body).toEqual(fixture(request.body));
        if (request.bodyIncludes) expect(first.body).toMatchObject(request.bodyIncludes);
        if (request.bodyContactCount) expect(first.body.contacts).toHaveLength(request.bodyContactCount);
      }
    });
  }
});
