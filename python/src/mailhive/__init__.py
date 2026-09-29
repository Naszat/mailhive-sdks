"""The official Python SDK for Mailhive Send.

    from mailhive import Mailhive

    client = Mailhive()  # reads MAILHIVE_API_KEY
    client.emails.send({"from": "hello@acme.com", "to": "ada@example.com", "subject": "Hi", "text": "Hello"})
"""

from ._client import DEFAULT_BASE_URL, AsyncMailhive, Mailhive
from ._errors import (
    APIConnectionError,
    APIError,
    AuthenticationError,
    BillingError,
    ConflictError,
    MailhiveError,
    NotFoundError,
    PermissionDeniedError,
    RateLimitError,
    ValidationError,
    WebhookVerificationError,
)
from ._types import AcceptedEmail, Attachment, Email, SendEmailParams
from ._version import __version__
from ._webhooks import verify as verify_webhook

__all__ = [
    "DEFAULT_BASE_URL",
    "APIConnectionError",
    "APIError",
    "AcceptedEmail",
    "AsyncMailhive",
    "Attachment",
    "AuthenticationError",
    "BillingError",
    "ConflictError",
    "Email",
    "Mailhive",
    "MailhiveError",
    "NotFoundError",
    "PermissionDeniedError",
    "RateLimitError",
    "SendEmailParams",
    "ValidationError",
    "WebhookVerificationError",
    "__version__",
    "verify_webhook",
]
