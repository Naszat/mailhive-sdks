"""The Mailhive Send clients: Mailhive (sync) and AsyncMailhive."""

from __future__ import annotations

import asyncio
import base64
import json
import os
import platform
import random
import time
import uuid
from typing import Any, Dict, List, Mapping, Optional, Tuple

import httpx

from ._errors import APIConnectionError, APIError, MailhiveError, error_for
from ._types import AcceptedEmail, Email, SendEmailParams
from ._version import __version__
from ._webhooks import verify as _verify_webhook

DEFAULT_BASE_URL = "https://api.mailhive.africa/v1"
_MAX_RETRY_AFTER = 60.0


def _backoff(attempt: int) -> float:
    """About 0.5s, 1s, 2s … up to 8s, with jitter."""
    ceiling = min(8.0, 0.5 * 2**attempt)
    return ceiling / 2 + random.random() * ceiling / 2


def _retry_after(headers: Mapping[str, str]) -> Optional[float]:
    value = headers.get("retry-after")
    try:
        seconds = float(value) if value not in (None, "") else None
    except ValueError:
        return None
    return seconds if seconds is not None and seconds >= 0 else None


def _encode_email(params: Mapping[str, Any]) -> Dict[str, Any]:
    email = dict(params)
    if "from_" in email:
        email["from"] = email.pop("from_")
    attachments = email.get("attachments")
    if attachments:
        email["attachments"] = [
            {**a, "content": base64.b64encode(a["content"]).decode()} if isinstance(a.get("content"), (bytes, bytearray)) else dict(a)
            for a in attachments
        ]
    return email


class _Base:
    def __init__(
        self,
        api_key: Optional[str],
        base_url: Optional[str],
        timeout: float,
        max_retries: int,
    ) -> None:
        key = api_key or os.environ.get("MAILHIVE_API_KEY")
        if not key:
            raise MailhiveError(
                "No API key. Pass one to Mailhive(...) or set MAILHIVE_API_KEY. "
                "Create keys under Mailhive Send → API keys."
            )
        if key.startswith("mhp_"):
            raise MailhiveError(
                "That's a form's publishable key (mhp_…). The server SDK needs a secret API key (mhs_…) "
                "from Mailhive Send → API keys."
            )
        self._api_key = key
        self.base_url = (base_url or os.environ.get("MAILHIVE_BASE_URL") or DEFAULT_BASE_URL).rstrip("/")
        self.timeout = timeout
        self.max_retries = max(0, max_retries)

    def __repr__(self) -> str:  # never shows the key
        return f"{type(self).__name__}(base_url={self.base_url!r})"

    def _prepare(self, method: str, body: Any, idempotency_key: Optional[str]) -> Tuple[Dict[str, str], Optional[bytes]]:
        headers = {
            "Authorization": f"Bearer {self._api_key}",
            "Accept": "application/json",
            "User-Agent": f"mailhive-python/{__version__} python/{platform.python_version()}",
        }
        content = None
        if body is not None:
            headers["Content-Type"] = "application/json"
            content = json.dumps(body).encode()
        if method == "POST":
            # One key per call, reused on every retry: a retry never sends twice.
            headers["Idempotency-Key"] = idempotency_key or str(uuid.uuid4())
        return headers, content

    @staticmethod
    def _error(response: httpx.Response) -> APIError:
        try:
            error = response.json().get("error") or {}
        except (ValueError, AttributeError):
            error = {}
        return error_for(
            error.get("message") or f"HTTP {response.status_code} {response.reason_phrase}".strip(),
            status=response.status_code,
            code=error.get("code") or "http_error",
            details=error.get("details"),
            request_id=error.get("request_id") or response.headers.get("x-request-id"),
            headers=response.headers,
        )

    def _wait_before_retry(self, error: APIError, attempt: int) -> Optional[float]:
        """Seconds to wait before retrying, or None to give up. Only a rate
        limit and server errors are worth retrying."""
        if attempt >= self.max_retries:
            return None
        if not (error.status >= 500 or (error.status == 429 and error.code == "rate_limited")):
            return None
        wait = _retry_after({k.lower(): v for k, v in error.headers.items()})
        if wait is not None and wait > _MAX_RETRY_AFTER:
            return None
        return _backoff(attempt) if wait is None else wait


class Emails:
    def __init__(self, client: "Mailhive") -> None:
        self._client = client

    def send(
        self, params: Optional[SendEmailParams] = None, *, idempotency_key: Optional[str] = None, **fields: Any
    ) -> AcceptedEmail:
        """Sends one email. Pass the fields as a dict, or as keyword
        arguments with ``from_`` for ``from``."""
        return self._client._request("POST", "/send/emails", _encode_email({**(params or {}), **fields}), idempotency_key)

    def send_batch(self, emails: List[SendEmailParams], *, idempotency_key: Optional[str] = None) -> List[AcceptedEmail]:
        """Up to 100 independent emails; all are accepted, or none."""
        response = self._client._request(
            "POST", "/send/emails/batch", {"emails": [_encode_email(e) for e in emails]}, idempotency_key
        )
        return response["data"]

    def get(self, email_id: str) -> Email:
        return self._client._request("GET", f"/send/emails/{_quote(email_id)}", None, None)


