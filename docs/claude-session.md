# Development sessions

The work was done in conversation with an AI assistant (Claude Opus 5, via Claude Code). The
transcripts are kept because the reasoning behind the decisions — including the disagreements
and the measurements that settled them — is more useful than the decisions alone.

The account email has been redacted from every transcript. They contain no keys, tokens, or
credentials: the `SigningKey` mentions are prose about never committing one, the only literal
key is the `a-test-only-signing-key-of-at-least-32-bytes` constant that already lives in the
test projects, and the connection strings are `Data Source=app.db`.

| Session | What happened |
|---|---|
| [00 — Pre-project](transcripts/00-pre-project.md) | Orientation and repository hygiene before any code. |
| [01 — Requirements interview](transcripts/01-requirements-interview.md) | The scoping conversation that produced [`REQUIREMENTS.md`](../REQUIREMENTS.md): every FR/NFR, the three decisions in §3, the out-of-scope list in §4, and the five open questions in §5. |
| [02 — Setup and governance](transcripts/02-setup-and-governance.md) | Hardening `.gitignore` for credential paths, and building the `security-review` skill and its checklist. |
| [03 — Implementation](transcripts/03-implementation.md) | Design, hostile self-review, task breakdown, and tasks 1–16. |

## What the implementation session contains

- The design, written before any code, as [`PLAN.md`](../PLAN.md).
- A hostile self-review of that design. Nine findings accepted, five rejected with reasons.
  Four were settled by running an experiment rather than by argument — the `PrivateAssets`
  runtime break, the PBKDF2 iteration-count default, and SQLite's ASCII-only `NOCASE`.
- The breakdown into 16 independently verifiable commits, as [`TASKS.md`](../TASKS.md).
- Implementation, one task per commit.
- A closing review of the finished codebase, which found three blockers still open. They are
  listed under "Known gaps" in the [README](../README.md).
