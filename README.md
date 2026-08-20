# Mizrachi Bank — user management API

A .NET 8 Web API exposing three operations — create a user, delete a user by id, validate a
username and password — over a repository pattern with three interchangeable stores.

The interesting part of this exercise is not the three endpoints. It is that several of the
security requirements are enforced by the *shape* of the code rather than by discipline, and
that each one has a test which was checked to fail when its control is removed.

---

## Run it

No database software to install, nothing to set up.

```bash
# A signing key is required and has no default. Any value of 32+ bytes will do locally.
dotnet user-secrets set "Jwt:SigningKey" "a-local-development-signing-key-32b+" --project src/Mizrachi.Api

dotnet run --project src/Mizrachi.Api
```

Swagger is at `/swagger` in Development, and only in Development.

### Choosing a store

By configuration, never by a code change (NFR-1.3):

```bash
dotnet run --project src/Mizrachi.Api --Persistence:Provider=InMemory
dotnet run --project src/Mizrachi.Api --Persistence:Provider=Sqlite   --Persistence:FilePath=./data/users.db
dotnet run --project src/Mizrachi.Api --Persistence:Provider=JsonFile --Persistence:FilePath=./data/users.json
```

`InMemory` starts clean every time. `Sqlite` and `JsonFile` survive a restart. An unrecognised
provider, a missing file path, or a missing signing key fails at startup with a message saying
what is wrong — not on the first request (NFR-1.4).

### Build and test

```bash
dotnet build
dotnet test     # 158 unit + 38 integration
```

---

## The endpoints

### `POST /api/users` — register

```json
{ "userName": "alice", "password": "a-long-enough-passphrase" }
```

| | |
|---|---|
| **201** | `{ "userId": "…", "userName": "alice" }`, plus a `Location` header |
| **400** | policy failure, naming the rule that failed in a `rule` field |
| **409** | the username is taken |
| **429** | rate limited — 5 per minute per address |

### `POST /api/users/validate` — validate credentials

```json
{ "userName": "alice", "password": "a-long-enough-passphrase" }
```

| | |
|---|---|
| **200** | `{ "userId", "userName", "token", "expiresAt" }` — the token authorises a later delete |
| **401** | wrong password **or** unknown username, answered identically |
| **429** | rate limited — 10 per minute per address |

### `DELETE /api/users/{userId}` — delete your own account

Requires `Authorization: Bearer <token>`.

| | |
|---|---|
| **204** | deleted |
| **401** | no token, or an invalid or expired one |
| **403** | the id is not yours — *identical whether or not that id exists* |
| **404** | your own account, already deleted |

Every response carries an `X-Correlation-Id` header, repeated in error bodies, so a caller's
report can be matched to the server log.

---

## Design

Four projects, referenced in one direction only:

| | |
|---|---|
| `Mizrachi.Domain` | the `User` entity and the rules constraining it. No I/O, **zero package references** |
| `Mizrachi.Application` | the three use cases and the ports they need. Every security decision lives here |
| `Mizrachi.Infrastructure` | the three stores, the hasher, the token issuer, and the composition root |
| `Mizrachi.Api` | HTTP only — routing, DTOs, status codes, middleware |

The API never sees an EF type; `UsersDbContext` and all three repositories are `internal`, and
switching provider is a configuration value.

### Requirements enforced by shape, not by care

These are the parts worth reviewing:

- **`IUserRepository` has no `ExistsAsync`.** Uniqueness is decided inside `TryAddAsync` by the
  datastore, so the check-then-insert race of FR-1.8 cannot be written against the port. Twenty
  concurrent inserts of one username yield exactly one success, in all three stores.
- **`ISecurityEventLog.AuthenticationFailed()` takes no parameters.** The submitted username must
  never be logged on a failed authentication (NFR-2.3); a method that cannot receive it cannot
  leak it.
- **`ValidateUserResult` has exactly one failure case.** Unknown username and wrong password must
  be indistinguishable (FR-3.5), so the distinction never leaves the service — no controller,
  logger or future maintainer can reveal what it does not have.
- **`DeleteUserService` compares caller to target before touching the repository.** Authorisation
  precedes existence (FR-2.4), so an id you do not own is refused identically whether or not it
  is real.
- **The unknown-username path still verifies a hash**, against a dummy computed at startup, so the
  work done does not depend on whether the account was found (FR-3.6).

### Passwords

PBKDF2-HMAC-SHA512 via the framework's `PasswordHasher<T>` in IdentityV3 format, with the
iteration count set explicitly to **210,000** — the framework default is 100,000, which is below
current OWASP guidance. Argon2id would be the better algorithm and is what production should use;
it was passed over here only because it means a third-party package in the credential path.
bcrypt was passed over because its 72-byte truncation collides with a 128-character allowance.

Policy is length-based with a deny-list and **no composition rules** (FR-5.4): rules like "must
contain a digit" narrow the search space rather than widen it, because people satisfy them
predictably.

---

## Known limitations

Deliberate, not overlooked. `REQUIREMENTS.md` §4 has the full list with what production would
need instead; these are the ones that would bite first:

- **The JSON store's uniqueness guarantee is process-local.** Within one process a semaphore makes
  the check and the insert atomic. Two processes over one file have no atomic compare-and-insert
  to appeal to. **SQLite is the durable provider to use**; the JSON store demonstrates that the
  repository port is genuinely provider-agnostic.
- **Usernames are ASCII only** (3–64 characters, letters, digits, `.`, `_`, `-`). This is load
  bearing rather than lazy: SQLite's `NOCASE` folds only ASCII while .NET's `OrdinalIgnoreCase`
  folds all of Unicode, so without the restriction a username could be taken in one store and
  free in another.
- **Tokens cannot be revoked.** They are short-lived (15 minutes) and self-contained. Production
  needs asymmetric signing with keys in a vault, plus a revocation path.
- **Rate limiting is per client address.** Weak against a distributed attack. It is not keyed on
  username on purpose — that would let anyone who knows a name exhaust its allowance and lock the
  owner out, which is the denial-of-service that account lockout was rejected for.
- **Deletion is a hard delete.** A bank generally cannot do this; anti-money-laundering and
  know-your-customer rules mandate retention that overrides an erasure request.
- **No multi-factor authentication, no password reset.** The reset flow is where most real
  authentication vulnerabilities live, and is the highest-risk area deliberately not built.
- **Audit events go to the application log**, not to an append-only tamper-evident store held
  separately from the application.

---

## Documents

| | |
|---|---|
| [`REQUIREMENTS.md`](REQUIREMENTS.md) | the agreed contract — what and why, with FR/NFR ids and open questions |
| [`PLAN.md`](PLAN.md) | the design, and the findings that changed it |
| [`TASKS.md`](TASKS.md) | the work broken into independently verifiable commits |
| [`CLAUDE.md`](CLAUDE.md) | the invariants and security rules the code is held to |

### Development history

The work was done in sessions with an AI assistant, and the transcripts are kept as a record of
how the decisions were reached — including the disagreements and the measurements that settled
them. The account email in the client banner has been redacted; they contain no keys, tokens or
credentials.

| | |
|---|---|
| [Pre-project](docs/transcripts/00-pre-project.md) | initial orientation |
| [Requirements interview](docs/transcripts/01-requirements-interview.md) | the scoping conversation `REQUIREMENTS.md` came from |
| [Setup and governance](docs/transcripts/02-setup-and-governance.md) | repository hardening and the review skill |

The implementation session — the design, the task breakdown, and tasks 1 to 16 — is not yet
exported. See the note at the end of `TASKS.md`.
