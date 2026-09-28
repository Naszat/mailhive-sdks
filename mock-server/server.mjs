// A stand-in for the Mailhive Send API that every SDK's conformance tests
// run against (spec/conformance.json). No dependencies: `node server.mjs
// [--port 4010]`, or import { startMockServer } from a Node test.
//
// The API key picks the scenario: `Bearer mhs_rate_limited_once` plays the
// rate_limited_once scenario. POST /__reset clears recorded requests and
// scenario state; GET /__requests returns every request since.

import http from "node:http";
import { pathToFileURL } from "node:url";

const accepted = (n) => ({ id: `msg_${n}`, status: "queued", suppressed: [], test: false });

const EMAIL = {
  id: "msg_1",
  status: "delivered",
  stream: "transactional",
  test: false,
  from: "hello@acme.com",
  to: ["ada@example.com"],
  cc: [],
  bcc: [],
  subject: "Hi",
  suppressed: [],
  tags: {},
  template_id: null,
  template_version: null,
  created_at: "2026-09-28T12:00:00",
  sent_at: "2026-09-28T12:00:01",
  last_event_at: "2026-09-28T12:00:03",
};

const ERRORS = {
  unauthorized: [401, "invalid_api_key", "This API key doesn't exist or has been revoked."],
  billing: [402, "billing_restricted", "Mailhive Send is paused until a payment is sorted."],
  forbidden: [403, "send_not_activated", "Mailhive Send isn't turned on for this organization."],
  conflict: [409, "idempotency_conflict", "This Idempotency-Key was already used for a different request."],
  quota: [429, "monthly_quota_reached", "Your trial includes 10,000 emails and they've been used."],
  daily_cap: [429, "daily_cap_reached", "Your organization's daily sending limit is used up."],
};

export function createMockServer() {
  let requests = [];
  let attempts = {};

  function send(res, status, body, headers = {}) {
    const requestId = `req_mock_${requests.length}`;
    const isJson = typeof body !== "string";
    res.writeHead(status, {
      "Content-Type": isJson ? "application/json" : "text/html",
      "X-Request-Id": requestId,
      ...headers,
    });
    res.end(isJson ? JSON.stringify(body) : body);
  }

  function error(res, status, code, message, { details = null, headers = {} } = {}) {
    const requestId = `req_mock_${requests.length}`;
    send(res, status, { error: { code, message, details, request_id: requestId } }, headers);
  }

  function success(res, route, id, body) {
    if (route === "send") return send(res, 200, accepted(1));
    if (route === "batch") return send(res, 200, { data: (body?.emails ?? []).map((_, i) => accepted(i + 1)) });
    if (id === "msg_1") return send(res, 200, EMAIL);
    return error(res, 404, "not_found", "Email not found.");
  }

  const server = http.createServer((req, res) => {
    let raw = "";
    req.on("data", (chunk) => (raw += chunk));
    req.on("end", () => {
      const url = new URL(req.url, "http://localhost");
      if (req.method === "POST" && url.pathname === "/__reset") {
        requests = [];
        attempts = {};
        return send(res, 200, { ok: true });
      }
      if (req.method === "GET" && url.pathname === "/__requests") {
        return send(res, 200, requests);
      }

      let body = null;
      try {
        body = raw ? JSON.parse(raw) : null;
      } catch {
        body = raw;
      }
      requests.push({ method: req.method, path: url.pathname, headers: req.headers, body, at: Date.now() });

      let route = null;
      let id = null;
      if (req.method === "POST" && url.pathname === "/v1/send/emails") route = "send";
      else if (req.method === "POST" && url.pathname === "/v1/send/emails/batch") route = "batch";
      else if (req.method === "GET" && url.pathname.startsWith("/v1/send/emails/")) {
        route = "get";
        id = decodeURIComponent(url.pathname.slice("/v1/send/emails/".length));
      }
      if (route === null) return error(res, 404, "not_found", "Not Found");

      const auth = req.headers.authorization ?? "";
      if (!auth.startsWith("Bearer mhs_")) {
        return error(res, 401, "invalid_api_key", "Send a Mailhive Send API key as 'Authorization: Bearer mhs_…'.");
      }
      const scenario = auth.slice("Bearer mhs_".length);
      const attempt = (attempts[scenario] = (attempts[scenario] ?? 0) + 1);

      switch (scenario) {
        case "ok":
          return success(res, route, id, body);
        case "rate_limited_once":
          if (attempt === 1) {
            return error(res, 429, "rate_limited", "Too many requests — slow down and retry in a minute.", {
              headers: { "Retry-After": "1", "RateLimit-Limit": "600", "RateLimit-Remaining": "0", "RateLimit-Reset": "1" },
            });
          }
          return success(res, route, id, body);
        case "server_error_once":
          if (attempt === 1) {
            return error(res, 503, "maintenance", "Down for maintenance.", { headers: { "Retry-After": "0" } });
          }
          return success(res, route, id, body);
        case "server_error_always":
          return error(res, 500, "internal_server_error", "An unexpected error occurred", { headers: { "Retry-After": "0" } });
        case "bad_gateway_once":
          if (attempt === 1) return send(res, 502, "<html><body><h1>502 Bad Gateway</h1></body></html>");
          return success(res, route, id, body);
        case "network_error_once":
          if (attempt === 1) return req.socket.destroy();
          return success(res, route, id, body);
        case "validation":
          return error(res, 422, "validation_error", "Request validation failed", {
            details: [{ type: "missing", loc: ["body", "to"], msg: "Field required" }],
          });
        default: {
          const known = ERRORS[scenario];
          if (known) return error(res, known[0], known[1], known[2]);
          return error(res, 401, "invalid_api_key", "This API key doesn't exist or has been revoked.");
        }
      }
    });
  });
  return server;
}

/** Starts the server on a free port (or `port`); resolves to its base URL
 * (without /v1) and a close function. */
export function startMockServer(port = 0) {
  const server = createMockServer();
  return new Promise((resolve) => {
    server.listen(port, "127.0.0.1", () => {
      const { port: actual } = server.address();
      resolve({ url: `http://127.0.0.1:${actual}`, close: () => new Promise((done) => server.close(done)) });
    });
  });
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  const flag = process.argv.indexOf("--port");
  const port = flag > -1 ? Number(process.argv[flag + 1]) : 4010;
  startMockServer(port).then(({ url }) => console.log(`Mailhive mock server on ${url}`));
}
