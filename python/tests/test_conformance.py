"""The shared conformance suite (spec/conformance.json): every Mailhive SDK
runs these same cases against the same mock server."""

from __future__ import annotations

import asyncio
import re
import time

import pytest

import mailhive
from tests.conftest import load_spec

SPEC = load_spec("conformance.json")
KINDS = {
    "api": mailhive.APIError,
    "authentication": mailhive.AuthenticationError,
    "billing": mailhive.BillingError,
    "permission": mailhive.PermissionDeniedError,
    "not_found": mailhive.NotFoundError,
    "conflict": mailhive.ConflictError,
    "validation": mailhive.ValidationError,
    "rate_limit": mailhive.RateLimitError,
}


def run_sync(case, url):
    client = mailhive.Mailhive(case["key"], base_url=f"{url}/v1", max_retries=2)
    email = dict(SPEC["email"])
    options = {"idempotency_key": case["idempotencyKey"]} if case.get("idempotencyKey") else {}
    with client:
        if case["call"] == "emails.send":
            return client.emails.send(email, **options)
        if case["call"] == "emails.sendBatch":
            return client.emails.send_batch([email, email], **options)
        return client.emails.get(case["id"])


def run_async(case, url):
    async def go():
        async with mailhive.AsyncMailhive(case["key"], base_url=f"{url}/v1", max_retries=2) as client:
            email = dict(SPEC["email"])
            options = {"idempotency_key": case["idempotencyKey"]} if case.get("idempotencyKey") else {}
            if case["call"] == "emails.send":
                return await client.emails.send(email, **options)
            if case["call"] == "emails.sendBatch":
                return await client.emails.send_batch([email, email], **options)
            return await client.emails.get(case["id"])

    return asyncio.run(go())


@pytest.mark.parametrize("runner", [run_sync, run_async], ids=["sync", "async"])
@pytest.mark.parametrize("case", SPEC["cases"], ids=[c["name"] for c in SPEC["cases"]])
def test_conformance(case, runner, mock):
    want = case["expect"]
    started = time.monotonic()
    result, error = None, None
    try:
        result = runner(case, mock.url)
    except mailhive.MailhiveError as caught:
        error = caught
    elapsed_ms = (time.monotonic() - started) * 1000
    requests = mock.requests()

    assert len(requests) == want["attempts"]
    for field in ("result", "resultFields"):
        if field in want:
            expected = want[field]
            if isinstance(expected, list):
                assert [{k: r[k] for k in e} for r, e in zip(result, expected)] == expected
            else:
                assert {k: result[k] for k in expected} == expected
    if "minElapsedMs" in want:
        assert elapsed_ms >= want["minElapsedMs"]
    if want.get("sameIdempotencyKey"):
        keys = {r["headers"].get("idempotency-key") for r in requests}
        assert len(keys) == 1 and None not in keys
    if "error" in want:
        expected = want["error"]
        assert isinstance(error, KINDS[expected["kind"]]), repr(error)
        assert error.status == expected["status"] and error.code == expected["code"]
        if "requestId" in expected:
            assert error.request_id == expected["requestId"]
        if expected.get("hasDetails"):
            assert error.details
    else:
        assert error is None, repr(error)

    request = want.get("request")
    if request:
        first = requests[0]
        headers = first["headers"]
        checks = {
            "method": lambda v: first["method"] == v,
            "path": lambda v: first["path"] == v,
            "authorization": lambda v: headers["authorization"] == v,
            "contentType": lambda v: headers["content-type"] == v,
            "userAgentPattern": lambda v: re.search(v, headers["user-agent"]),
            "idempotencyKeyPattern": lambda v: re.search(v, headers["idempotency-key"]),
            "idempotencyKey": lambda v: headers["idempotency-key"] == v,
            "noIdempotencyKey": lambda v: "idempotency-key" not in headers,
            "body": lambda v: first["body"] == v,
            "bodyEmailCount": lambda v: len(first["body"]["emails"]) == v,
        }
        for key, value in request.items():
            assert checks[key](value), (key, value, first)
