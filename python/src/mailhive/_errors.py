"""Every error the SDK raises is a MailhiveError. Errors from the API carry
the HTTP status, Mailhive's stable error ``code`` and the ``request_id`` to
quote to support."""

from __future__ import annotations

from typing import Any, Mapping, Optional


class MailhiveError(Exception):
    """Base class for everything this SDK raises."""


class APIError(MailhiveError):
    """An error response from the API. Check ``code``, not ``message``."""

    def __init__(
        self,
        message: str,
        *,
        status: int,
        code: str,
        details: Any = None,
        request_id: Optional[str] = None,
        headers: Optional[Mapping[str, str]] = None,
    ) -> None:
        super().__init__(message)
        self.message = message
        self.status = status
        self.code = code
        self.details = details
        self.request_id = request_id
        self.headers = dict(headers or {})

    def __repr__(self) -> str:
        return f"{type(self).__name__}(status={self.status}, code={self.code!r}, request_id={self.request_id!r})"


class AuthenticationError(APIError):
    """401: missing, unknown or revoked API key."""


class BillingError(APIError):
    """402: Mailhive Send is paused over an unpaid invoice."""


class PermissionDeniedError(APIError):
    """403: Send isn't activated, or the stream is paused."""


class NotFoundError(APIError):
    """404"""


class ConflictError(APIError):
    """409: e.g. an Idempotency-Key reused for a different request."""


class ValidationError(APIError):
    """422: the request is invalid; ``details`` lists every problem."""


class RateLimitError(APIError):
    """429: ``rate_limited`` (retried for you), or ``monthly_quota_reached``
    / ``daily_cap_reached`` (not retried: waiting won't help)."""

    @property
    def retry_after(self) -> Optional[float]:
        value = {k.lower(): v for k, v in self.headers.items()}.get("retry-after")
        try:
            return float(value) if value not in (None, "") else None
        except ValueError:
            return None


class APIConnectionError(MailhiveError):
    """The API couldn't be reached, or didn't answer in time."""


class WebhookVerificationError(MailhiveError):
    """A webhook's signature didn't check out. ``reason`` is ``header``,
    ``timestamp`` or ``signature``."""

    def __init__(self, message: str, reason: str) -> None:
        super().__init__(message)
        self.reason = reason


_BY_STATUS = {
    400: ValidationError,
    401: AuthenticationError,
    402: BillingError,
    403: PermissionDeniedError,
    404: NotFoundError,
    409: ConflictError,
    422: ValidationError,
    429: RateLimitError,
}


def error_for(message: str, **kwargs: Any) -> APIError:
    return _BY_STATUS.get(kwargs["status"], APIError)(message, **kwargs)
