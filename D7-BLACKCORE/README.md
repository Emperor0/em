# D7 BLACKCORE

Public distribution repository for D7 BLACKCORE.

- Runtime secrets, browser sessions, memory, logs, screenshots and local settings are intentionally excluded from GitHub.
- The application stores sensitive/local state under `%LOCALAPPDATA%\\D7BLACKCORE`.
- Online updates are driven by `D7-BLACKCORE/releases/update.json` and verified with SHA-256 before install.
- Release package: `D7-BLACKCORE/releases/D7_BLACKCORE_v1.0.0.zip`.

## Security note
Never commit API keys, tokens, browser profiles, `.env` files, SQLite memory databases, or local BLACKCORE settings.
