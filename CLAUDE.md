# Branching and release flow

`feature/<name>` → `develop` → `main`. `develop` is the default branch. The SDKs have no beta branch: they reach the beta API through the base-URL override (`MAILHIVE_BASE_URL=https://api-beta.mailhive.africa/v1`).

- **New work:** start on a feature branch cut from an up-to-date `develop`: `git fetch origin && git switch -c feature/<name> origin/develop`. Never commit straight to `develop` or `main`.
- **Finished work** is merged into `develop`. It is merged into `main` only when the user asks.
- **Releases** are tagged from `main`, one tag per SDK: `node-v0.1.0`, `python-v0.1.0`, and so on. The tag triggers that SDK's publish workflow. Publish only when the user asks.
- **Before any push,** run `git fetch origin`. `git rev-list --left-right --count HEAD...origin/<branch>` must read `N 0`. Never force-push `develop` or `main`.
- **Commits** are atomic and follow Conventional Commits, scoped by SDK (`feat(node): …`, `fix(python): …`, `chore(spec): …`). They carry **no AI-attribution trailer**.

## Before every commit

Run the checks for every SDK you touched, and commit only when they pass:

- Node: `cd node && npm run typecheck && npm test && npm run build`
- Browser client: `cd client && npm run typecheck && npm test && npm run build`
- Python: `cd python && .venv/bin/python -m pytest -q` (needs `node` on PATH for the mock server; create the venv with `python3 -m venv .venv && .venv/bin/pip install -e '.[django]' pytest`)
- PHP: `cd php && composer install && vendor/bin/phpunit` (also needs `node`). The `laravel` suite needs PHP 8.2+. Don't name a core test `Laravel*`: the core suite excludes `tests/Laravel/`

Any change to `spec/` or `mock-server/` must pass in **every** SDK.
