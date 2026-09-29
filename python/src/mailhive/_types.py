"""Request and response shapes. Keys match the REST API exactly, so the API
reference applies as written (``from`` is a Python keyword, hence the
functional TypedDict syntax)."""

from __future__ import annotations

import sys
from typing import Dict, List, Optional, Union

if sys.version_info >= (3, 11):
    from typing import NotRequired, TypedDict
else:  # pragma: no cover
    from typing_extensions import NotRequired, TypedDict

Recipients = Union[str, List[str]]


class Attachment(TypedDict):
    filename: str
    #: The file: base64 text, or bytes (encoded for you).
    content: Union[str, bytes]
    content_type: NotRequired[str]


SendEmailParams = TypedDict(
    "SendEmailParams",
    {
        "from": str,
        "to": Recipients,
        "cc": NotRequired[Recipients],
        "bcc": NotRequired[Recipients],
        "subject": NotRequired[str],
        "html": NotRequired[str],
        "text": NotRequired[str],
        "template_id": NotRequired[str],
        "variables": NotRequired[Dict[str, Union[str, int, float, bool, None]]],
        "reply_to": NotRequired[Recipients],
        "headers": NotRequired[Dict[str, str]],
        "tags": NotRequired[Dict[str, str]],
        "attachments": NotRequired[List[Attachment]],
    },
)


class AcceptedEmail(TypedDict):
    id: str
    status: str
    suppressed: List[str]
    #: True when sent with a test key (mhs_test_…): delivery is simulated.
    test: bool


Email = TypedDict(
    "Email",
    {
        "id": str,
        "status": str,
        "stream": str,
        "test": bool,
        "from": str,
        "to": List[str],
        "cc": List[str],
        "bcc": List[str],
        "subject": str,
        "suppressed": List[str],
        "tags": Dict[str, str],
        "template_id": Optional[str],
        "template_version": Optional[int],
        "created_at": Optional[str],
        "sent_at": Optional[str],
        "last_event_at": Optional[str],
    },
)
