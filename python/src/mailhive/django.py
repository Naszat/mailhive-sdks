"""A Django email backend that sends through Mailhive Send.

    # settings.py, Django 6.1+
    MAILERS = {"default": {"BACKEND": "mailhive.django.EmailBackend", "OPTIONS": {"api_key": env("MAILHIVE_API_KEY")}}}

    # settings.py, earlier versions
    EMAIL_BACKEND = "mailhive.django.EmailBackend"
    MAILHIVE_API_KEY = env("MAILHIVE_API_KEY")   # or leave it in the environment

Everything Django sends then goes through the API: send_mail, EmailMessage,
EmailMultiAlternatives (the text/html alternative becomes ``html``),
attachments, cc, bcc, reply_to and extra headers. Mailhive features: set
``message.mailhive_tags = {...}`` or ``message.mailhive_idempotency_key``.
"""

from __future__ import annotations

from email.mime.base import MIMEBase
from typing import Any, Dict, List, Optional, Sequence

from django.conf import settings
from django.core.mail.backends.base import BaseEmailBackend
from django.core.mail.message import EmailMessage

from ._client import Mailhive
from ._errors import MailhiveError


def _attachment(item: Any) -> Dict[str, Any]:
    if isinstance(item, MIMEBase):
        return {
            "filename": item.get_filename() or "attachment",
            "content": item.get_payload(decode=True) or b"",
            "content_type": item.get_content_type(),
        }
    filename, content, mimetype = item[0], item[1], item[2] if len(item) > 2 else None
    data = content.encode() if isinstance(content, str) else bytes(content)
    return {"filename": filename or "attachment", "content": data, **({"content_type": mimetype} if mimetype else {})}


def message_params(message: EmailMessage) -> Dict[str, Any]:
    """The API request for one Django message."""
    params: Dict[str, Any] = {
        "from": message.from_email or settings.DEFAULT_FROM_EMAIL,
        "to": list(message.to),
        "subject": message.subject,
    }
    if message.cc:
        params["cc"] = list(message.cc)
    if message.bcc:
        params["bcc"] = list(message.bcc)
    if message.reply_to:
        params["reply_to"] = list(message.reply_to)
    if message.content_subtype == "html":
        params["html"] = message.body
    elif message.body:
        params["text"] = message.body
    for content, mimetype in getattr(message, "alternatives", None) or []:
        if mimetype == "text/html":
            params["html"] = content
    if message.extra_headers:
        params["headers"] = {k: str(v) for k, v in message.extra_headers.items()}
    if message.attachments:
        params["attachments"] = [_attachment(a) for a in message.attachments]
    tags = getattr(message, "mailhive_tags", None)
    if tags:
        params["tags"] = dict(tags)
    return params


class EmailBackend(BaseEmailBackend):
    def __init__(
        self,
        fail_silently: bool = False,
        *,
        api_key: Optional[str] = None,
        base_url: Optional[str] = None,
        **kwargs: Any,
    ) -> None:
        # fail_silently is kept here rather than passed up: Django 6.1+
        # deprecates it on the base class. alias and anything unknown go up,
        # so Django can report a misconfigured MAILERS entry.
        super().__init__(**kwargs)
        self.fail_silently = fail_silently
        self.api_key = api_key or getattr(settings, "MAILHIVE_API_KEY", None)
        self.base_url = base_url or getattr(settings, "MAILHIVE_BASE_URL", None)
        self._client: Optional[Mailhive] = None

    def open(self) -> bool:
        if self._client is not None:
            return False
        try:
            self._client = Mailhive(self.api_key, base_url=self.base_url)
        except MailhiveError:
            if not self.fail_silently:
                raise
            return False
        return True

    def close(self) -> None:
        if self._client is not None:
            self._client.close()
            self._client = None

    def send_messages(self, email_messages: Sequence[EmailMessage]) -> int:
        if not email_messages:
            return 0
        opened = self.open()
        if self._client is None:
            return 0
        sent = 0
        try:
            for message in email_messages:
                if not message.recipients():
                    continue
                try:
                    self._client.emails.send(
                        message_params(message), idempotency_key=getattr(message, "mailhive_idempotency_key", None)
                    )
                    sent += 1
                except MailhiveError:
                    if not self.fail_silently:
                        raise
        finally:
            if opened:
                self.close()
        return sent
