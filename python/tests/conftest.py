from __future__ import annotations

import json
import subprocess
from pathlib import Path

import httpx
import pytest

SPEC = Path(__file__).resolve().parents[2] / "spec"
MOCK_SERVER = Path(__file__).resolve().parents[2] / "mock-server" / "server.mjs"


def load_spec(name: str) -> dict:
    return json.loads((SPEC / name).read_text())


@pytest.fixture(scope="session")
def mock_server():
    """The shared mock API (mock-server/server.mjs), on a free port."""
    process = subprocess.Popen(["node", str(MOCK_SERVER), "--port", "0"], stdout=subprocess.PIPE, text=True)
    line = process.stdout.readline()
    url = line.strip().rsplit(" ", 1)[-1]
    yield url
    process.terminate()
    process.wait(timeout=5)


@pytest.fixture()
def mock(mock_server):
    httpx.post(f"{mock_server}/__reset")

    class Mock:
        url = mock_server

        @staticmethod
        def requests() -> list:
            return httpx.get(f"{mock_server}/__requests").json()

    return Mock
