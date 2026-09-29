from __future__ import annotations

import json

import django
import httpx
import pytest
from django.conf import settings

if not settings.configured:
    settings.configure(
        EMAIL_BACKEND="mailhive.django.EmailBackend",
        MAILHIVE_API_KEY="mhs_django",
        DEFAULT_FROM_EMAIL="Acme <hello@acme.com>",
    )
    django.setup()

from django.core import mail  # noqa: E402

import mailhive._client as client_module  # noqa: E402


@pytest.fixture()
def api(monkeypatch):
    """Every client the backend makes talks to a fake API."""
    sent = []

    def handler(request):
        body = json.loads(request.content)
        sent.append({"body": body, "headers": dict(request.headers)})
        return httpx.Response(200, json={"id": f"m{len(sent)}", "status": "queued", "suppressed": [], "test": False})

    real = client_module.httpx.Client
    monkeypatch.setattr(client_module.httpx, "Client", lambda **kw: real(transport=httpx.MockTransport(handler), **kw))
    return sent


def test_send_mail_goes_through_mailhive(api):
    assert mail.send_mail("Hi", "Plain body", None, ["ada@example.com"], html_message="<p>HTML body</p>") == 1
    body = api[0]["body"]
    assert body == {
        "from": "Acme <hello@acme.com>",
        "to": ["ada@example.com"],
        "subject": "Hi",
        "text": "Plain body",
        "html": "<p>HTML body</p>",
    }
    assert api[0]["headers"]["authorization"] == "Bearer mhs_django"


def test_everything_an_email_message_carries(api):
    message = mail.EmailMessage(
        "Invoice", "See attached", "billing@acme.com", ["ada@example.com"],
        cc=["accounts@example.com"], bcc=["audit@acme.com"], reply_to=["support@acme.com"], headers={"X-Order": "1042"},
    )
    message.attach("invoice.txt", "Paid", "text/plain")
    message.mailhive_tags = {"type": "invoice"}
    message.mailhive_idempotency_key = "order-1042"
    message.send()
    body = api[0]["body"]
    assert body["cc"] == ["accounts@example.com"] and body["bcc"] == ["audit@acme.com"]
    assert body["reply_to"] == ["support@acme.com"] and body["headers"] == {"X-Order": "1042"}
    assert body["attachments"] == [{"filename": "invoice.txt", "content": "UGFpZA==", "content_type": "text/plain"}]
    assert body["tags"] == {"type": "invoice"}
    assert api[0]["headers"]["idempotency-key"] == "order-1042"


def test_html_only_messages(api):
    message = mail.EmailMessage("Hi", "<b>Bold</b>", None, ["ada@example.com"])
    message.content_subtype = "html"
    message.send()
    assert api[0]["body"]["html"] == "<b>Bold</b>" and "text" not in api[0]["body"]


def test_one_connection_sends_many(api):
    with mail.get_connection() as connection:
        sent = connection.send_messages(
            [mail.EmailMessage("A", "a", None, ["a@example.com"]), mail.EmailMessage("B", "b", None, ["b@example.com"])]
        )
    assert sent == 2 and len(api) == 2


def test_options_can_come_from_the_mailer_configuration(api):
    from mailhive.django import EmailBackend

    backend = EmailBackend(api_key="mhs_from_options", base_url="https://api-beta.mailhive.africa/v1", alias="default")
    backend.send_messages([mail.EmailMessage("Hi", "x", None, ["ada@example.com"])])
    assert api[0]["headers"]["authorization"] == "Bearer mhs_from_options"


def test_errors_raise_unless_fail_silently(monkeypatch):
    real = client_module.httpx.Client
    fail = lambda request: httpx.Response(422, json={"error": {"code": "domain_not_verified", "message": "no"}})  # noqa: E731
    monkeypatch.setattr(client_module.httpx, "Client", lambda **kw: real(transport=httpx.MockTransport(fail), **kw))
    with pytest.raises(client_module.MailhiveError):
        mail.send_mail("Hi", "x", None, ["ada@example.com"])
    assert mail.send_mail("Hi", "x", None, ["ada@example.com"], fail_silently=True) == 0
