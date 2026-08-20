# Checklist

`!` marks a CLAUDE.md invariant — a violation is an automatic FAIL at minimum High
severity, however unexploitable it looks.

**SEC-1 Secrets and config**
- 1.1! No secret, key, or credential in any committed file; signing keys from user-secrets or environment only (NFR-2.6).
- 1.2 Committed `appsettings*.json` holds placeholders only; `.gitignore` still covers `appsettings.Development.json`, `*.user`, `.vs/`, and any secret path this change adds.
- 1.3 No hard-coded fallback (`?? "dev-key"`) — missing config fails at startup, not at first request (NFR-1.4).
