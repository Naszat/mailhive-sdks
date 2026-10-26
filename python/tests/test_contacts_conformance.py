"""spec/conformance.json "contactCases": audiences and contacts, run against
the shared mock server by every SDK that implements them."""

from __future__ import annotations

import asyncio
import time

import pytest

import mailhive
from tests.conftest import load_spec
from tests.test_conformance import KINDS

SPEC = load_spec("conformance.json")["contactCases"]
AUDIENCE = SPEC["audienceId"]


def fixture(value):
    """A string naming a fixture stands for it; anything else is itself."""
    return SPEC[value] if isinstance(value, str) and value in SPEC else value


def call(client, case):
    """Returns the SDK call for a case: a function, or a coroutine function for the async client."""
    a = case.get("args", {})
    name = case["call"]
    if name == "audiences.list":
        return client.audiences.list()
    if name == "audiences.create":
        return client.audiences.create(a["name"])
    if name == "audiences.rename":
        return client.audiences.rename(a["id"], a["name"])
    if name == "contacts.add":
        return client.contacts.add(AUDIENCE, fixture(a["contact"]))
    if name == "contacts.upsert":
        contact = dict(fixture(a["contact"]))
        if a.get("doubleOptIn"):
            contact["double_opt_in"] = fixture(a["doubleOptIn"])
        return client.contacts.upsert(AUDIENCE, contact)
    if name == "contacts.upsertBatch":
        return client.contacts.upsert_batch(AUDIENCE, [fixture(c) if isinstance(c, str) else c for c in a["contacts"]])
    if name == "contacts.list":
        return client.contacts.list(
            AUDIENCE, email=a.get("email"), status=a.get("status"), page=a.get("page"), page_size=a.get("pageSize")
        )
    if name == "contacts.get":
        return client.contacts.get(a["id"])
    if name == "contacts.update":
        return client.contacts.update(a["id"], a["changes"])
    if name == "contacts.unsubscribe":
        return client.contacts.unsubscribe(a["id"])
    if name == "contacts.delete":
        return client.contacts.delete(a["id"])
    raise AssertionError(name)


def run_sync(case, url):
    with mailhive.Mailhive(case["key"], base_url=f"{url}/v1", max_retries=2) as client:
        return call(client, case)


def run_async(case, url):
    async def go():
        async with mailhive.AsyncMailhive(case["key"], base_url=f"{url}/v1", max_retries=2) as client:
            return await call(client, case)

    return asyncio.run(go())


def at(value, path):
    for part in path.split("."):
        value = value[int(part)] if isinstance(value, list) else value[part]
    return value


@pytest.mark.parametrize("runner", [run_sync, run_async], ids=["sync", "async"])
@pytest.mark.parametrize("case", SPEC["cases"], ids=[c["name"] for c in SPEC["cases"]])
def test_contact_conformance(case, runner, mock):
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
    for path, expected in want.get("resultFields", {}).items():
        assert at(result, path) == expected, path
    if "resultCount" in want:
        assert len(result) == want["resultCount"]
    if want.get("resultIsEmpty"):
        assert result is None
    if "minElapsedMs" in want:
        assert elapsed_ms >= want["minElapsedMs"]
    if "error" in want:
        expected = want["error"]
        assert isinstance(error, KINDS[expected["kind"]]), repr(error)
        assert error.status == expected["status"] and error.code == expected["code"]
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
            "noIdempotencyKey": lambda v: "idempotency-key" not in headers,
            "query": lambda v: first["query"] == v,
            "body": lambda v: first["body"] == fixture(v),
            "bodyIncludes": lambda v: {k: first["body"][k] for k in v} == v,
            "bodyContactCount": lambda v: len(first["body"]["contacts"]) == v,
        }
        for key, value in request.items():
            assert checks[key](value), (key, value, first)
