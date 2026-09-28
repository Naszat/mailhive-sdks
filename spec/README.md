# Shared specification

- `openapi.json`: the Mailhive Send API. It is a copy of `app/public/docs/mailhive-send-openapi.json` in the app repo, which is served at https://mailhive.africa/docs/mailhive-send-openapi.json. Update the copy whenever the published file changes. The `spec-sync` workflow reports any drift.
- `conformance.json`: behaviour every SDK must pass against `mock-server/server.mjs`.
- `webhook-vectors.json`: `Mailhive-Signature` test vectors, made with the backend's `app/services/send/webhooks.py` `sign()`. Regenerate them from the backend if the signing scheme ever changes.
