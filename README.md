# Mizrachi Bank — user management API

A .NET 8 Web API with three operations — create a user, delete a user by id, validate a username
and password — over a repository pattern with three interchangeable stores.

The three endpoints are not the interesting part. What is worth reviewing is that several
security requirements are enforced by the *shape* of the code rather than by discipline, and that
each one has a test which was checked to fail when its control is removed.

---

## Running it

Nothing to install beyond the .NET 8 SDK: no database server, no Docker, no setup script.

**1. Generate a signing key.** There is no default and no committed value, so the API will not
start without one. Any 32 or more bytes of random data will do.

```powershell
# Windows PowerShell
$b = [byte[]]::new(48)
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
[Convert]::ToBase64String($b)
```

```bash
# bash / macOS / Linux
openssl rand -base64 48
```

**2. Store it in User Secrets.** These live in your user profile, outside the working tree, so
there is nothing in the repository to commit by accident:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<the value from step 1>" --project src/Mizrachi.Api
```

**3. Run it.**

```bash
dotnet run --project src/Mizrachi.Api
```

Swagger is at `/swagger`, in Development only.

If you skip step 2, the API stops at startup instead of falling back to a default. The failure
names the missing key and the command that sets it, and never prints the value.

User Secrets are a Development-only configuration source. To run outside Development, supply the
same key as an environment variable — `Jwt__SigningKey`, with a double underscore, maps to the
`Jwt:SigningKey` configuration key:

```powershell
$env:Jwt__SigningKey = "<the value from step 1>"
```

Do not put the key in `appsettings.json` or `appsettings.Development.json`. Neither has a slot for
it, `appsettings.Development.json` is git-ignored, and a committed signing key is a compromised
signing key.

### Choosing a store

By configuration, never by a code change (NFR-1.3):

```bash
dotnet run --project src/Mizrachi.Api --Persistence:Provider=InMemory
dotnet run --project src/Mizrachi.Api --Persistence:Provider=Sqlite   --Persistence:FilePath=./data/users.db
dotnet run --project src/Mizrachi.Api --Persistence:Provider=JsonFile --Persistence:FilePath=./data/users.json
```

`InMemory` starts clean each time; the other two survive a restart. An unrecognised provider, a
missing file path, or a missing signing key **fails at startup** with a message naming the
problem — not on the first request (NFR-1.4).

### Testing

```bash
dotnet build
dotnet test          # 167 unit + 44 integration
```

The unit suite includes a repository contract suite run three times, once per store, so a store
that passes is interchangeable with the others.

### The endpoints

| | | |
|---|---|---|
| `POST` | `/api/users` | **201** created · **400** policy failure, naming the failed rule · **409** username taken · **429** rate limited |
| `POST` | `/api/users/validate` | **200** with a bearer token · **401** wrong password *or* unknown username, answered identically · **429** rate limited |
| `DELETE` | `/api/users/{userId}` | **204** deleted · **401** no or invalid token · **403** not your id, *identical whether or not it exists* · **404** your own, already deleted |

Every response carries an `X-Correlation-Id` header, repeated in error bodies, so a caller's
report can be tied to the server log.

---

## Architecture

Four projects, referenced in one direction only.

| Project | Holds | Depends on |
|---|---|---|
| `Mizrachi.Domain` | the `User` entity and the rules constraining it — password policy, username policy. No I/O. **Zero package references.** | nothing |
| `Mizrachi.Application` | the three use cases and the ports they need. Every security decision lives here. | Domain |
| `Mizrachi.Infrastructure` | the three stores, password hasher, token issuer, and the composition root | Application |
| `Mizrachi.Api` | HTTP only — routing, DTOs, status codes, middleware | Application, Infrastructure |

The API project never sees an EF type. `UsersDbContext` and all three repositories are
`internal`, reachable only through a single `AddInfrastructure(configuration)` call.

### Why the repository pattern here

It earns its place for one specific reason, and it is not testability.

The specification allows "an in-memory, file, or other database", and NFR-1.1 and NFR-1.2 pull in
opposite directions: the API must run on a clean machine with nothing installed, *and*
demonstrate that data survives a restart. That is two stores minimum, chosen at runtime.

A port is the only way to satisfy both without the choice leaking into the use cases. But the
port's real work is enforcing a requirement, not enabling a swap:

> **`IUserRepository` has no `ExistsAsync`.**

Uniqueness is decided inside `TryAddAsync`, by the datastore, and returned as a bool. FR-1.8
requires that under simultaneous requests for the same username exactly one succeeds, *guaranteed
by the datastore and not by a prior check*. Because the port offers no way to ask "does this
exist", the check-then-insert race cannot be written against it — not "should not be", **cannot
be**. Every store then proves it: the contract suite fires twenty concurrent inserts of one
username at each provider and asserts exactly one wins.

The same interface makes the SQLite unique index, the in-memory `ConcurrentDictionary.TryAdd`,
and the JSON store's semaphore-guarded section all answer the same question the same way. That
is what the abstraction is for.

---

## Security decisions

### Password hashing: PBKDF2-HMAC-SHA512, 210,000 iterations

Via the framework's `PasswordHasher<T>` in IdentityV3 format, behind our own `IPasswordHasher`
so no other layer names a hashing library.

**Why not Argon2id**, which is the better algorithm: .NET has no in-box implementation, so it
means a third-party package sitting in the credential path of a banking exercise. That is a
harder thing to defend at review than a slower KDF. In production, with time to vet the
dependency, Argon2id is the right choice — it is memory-hard, so GPU and ASIC attacks scale far
worse against it.

**Why not bcrypt**: it truncates at 72 bytes. FR-5.2 allows 128 characters and FR-5.3 allows any
character, so two passwords differing only past byte 72 would validate against each other. There
is a test asserting exactly this does not happen.

**Why 210,000 and not the default**: the framework default is 100,000, measured on this machine,
which is below current OWASP guidance for SHA-512. It is set explicitly, with the number in one
named constant.

The policy itself is length-based with a deny-list and **no composition rules** (FR-5.4). Rules
like "must contain a digit" narrow the search space rather than widen it, because people satisfy
them predictably — `Password1!` clears most corporate policies. Length and a deny-list remove the
passwords attackers actually try first.

### The DTO boundary: the entity never crosses it

`User` carries `UserPassword` because the specified schema says so, and that name cannot change.
Which is precisely why the entity must not reach the wire: returning it is how a stored hash
escapes.

Three rules, each with a test:

- **No response type declares a credential member.** Asserted by reflection over every type
  reachable from a controller action, plus a raw-JSON scan for a sentinel password across every
  endpoint's happy path — so a future DTO that adds a `PasswordHash` property fails the build's
  tests, not a reviewer's attention.
- **`User` is never a declared response type.** A separate assertion, because the first one would
  pass if someone returned the entity from an action typed as `IActionResult`.
- **Request DTOs override `ToString()`** to return only their type name. A stray interpolated log
  line or an unhandled-exception dump then cannot spill a password.

### Validation responses reveal nothing about which accounts exist

FR-3.5 requires a wrong password and an unknown username to be indistinguishable. Three things
make that true, and the third is the one that lasts:

1. **Both paths do the same work.** On a lookup miss, the service verifies the submitted password
   against a dummy hash computed at startup, then discards the result. One repository lookup and
   one hash verification happen either way (FR-3.6).
2. **The response body is fixed.** Same status, same title, same detail. The only field that
   varies is the request-scoped correlation id, which is not derived from account state.
3. **The service cannot express the difference.** `ValidateUserResult` has exactly one failure
   case, `Rejected`. The information about *which* failure occurred never leaves the service,
   because there is no case to carry it — so no controller, logger, or future maintainer can leak
   what they do not have.

The same reasoning shapes deletion. `DeleteUserService` compares caller to target **before
touching the repository at all**, so an id you do not own is refused identically whether or not
it is real (FR-2.4). Authorization is a gate in front of the lookup, not a filter applied to its
result.

Registration is the deliberate exception: a 409 does tell you a username is taken. That is
unavoidable for self-service sign-up — you cannot ask someone to pick a unique name without
telling them when one is taken — and it is recorded as an accepted trade-off in
`REQUIREMENTS.md` §3.2, mitigated only by rate limiting.

---

## Development process

Requirements first, code last, with every stage written down before the next began.

1. **Requirements interview** → [`REQUIREMENTS.md`](REQUIREMENTS.md). A scoping conversation, not
   a guess: functional and non-functional requirements with ids, three recorded decisions with
   their reasoning, an explicit out-of-scope list saying what production would need instead, and
   five open questions left open rather than assumed away.
2. **Architecture plan** → [`PLAN.md`](PLAN.md). Layers, every interface signature, the entity/DTO
   boundary, each endpoint's status codes, and the testing strategy — written and reviewed before
   any implementation.
3. **Hostile self-review of the plan.** Nine findings accepted, five rejected with a one-line
   reason each. Four were settled by running an experiment instead of arguing: `PrivateAssets="all"`
   was measured breaking the app at runtime, the PBKDF2 default was measured at 100,000 rather
   than the 210,000 claimed, and SQLite's `NOCASE` was measured folding ASCII only — which is why
   usernames are ASCII-restricted today.
4. **Task breakdown** → [`TASKS.md`](TASKS.md). Sixteen tasks, each touching as few files as
   possible, each independently buildable and testable, each with an explicit done condition.
5. **One task per commit**, against a written checklist ([`.claude/skills/security-review/`](.claude/skills/security-review/SKILL.md))
   that every commit was checked against: no credential in a response type, no password or token
   in a log, authorization before existence, hash verification on unknown users, no secret in a
   committed file.

Transcripts of all four sessions, indexed in [`docs/claude-session.md`](docs/claude-session.md): the three design sessions that preceded implementation, and the implementation session itself.

> **Honest note on step 5.** The checklist is real and every commit was reviewed against it, but
> the early commits were reviewed by hand: the skill was authored during the same session and had
> never actually been loaded. It has since been run as a real pre-commit gate, and its first
> genuine pass found two defects in the checklist itself — a `!` invariant that contradicted
> `CLAUDE.md`, and a secrets grep that did not match this project's most sensitive setting name.
> What is still missing is enforcement: nothing but discipline causes it to run. Making it
> automatic, in CI or a pre-commit hook, is the first item under "Known gaps" below.

---

## AI tooling decisions

### Adopted

| Tool | Why |
|---|---|
| **`CLAUDE.md`** | Project invariants and security rules in one file the assistant reads every session, so "never log a password" is a standing constraint rather than something re-explained each time. |
| **`security-review` skill** | A banking-grade checklist derived from `CLAUDE.md` and the requirements, with severity bands and a PASS/FAIL verdict, so pre-commit review is a fixed procedure instead of improvisation. |
| **Plan mode** | Design is proposed and approved before any file changes. Used for the architecture plan and again per task, which is what kept scope from creeping mid-task. |
| **Mutation testing of the security tests** | Each control was removed on purpose to confirm its test goes red. It caught one test that was passing vacuously and one leak the assertions did not cover. |

### Rejected

| Tool | Why not |
|---|---|
| **GitHub MCP server** | `git` and `gh` already work from the terminal. An extra protocol layer between the assistant and the repository adds a failure mode and an auth surface without adding a capability. |
| **Database MCP server** | There is no external database to connect to. The stores are an in-process dictionary, a local SQLite file, and a JSON file — all reachable through the code under test. |
| **`/ship` command** | Designed as a close-out sequence (build → test → security review → stage → commit) but **not built**: the sequence is short, already written down in `TASKS.md`, and automating it would have hidden the per-commit verification rather than making it visible. |

---

## Known gaps

Found in a review of the finished code, listed here rather than left for a reader to discover.

1. **No CI.** NFR-3.3 requires build, tests, and a dependency vulnerability check to run
   automatically. `.github/workflows/` is empty. A 211-test suite nobody is obliged to run is a
   suggestion, and this codebase's safety argument rests on those tests.
2. **Rate limiting is not proxy-aware.** It partitions on the connection's remote address with no
   forwarded-headers configuration, so behind a load balancer every caller shares one bucket.
3. **Re-hash on login is designed but not implemented.** `PasswordVerification.SuccessRehashNeeded`
   is produced and never consumed, so raising the iteration count later would silently leave
   existing accounts on the old one.
4. **FR-3.5's timing clause is argued, not measured.** No test compares response times.

---

## What I would add for production

The four gaps above are corrections. These are the things that are genuinely out of scope for a
take-home and would be non-negotiable for a real deployment.

| Area | Today | Production needs |
|---|---|---|
| **Rate limiting** | In-process fixed window, per address: 10/min on validate, 5/min on create. Not keyed on username, on purpose — that would let anyone who knows a name exhaust its allowance and lock the owner out. | A distributed limiter (Redis or the gateway) so limits hold across instances, proxy-aware client identification, and adaptive throttling on anomalous patterns rather than a fixed window. |
| **Account lockout** | None, deliberately. Lockout is itself a denial-of-service vector: an attacker with a username list can lock out every customer. Rate limiting was chosen instead. | Progressive delays, device and geo-velocity signals, impossible-travel detection, and step-up authentication on anomaly — never a fixed failure threshold. |
| **HTTPS** | Redirection and HSTS outside Development. | TLS terminated at the edge with modern ciphers only, HSTS preload, certificate pinning for first-party clients, and mTLS between internal services. Plaintext HTTP should never reach the application. |
| **Audit logging** | Security events — created, deleted, authentication succeeded and failed, authorization refused — to the application log, with correlation ids and no credentials. | An append-only, tamper-evident store held **separately from application logs**, because audit records must survive compromise of the application. Plus retention aligned to regulatory requirements and automated PII redaction on export. |
| **Secrets** | Signing key from user-secrets or environment, with no default and startup failure when absent. Nothing committed. | A managed vault or HSM, with short-lived dynamic credentials, automatic rotation, and asymmetric signing so the verifying service never holds a key that can mint tokens. |
| **Persistence** | SQLite or JSON file, schema created with `EnsureCreated`. | A managed RDBMS with versioned migrations, encryption at rest, read replicas, point-in-time recovery, and tested restores. The JSON store's uniqueness guarantee is process-local and it should not leave the demo. |

Beyond the table: multi-factor authentication on any account that can perform a destructive
operation, breached-password screening against a real corpus via a privacy-preserving lookup,
dual control on customer deletion, and soft-delete with an anonymisation schedule — a bank
generally cannot hard-delete a customer record, because anti-money-laundering retention overrides
an erasure request.

`REQUIREMENTS.md` §4 carries the full out-of-scope list with the reasoning for each.

---

## Documents

| | |
|---|---|
| [`REQUIREMENTS.md`](REQUIREMENTS.md) | the agreed contract — what and why, with ids and open questions |
| [`PLAN.md`](PLAN.md) | the design, and the findings that changed it |
| [`TASKS.md`](TASKS.md) | the work as independently verifiable commits |
| [`CLAUDE.md`](CLAUDE.md) | the invariants the code is held to |
| [`docs/claude-session.md`](docs/claude-session.md) | index of the [`docs/transcripts/`](docs/transcripts/) session transcripts — design and implementation |
