"""Request and response shapes. Keys match the REST API exactly, so the API
reference applies as written (``from`` is a Python keyword, hence the
functional TypedDict syntax)."""

from __future__ import annotations

import sys
from typing import Any, Dict, List, Optional, Union

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
    #: "queued", or "suppressed" when no recipient was left after the
    #: suppression list and the recipient checks.
    status: str
    #: Recipients dropped because they're on the suppression list.
    suppressed: List[str]
    #: Recipients dropped before sending because the address can't work,
    #: keyed by address: "invalid_syntax", "domain_typo", "domain_not_found"
    #: or "no_mail_server". They never reach the sending engine, so they
    #: can't bounce. Empty when every recipient was fine.
    rejected: Dict[str, str]
    #: True when sent with a test key (mhs_test_…): delivery is simulated.
    test: bool


class RecipientVerdict(TypedDict):
    """One result of ``emails.validate``."""

    #: The address as given.
    email: str
    #: The bare, lowercased address, or None when there isn't one.
    normalized: Optional[str]
    #: False when sending to this address would be refused.
    valid: bool
    #: Why it's invalid: "invalid_syntax", "domain_typo", "domain_not_found",
    #: "no_mail_server" or "suppressed" (on your suppression list). None when valid.
    reason: Optional[str]
    #: The reason in plain words, fit to show the person typing the address.
    message: Optional[str]
    #: For "domain_typo", the address that was probably meant (ada@gmail.com for ada@gmial.com).
    suggestion: Optional[str]


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
        "rejected": Dict[str, str],
        "tags": Dict[str, str],
        "template_id": Optional[str],
        "template_version": Optional[int],
        "created_at": Optional[str],
        "sent_at": Optional[str],
        "last_event_at": Optional[str],
    },
)


# ---- audiences and contacts (an API key with Full access) ----


class Audience(TypedDict):
    id: str
    name: str
    created_at: NotRequired[str]
    #: People in the audience.
    contacts: NotRequired[int]
    #: Of those, who a broadcast would reach.
    mailable: NotRequired[int]


DoubleOptIn = TypedDict(
    "DoubleOptIn",
    {
        #: On a verified sending domain, e.g. "Acme <news@example.com>".
        "from": str,
        "subject": NotRequired[str],
    },
)
"""Email the person a confirmation link first: they're subscribed only if
they click it. Until then they're ``pending`` and never mailed."""


class ContactInput(TypedDict):
    email: str
    #: Leave out to keep it; "" clears it.
    first_name: NotRequired[Optional[str]]
    last_name: NotRequired[Optional[str]]
    #: Merged into the contact's fields; an empty value removes one.
    fields: NotRequired[Dict[str, str]]
    #: "subscribed" or "unsubscribed". Someone who unsubscribed or reported
    #: spam can't be set to subscribed: use double_opt_in.
    status: NotRequired[str]
    double_opt_in: NotRequired[DoubleOptIn]


class ContactUpdate(TypedDict, total=False):
    first_name: Optional[str]
    last_name: Optional[str]
    #: Replaces the contact's fields.
    fields: Dict[str, str]
    unsubscribe: bool


class Contact(TypedDict):
    id: str
    audience_id: str
    email: str
    first_name: Optional[str]
    last_name: Optional[str]
    fields: Dict[str, str]
    #: "subscribed", "unsubscribed" or "pending".
    status: str
    #: On the suppression list (bounce, spam complaint or unsubscribe): never mailed.
    suppressed: bool
    #: Subscribed and not suppressed: a broadcast would reach them.
    mailable: bool
    source: str
    created_at: str
    unsubscribed_at: Optional[str]
    #: True when a test key made the call: nothing was saved or sent.
    test: NotRequired[bool]


class ContactDetail(Contact):
    #: The latest emails sent to them.
    history: List[Email]


class ContactPage(TypedDict):
    items: List[Contact]
    page: int
    page_size: int
    total: int


class ContactBatchEntry(TypedDict):
    index: int
    email: str
    ok: bool
    created: NotRequired[bool]
    contact: NotRequired[Contact]
    error: NotRequired[Dict[str, Any]]


class ContactBatchResult(TypedDict):
    created: int
    updated: int
    failed: int
    #: One per contact sent, in order. A failure here doesn't raise.
    results: List[ContactBatchEntry]
