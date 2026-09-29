from __future__ import annotations

import base64
import json

import httpx
import pytest

import mailhive


def client_with(handler, **kwargs):
    return mailhive.Mailhive("mhs_x", http_client=httpx.Client(transport=httpx.MockTransport(handler)), **kwargs)


ACCEPTED = {"id": "m1", "status": "queued", "suppressed": [], "test": False}


def test_reads_the_key_and_base_url_from_the_environment(monkeypatch):
    monkeypatch.setenv("MAILHIVE_API_KEY", "mhs_fromenv")
    monkeypatch.setenv("MAILHIVE_BASE_URL", "https://api-beta.mailhive.africa/v1/")
    seen = {}

    def handler(request):
        seen.update(url=str(request.url), auth=request.headers["authorization"])
        return httpx.Response(200, json=ACCEPTED)

    mailhive.Mailhive(http_client=httpx.Client(transport=httpx.MockTransport(handler))).emails.send(
        {"from": "a@acme.com", "to": "b@example.com", "subject": "s", "text": "t"}
    )
    assert seen == {"url": "https://api-beta.mailhive.africa/v1/send/emails", "auth": "Bearer mhs_fromenv"}


def test_needs_a_key_and_refuses_a_form_key(monkeypatch):
    monkeypatch.delenv("MAILHIVE_API_KEY", raising=False)
    with pytest.raises(mailhive.MailhiveError, match="No API key"):
        mailhive.Mailhive()
    with pytest.raises(mailhive.MailhiveError, match="publishable key"):
        mailhive.Mailhive("mhp_form")


def test_never_shows_the_key():
    assert "secret" not in repr(mailhive.Mailhive("mhs_secretvalue"))


def test_keyword_arguments_and_byte_attachments():
    sent = {}

    def handler(request):
        sent.update(json.loads(request.content))
        return httpx.Response(200, json=ACCEPTED)

    client_with(handler).emails.send(
        from_="a@acme.com", to="b@example.com", subject="s", text="t",
        attachments=[{"filename": "a.txt", "content": b"hello"}, {"filename": "b.txt", "content": "aGk="}],
    )
    assert sent["from"] == "a@acme.com" and "from_" not in sent
    assert [a["content"] for a in sent["attachments"]] == [base64.b64encode(b"hello").decode(), "aGk="]


def test_gives_up_on_a_retry_after_longer_than_a_minute():
    calls = []

    def handler(request):
        calls.append(1)
        return httpx.Response(429, json={"error": {"code": "rate_limited", "message": "slow"}}, headers={"Retry-After": "3600"})

    with pytest.raises(mailhive.RateLimitError) as caught:
        client_with(handler).emails.get("m1")
    assert caught.value.retry_after == 3600 and len(calls) == 1


def test_a_timeout_is_a_connection_error():
    def handler(request):
        raise httpx.ReadTimeout("slow", request=request)

    with pytest.raises(mailhive.APIConnectionError, match="didn't answer within 5 seconds"):
        client_with(handler, timeout=5, max_retries=0).emails.get("m1")


def test_every_error_is_a_mailhive_error():
    handler = lambda request: httpx.Response(404, json={"error": {"code": "not_found", "message": "Email not found."}})  # noqa: E731
    with pytest.raises(mailhive.MailhiveError):
        client_with(handler).emails.get("x")


def test_version_matches_the_package():
    from importlib.metadata import version

    assert mailhive.__version__ == version("mailhive")
