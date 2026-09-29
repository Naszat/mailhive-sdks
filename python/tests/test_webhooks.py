from __future__ import annotations

import pytest

import mailhive
from tests.conftest import load_spec

VECTORS = load_spec("webhook-vectors.json")


@pytest.mark.parametrize("vector", VECTORS["cases"], ids=[v["name"] for v in VECTORS["cases"]])
def test_vectors_signed_by_the_backend(vector):
    verify = lambda: mailhive.verify_webhook(vector["payload"], vector["header"], vector["secret"], now=vector["now"])  # noqa: E731
    if vector["valid"]:
        assert verify()["type"] == "email.delivered"
    else:
        with pytest.raises(mailhive.WebhookVerificationError) as caught:
            verify()
        assert caught.value.reason == vector["reason"]


def test_bytes_bodies_work_and_parsed_json_is_refused():
    v = VECTORS["cases"][0]
    assert mailhive.verify_webhook(v["payload"].encode(), v["header"], v["secret"], now=v["now"])
    with pytest.raises(TypeError, match="raw request body"):
        mailhive.verify_webhook({"type": "x"}, v["header"], v["secret"], now=v["now"])
    assert mailhive.Mailhive("mhs_x").webhooks.verify(v["payload"], v["header"], v["secret"], now=v["now"])
