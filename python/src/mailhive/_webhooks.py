"""Checking Mailhive-Signature on webhooks."""

from __future__ import annotations

import hashlib
import hmac
import json
import re
import time
from typing import Any, Dict, List, Optional, Union

from ._errors import WebhookVerificationError

_V1 = re.compile(r"^[0-9a-fA-F]{64}$")


def _parse(header: str) -> Optional[tuple]:
    timestamp: Optional[int] = None
    signatures: List[str] = []
    for part in header.split(","):
        key, sep, value = part.partition("=")
        if not sep:
            continue
        key, value = key.strip(), value.strip()
        if key == "t" and value.isdigit():
            timestamp = int(value)
        elif key == "v1" and _V1.match(value):
            signatures.append(value.lower())
    if timestamp is None or not signatures:
        return None
    return timestamp, signatures


def verify(
    payload: Union[str, bytes],
    signature: Optional[str],
    secret: str,
    *,
    tolerance: int = 300,
    now: Optional[float] = None,
) -> Dict[str, Any]:
    """Checks a webhook's ``Mailhive-Signature`` header (HMAC-SHA256 of
    ``"<t>.<raw body>"`` with the endpoint's secret) and returns the parsed
    event. ``payload`` must be the raw body exactly as received: parsing and
    re-serializing it changes the bytes. Several ``v1=`` values are accepted,
    so secrets can be rotated. Raises WebhookVerificationError."""
    if isinstance(payload, (dict, list)):
        raise TypeError(
            "verify() needs the raw request body (str or bytes), not parsed JSON: "
            "re-serializing changes the bytes, so the signature can't match."
        )
    body = payload.encode() if isinstance(payload, str) else bytes(payload)
    parsed = _parse(signature) if signature else None
    if parsed is None:
        raise WebhookVerificationError("Missing or malformed Mailhive-Signature header.", "header")
    timestamp, signatures = parsed
    current = time.time() if now is None else now
    if abs(current - timestamp) > tolerance:
        raise WebhookVerificationError(
            f"The webhook's timestamp is more than {tolerance} seconds from now; it may be a replay.", "timestamp"
        )
    expected = hmac.new(secret.encode(), f"{timestamp}.".encode() + body, hashlib.sha256).hexdigest()
    if not any(hmac.compare_digest(expected, candidate) for candidate in signatures):
        raise WebhookVerificationError(
            "The webhook's signature doesn't match. Check the endpoint's signing secret.", "signature"
        )
    return json.loads(body)
