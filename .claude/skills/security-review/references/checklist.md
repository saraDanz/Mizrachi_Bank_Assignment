# Checklist

`!` marks a CLAUDE.md invariant — a violation is an automatic FAIL at minimum High
severity, however unexploitable it looks.

**SEC-1 Secrets and config**
- 1.1! No secret, key, or credential in any committed file; signing keys from user-secrets or environment only (NFR-2.6).
- 1.2 Committed `appsettings*.json` holds placeholders only; `.gitignore` still covers `appsettings.Development.json`, `*.user`, `.vs/`, and any secret path this change adds.
- 1.3 No hard-coded fallback (`?? "dev-key"`) — missing config fails at startup, not at first request (NFR-1.4). Exactly one carve-out, §3.5/NFR-1.5: an *absent* `Jwt:SigningKey`, in Development only, is filled with a random in-memory key and warned about. A **constant** fallback, one that applies **outside** Development, one that replaces a **configured-but-too-short** key, or one that writes or logs the value, is still a finding.

**SEC-2 Password handling**
- 2.1! No hand-written crypto; hashing goes through the framework hasher behind our abstraction (NFR-2.2).
- 2.2 No `MD5`/`SHA*`/raw `HMAC`/manual salt concatenation for storage — plain `SHA*` over a password is Critical (NFR-2.1).
- 2.3 Plaintext lives only in the request DTO and the hasher call — never on an entity, in a log scope, cache, or static.
- 2.4 Verification uses the hasher's verify API, never `==` on hash strings.
- 2.5 Policy enforced server-side: min 12, max 128, no composition rules, deny-list, reject password equal to username (FR-5.1–5.7).
- 2.6 The 128 cap is enforced *before* hashing — unbounded input is CPU exhaustion against an iterated KDF (FR-5.2).
- 2.7 Rejection names the failed rule without echoing the password (FR-5.7).

**SEC-3 Authentication and enumeration**
- 3.1! Failed validation returns 401 with a byte-identical body whether or not the account exists — no "user not found" branch, no differing `title`/`detail` (FR-3.5).
- 3.2! Unknown username still verifies against a fixed dummy hash — no early `return Unauthorized()` on the lookup miss (FR-3.6).
- 3.3 No timing side channel beyond the hash: no extra log, round-trip, or `await` on one path only.
- 3.4! Credentials in the request body only — no `[FromQuery]`/`[FromRoute]` on a password or auth username, never in a URL (FR-3.2).
- 3.5 Validate and create are rate limited (NFR-2.4, OQ-3).
- 3.6 Username trimmed, compared case-insensitively, uniqueness enforced by a datastore constraint — an existence check before insert is a race (Critical) (FR-1.5–1.8).
- 3.7 Duplicate username returns 409, distinct from 400 and 401 (FR-1.7).

**SEC-4 Authorization**
- 4.1! Delete evaluates authorization *before* existence: an unowned id returns 403, never 404, never a different body (FR-2.3, FR-2.4).
- 4.2 Ownership comes from the authenticated principal, never from a client-supplied body, header, or route value.
- 4.3 Every authenticated endpoint carries `[Authorize]`; `[AllowAnonymous]` appears only on create and validate (FR-1.10, FR-2.2, FR-3.7).
- 4.4 Deleting an already-deleted own account returns 404, not 204 (FR-2.5, FR-2.6).

**SEC-5 Issued credential**
- 5.1 Token carries no password or hash, and nothing beyond subject, username, lifetime.
- 5.2 `ValidateIssuer`/`Audience`/`Lifetime`/`IssuerSigningKey` all true, algorithm pinned — no `alg: none`, no unpinned algorithm.
- 5.3 Symmetric key ≥ 256 bits, sourced per SEC-1.1; `RequireHttpsMetadata` not disabled outside Development.
- 5.4 Lifetime short and explicit; no non-expiring credential (FR-3.3, OQ-4).

**SEC-6 Responses and error surface**
- 6.1! No response type declares a password or hash member, on any path (FR-1.4, FR-4.1).
- 6.2 No endpoint returns a domain entity — response DTOs only; returning the entity is how a hash escapes.
- 6.3! Outside Development, errors carry no stack trace, exception type, or datastore detail; a global handler yields generic ProblemDetails (FR-4.3).
- 6.4 One consistent machine-readable error shape (FR-4.2), each response carrying a correlation id that matches the log (FR-4.4).
- 6.5! Swagger is registered *and* mapped only when `IsDevelopment()` (NFR-2.7).
- 6.6 HTTPS redirection and HSTS on outside Development; no `AllowAnyOrigin` together with credentials.

**SEC-7 Logging and audit**
- 7.1! No password, token, or request body is ever logged (NFR-2.3).
- 7.2! No submitted username logged on *failed* authentication — it may be a mistyped near-miss credential (NFR-2.3).
- 7.3 Structured parameters, not interpolated strings that could sweep in a DTO's `ToString()`.
- 7.4 Security events recorded — created, deleted, auth succeeded, auth failed, authorization refused — with correlation id and, where safe, the *authenticated* subject id (NFR-2.5).
- 7.5 No leftover `Console.WriteLine`/`Debug.WriteLine` of request data.

**SEC-8 Input and persistence**
- 8.1 Request DTOs cannot bind `UserId` — no client-supplied id, no over-posting (FR-1.2).
- 8.2 Username has an enforced length bound and character set (OQ-2).
- 8.3 File store: path from config and validated — no user-controlled segment, no traversal; writes atomic (temp + replace) and concurrency-safe.
- 8.4 No SQL concatenation or `FromSqlRaw` with interpolated input — parameterized only.
- 8.5 Delete is a real hard delete, leaving no plaintext or hash residue (FR-2.7).

**SEC-9 Architecture invariants**
- 9.1! Schema is exactly `UserId`, `UserName`, `UserPassword` — no fourth field.
- 9.2! Domain project has zero `PackageReference` entries.
- 9.3! EF refs use `PrivateAssets="compile"` — **not `"all"`**, which withholds the runtime
  assets too, so the API builds clean, ships zero EF assemblies and throws
  `FileNotFoundException` at first use (measured under OQ-6, see `CLAUDE.md`). DbContext is
  `internal`; the API does not compile against EF types.
- 9.4! Provider selection is configuration-driven — no `#if`, no code edit to switch store (NFR-1.3).
- 9.5 Target is `net8.0`; no new package without a stated reason, no unvetted crypto package.

**SEC-10 Test evidence**
- 10.1! Security behaviours are tests: no-password-in-response, 403-on-non-owner, hash-on-unknown-user. A new security behaviour ships with its test (NFR-3.1).
- 10.2 Every repository implementation runs the same contract suite (NFR-3.2).
- 10.3 No mocking library, no FluentAssertions — hand-written fakes, plain xUnit asserts.
- 10.4 No test disabled, skipped, or weakened to get green.

## Greps

Run these; don't answer from memory.

```bash
git diff HEAD | grep -inE "(password|secret|apikey|api_key|token|connectionstring|bearer|private[_-]?key)[[:space:]]*[=:]"   # SEC-1
git diff HEAD | grep -inE "Log(Information|Debug|Trace|Warning|Error).*(password|body|request|username)"                     # SEC-7
grep -rnE "AllowAnonymous|\[Authorize" --include=*.cs .                                                                      # SEC-4
grep -rnE "(class|record) +[A-Za-z]*(Response|Dto|Result)" --include=*.cs -A15 . | grep -inE "password|hash|salt"             # SEC-6
grep -rn "PackageReference" --include=*.csproj .                                                                             # SEC-9
```