class AsyncEmails:
    def __init__(self, client: "AsyncMailhive") -> None:
        self._client = client

    async def send(
        self, params: Optional[SendEmailParams] = None, *, idempotency_key: Optional[str] = None, **fields: Any
    ) -> AcceptedEmail:
        return await self._client._request("POST", "/send/emails", _encode_email({**(params or {}), **fields}), idempotency_key)

    async def send_batch(self, emails: List[SendEmailParams], *, idempotency_key: Optional[str] = None) -> List[AcceptedEmail]:
        response = await self._client._request(
            "POST", "/send/emails/batch", {"emails": [_encode_email(e) for e in emails]}, idempotency_key
        )
        return response["data"]

    async def get(self, email_id: str) -> Email:
        return await self._client._request("GET", f"/send/emails/{_quote(email_id)}", None, None)


def _quote(value: str) -> str:
    from urllib.parse import quote

    return quote(value, safe="")


class _Webhooks:
    @staticmethod
    def verify(payload, signature, secret, *, tolerance: int = 300, now: Optional[float] = None) -> Dict[str, Any]:
        return _verify_webhook(payload, signature, secret, tolerance=tolerance, now=now)


class Mailhive(_Base):
    """The Mailhive Send API client.

    ``Mailhive()`` reads MAILHIVE_API_KEY. Use it as a context manager, or
    call ``close()``, to release its connections."""

    webhooks = _Webhooks()

    def __init__(
        self,
        api_key: Optional[str] = None,
        *,
        base_url: Optional[str] = None,
        timeout: float = 30.0,
        max_retries: int = 2,
        http_client: Optional[httpx.Client] = None,
    ) -> None:
        super().__init__(api_key, base_url, timeout, max_retries)
        self._http = http_client or httpx.Client(timeout=timeout)
        self._owns_http = http_client is None
        self.emails = Emails(self)

    def _request(self, method: str, path: str, body: Any, idempotency_key: Optional[str]) -> Any:
        headers, content = self._prepare(method, body, idempotency_key)
        url = f"{self.base_url}{path}"
        attempt = 0
        while True:
            try:
                response = self._http.request(method, url, headers=headers, content=content, timeout=self.timeout)
            except httpx.TransportError as exc:
                if attempt < self.max_retries:
                    time.sleep(_backoff(attempt))
                    attempt += 1
                    continue
                raise APIConnectionError(_connection_message(exc, self)) from exc
            if response.is_success:
                return response.json()
            error = self._error(response)
            wait = self._wait_before_retry(error, attempt)
            if wait is None:
                raise error
            time.sleep(wait)
            attempt += 1

    def close(self) -> None:
        if self._owns_http:
            self._http.close()

    def __enter__(self) -> "Mailhive":
        return self

    def __exit__(self, *exc: Any) -> None:
        self.close()


class AsyncMailhive(_Base):
    """The async client: ``await client.emails.send(...)``."""

    webhooks = _Webhooks()

    def __init__(
        self,
        api_key: Optional[str] = None,
        *,
        base_url: Optional[str] = None,
        timeout: float = 30.0,
        max_retries: int = 2,
        http_client: Optional[httpx.AsyncClient] = None,
    ) -> None:
        super().__init__(api_key, base_url, timeout, max_retries)
        self._http = http_client or httpx.AsyncClient(timeout=timeout)
        self._owns_http = http_client is None
        self.emails = AsyncEmails(self)

    async def _request(self, method: str, path: str, body: Any, idempotency_key: Optional[str]) -> Any:
        headers, content = self._prepare(method, body, idempotency_key)
        url = f"{self.base_url}{path}"
        attempt = 0
        while True:
            try:
                response = await self._http.request(method, url, headers=headers, content=content, timeout=self.timeout)
            except httpx.TransportError as exc:
                if attempt < self.max_retries:
                    await asyncio.sleep(_backoff(attempt))
                    attempt += 1
                    continue
                raise APIConnectionError(_connection_message(exc, self)) from exc
            if response.is_success:
                return response.json()
            error = self._error(response)
            wait = self._wait_before_retry(error, attempt)
            if wait is None:
                raise error
            await asyncio.sleep(wait)
            attempt += 1

    async def aclose(self) -> None:
        if self._owns_http:
            await self._http.aclose()

    async def __aenter__(self) -> "AsyncMailhive":
        return self

    async def __aexit__(self, *exc: Any) -> None:
        await self.aclose()


def _connection_message(exc: Exception, client: _Base) -> str:
    if isinstance(exc, httpx.TimeoutException):
        return f"The Mailhive API didn't answer within {client.timeout:g} seconds."
    return f"Couldn't reach the Mailhive API at {client.base_url}."
