# Implementation plan — Mizrachi Bank user-management API

Design document for the three-endpoint user-management Web API. `REQUIREMENTS.md` is the
contract (what and why); this is the design (how). Where the two disagree,
`REQUIREMENTS.md` wins and this file gets corrected.

---

## Context

The repo is an unmodified ASP.NET Core 8 template: one project, a `WeatherForecastController`,
no tests, no domain code. This plan takes it to the agreed scope — create user, delete user
by UserId, validate user by username and password — over a repository pattern with three
interchangeable stores, with the contract's security properties expressed in types and
proven by tests rather than asserted in comments.

Settled by the scoping questions: **4 projects**, **all three stores** (in-memory, EF Core +
SQLite, JSON file), PBKDF2 for hashing.

---

## Revision log — self-review of the first draft

### Accepted

| # | Finding | Change |
|---|---|---|
| F1 | **`PrivateAssets="all"` on the EF packages breaks the app at runtime.** Measured: the Api project builds clean, gets **zero** EF assemblies in its output, and throws `FileNotFoundException: Microsoft.EntityFrameworkCore` at first use. `PrivateAssets="compile"` copies the 8 runtime assemblies, runs correctly, **and still fails the compile** if Api references an EF type (CS0234) — i.e. it achieves the invariant's stated purpose, which `all` does not. | Use `compile`; amend the CLAUDE.md invariant. **See OQ-6 — your call.** |
| F2 | **An `internal` DbContext forces the repository internal too.** A `public` class cannot take an `internal` type in a public constructor (CS0051). | All three repositories become `internal sealed`, reachable only through `AddInfrastructure`. Tests resolve `IUserRepository` from a built `ServiceProvider`, so the test project needs neither `InternalsVisibleTo` nor an EF reference. |
| F3 | **SQLite's `NOCASE` collation is ASCII-only, so the stores disagree on uniqueness.** Measured: `'ALICE'='alice' COLLATE NOCASE` → 1, but `'ÉLODIE'='élodie'` → **0**. `StringComparer.OrdinalIgnoreCase` (in-memory, JSON) folds both. Two stores would give different answers to "is this name taken", and an ASCII-only test suite would never notice. | Constrain usernames to ASCII (**answers OQ-2**), which makes all three stores agree by construction. Add a contract test using a non-ASCII pair. |
| F4 | **The stated work factor was wrong.** Measured: `PasswordHasherOptions.IterationCount` defaults to **100,000** in .NET 8 (`CompatibilityMode = IdentityV3`), not the 210,000 I quoted. | Set `IterationCount = 210_000` explicitly and document that it is a deliberate override of the default. |
| F5 | **Model-validation failures return a different body shape.** `[ApiController]` auto-400s emit `ValidationProblemDetails`, not our error shape — two error formats, breaking FR-4.2. | Unify via `ConfigureApiBehaviorOptions.InvalidModelStateResponseFactory`. |
| F6 | **The concurrency test will flake on SQLite.** 20 parallel writers against a default-journal SQLite file produce `SQLITE_BUSY`, not a clean unique-constraint violation. | WAL journal mode + busy timeout in the connection string; the test asserts one 201 and nineteen 409s, never a 500. |
| F7 | **"Byte-identical 401 bodies" was overstated.** The correlation id (FR-4.4) varies per request by design, so the bodies are never byte-identical. | Restate the property precisely: identical status and identical body *modulo the correlation id*, which is request-scoped and not derived from account state. The test normalises that one field and nothing else. |
| F8 | **The no-password reflection test was scoped too narrowly** — "types whose name ends `Response`" misses records, nested types, and anything returned indirectly. | Scan every type reachable from any controller action's declared return type, plus `User` itself. |
| F9 | `PasswordHasher<T>` is generic and needs a type argument. | `PasswordHasher<User>`; Infrastructure already references Domain. |

### Rejected

- **Merge the two test projects** — the split keeps unit tests free of the ASP.NET test host, and reads clearly to a reviewer.
- **Drop `IClock` as unused** — `JwtTokenIssuer` needs it, and the token-expiry test needs to control it.
- **Add `ExistsAsync` for a friendlier 409** — that is exactly the check-then-insert race FR-1.8 forbids.
- **Return 404 for an id that belongs to nobody** — FR-2.4 requires authorization to be evaluated before existence.
- **Store a hashed/normalised lookup key for consistent case-folding** — a fourth field in all but name, violating the schema invariant.

---

## 1. Project structure and layers

| Project | Responsibility | Packages |
|---|---|---|
| `Mizrachi.Domain` | The `User` entity and the pure rules constraining it — password and username policy — with no I/O. | **none** (invariant) |
| `Mizrachi.Application` | The three use cases and the ports they need; every security decision lives here as a result type. | none |
| `Mizrachi.Infrastructure` | Adapters: three repositories, hasher, token issuer, clock, id generator, security-event log, and the config-driven composition root. | EF Core + SQLite, Identity.Core, JwtBearer |
| `Mizrachi.Api` | HTTP only — routing, DTO mapping, status codes, middleware, auth wiring. Knows nothing of EF. | Swashbuckle, JwtBearer |
| `Mizrachi.Tests.Unit` | Domain rules, use cases against hand-written fakes, and the repository contract suite across all three stores. | xUnit |
| `Mizrachi.Tests.Integration` | End-to-end over real HTTP via `WebApplicationFactory`, including environment-dependent behaviour. | xUnit, Mvc.Testing |

References: `Api → Application → Domain`, `Infrastructure → Application → Domain`.
`Api → Infrastructure` exists solely to call `AddInfrastructure`.

The existing `Mizrachi_Bank_Assignment` project is renamed to `Mizrachi.Api` (folder, csproj,
`.sln` entry, namespaces); `WeatherForecast*` is deleted.

---

## 2. Interfaces and classes

### Domain

```
public sealed class User
    public Guid UserId { get; }
    public string UserName { get; }        // trimmed, original casing
    public string UserPassword { get; }    // hash only — never plaintext
    public static User Create(Guid userId, string userName, string passwordHash)

public sealed class PasswordPolicy
    public PasswordPolicy(IPasswordDenyList denyList)
    public PolicyResult Validate(string password, string userName)

public sealed class UserNamePolicy
    public PolicyResult Validate(string userName)
    public static string Normalize(string userName)      // trim only

public interface IPasswordDenyList
    bool Contains(string password)

public readonly record struct PolicyResult(bool IsValid, string? FailedRule, string? Reason)
    public static PolicyResult Ok()
    public static PolicyResult Fail(string rule, string reason)
```

`Normalize` trims only. Case-insensitivity is a *comparison* property each store enforces —
never a stored fourth field.

### Application — ports

```
public interface IUserRepository
    Task<User?> FindByUserNameAsync(string userName, CancellationToken ct)
    Task<User?> FindByIdAsync(Guid userId, CancellationToken ct)
    Task<bool>  TryAddAsync(User user, CancellationToken ct)      // false = name taken
    Task<bool>  DeleteAsync(Guid userId, CancellationToken ct)    // false = not found

public interface IPasswordHasher
    string Hash(string password)
    PasswordVerification Verify(string passwordHash, string password)

public enum PasswordVerification { Failed, Success, SuccessRehashNeeded }

public interface ITokenIssuer          { IssuedToken Issue(Guid userId, string userName); }
public readonly record struct IssuedToken(string Token, DateTimeOffset ExpiresAt)
public interface IIdGenerator          { Guid NewId(); }
public interface IClock                { DateTimeOffset UtcNow { get; } }

public interface ISecurityEventLog
    void UserCreated(Guid userId)
    void UserDeleted(Guid userId)
    void AuthenticationSucceeded(Guid userId)
    void AuthenticationFailed()                                  // no parameters, by design
    void AuthorizationRefused(Guid callerId, Guid targetUserId)
```

Two signatures do real work. `TryAddAsync` returning a bool *is* FR-1.8: with no
`ExistsAsync` on the port, a check-then-insert race cannot be written. `AuthenticationFailed()`
taking no arguments makes NFR-2.3 — never log the username on failed auth — a compile-time
property rather than a rule someone must remember.

### Application — use cases

```
public sealed class CreateUserService
    ctor(IUserRepository, IPasswordHasher, PasswordPolicy, UserNamePolicy, IIdGenerator, ISecurityEventLog)
    Task<CreateUserResult> ExecuteAsync(string userName, string password, CancellationToken ct)

public abstract record CreateUserResult
    sealed record Created(Guid UserId, string UserName)
    sealed record InvalidUserName(string Rule, string Reason)
    sealed record InvalidPassword(string Rule, string Reason)
    sealed record DuplicateUserName

public sealed class ValidateUserService
    ctor(IUserRepository, IPasswordHasher, ITokenIssuer, ISecurityEventLog)
    Task<ValidateUserResult> ExecuteAsync(string userName, string password, CancellationToken ct)

public abstract record ValidateUserResult
    sealed record Authenticated(Guid UserId, string UserName, IssuedToken Token)
    sealed record Rejected                                       // the ONLY failure case

public sealed class DeleteUserService
    ctor(IUserRepository, ISecurityEventLog)
    Task<DeleteUserResult> ExecuteAsync(Guid callerId, Guid targetUserId, CancellationToken ct)

public abstract record DeleteUserResult
    sealed record Deleted
    sealed record Forbidden
    sealed record NotFound
```

`ValidateUserResult` has exactly one failure case, so the controller is structurally
incapable of distinguishing unknown-user from wrong-password (FR-3.5) — that information
never leaves the service. `DeleteUserService` compares `callerId` to `targetUserId` and
returns `Forbidden` **before touching the repository at all** (FR-2.4): ownership is a gate
in front of the lookup, not a filter applied to its result.

### Infrastructure

```
internal sealed class InMemoryUserRepository : IUserRepository      // F2
internal sealed class SqliteUserRepository   : IUserRepository
internal sealed class JsonFileUserRepository : IUserRepository
internal sealed class UsersDbContext : DbContext                    // internal, per invariant

public sealed class AspNetPasswordHasher     : IPasswordHasher      // PasswordHasher<User>, 210k iterations
public sealed class JwtTokenIssuer           : ITokenIssuer
public sealed class SystemClock              : IClock
public sealed class GuidIdGenerator          : IIdGenerator
public sealed class EmbeddedPasswordDenyList : IPasswordDenyList    // embedded resource, no package
public sealed class LoggingSecurityEventLog  : ISecurityEventLog

public sealed class PersistenceOptions { public string Provider; public string? FilePath; }
public sealed class JwtOptions         { public string Issuer, Audience; public int LifetimeMinutes; }

public static class InfrastructureRegistration
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
```

`AddInfrastructure` switches on `Persistence:Provider` ∈ `InMemory | Sqlite | JsonFile`. An
unrecognised value, a missing SQLite path, or an absent signing key throws at startup
(NFR-1.4). This is the only place a provider is named — no `#if`, no code edit to switch
(NFR-1.3). SQLite connection string sets WAL and a busy timeout (F6).

### Api

```
[ApiController][Route("api/users")]
public sealed class UsersController : ControllerBase
    ctor(CreateUserService, ValidateUserService, DeleteUserService)
    [HttpPost]                    [AllowAnonymous] Task<IActionResult> Create(CreateUserRequest)
    [HttpPost("validate")]        [AllowAnonymous] Task<IActionResult> Validate(ValidateUserRequest)
    [HttpDelete("{userId:guid}")] [Authorize]      Task<IActionResult> Delete(Guid userId)

public sealed class CorrelationIdMiddleware
public sealed class ExceptionHandlingMiddleware
internal static class ApiProblemDetails      // one error shape, incl. model-validation 400s (F5)
```

---

## 3. The `User` entity vs the DTOs at the boundary

The entity is exactly the three contracted fields and never crosses the HTTP boundary in
either direction.

| | Members | Notes |
|---|---|---|
| `User` (Domain) | `UserId`, `UserName`, `UserPassword` | `UserPassword` holds a **hash**. Immutable, built only via `Create`. |
| `CreateUserRequest` | `UserName`, `Password` | No `UserId` member exists, so a client-supplied id cannot bind (FR-1.2). `[Required]`, `[StringLength(128)]`. |
| `CreateUserResponse` | `UserId`, `UserName` | |
| `ValidateUserRequest` | `UserName`, `Password` | Body only — no `[FromQuery]`/`[FromRoute]` anywhere (FR-3.2). |
| `ValidateUserResponse` | `UserId`, `UserName`, `Token`, `ExpiresAt` | |
| errors | RFC 7807 `ProblemDetails` + `correlationId` | One shape for every failure, including model validation. |

Delete has no request DTO: the target comes from the route, the caller from the token.

Three rules the boundary enforces:

- **No response type declares a password or hash member** (FR-1.4, FR-4.1) — asserted by the
  reflection test in §6, scoped per F8.
- Both request DTOs **override `ToString()`** to return the type name only, so an interpolated
  log line or an exception dump cannot spill a password (NFR-2.3).
- Mapping is explicit in the controller — no AutoMapper, no serialising the entity.

---

## 4. Password hashing — choice of algorithm

**Chosen: PBKDF2-HMAC-SHA512 via `Microsoft.AspNetCore.Identity.PasswordHasher<User>`
(`IdentityV3` format), with `IterationCount` explicitly set to 210,000, behind our own
`IPasswordHasher`.**

Why PBKDF2 over the alternatives:

- **Argon2id** is the better algorithm and would be my production choice — memory-hard, so
  GPU and ASIC attack scales far worse. .NET has no in-box implementation, so it means a
  third-party package (`Konscious.Security.Cryptography`, `Isopoh.Cryptography.Argon2`) sitting
  in the credential path of a bank take-home. Harder to justify at review than a slower KDF.
- **bcrypt** (`BCrypt.Net-Next`) is battle-tested but truncates at 72 bytes, which collides
  with FR-5.2's 128-character allowance and FR-5.3's "all characters permitted": two passwords
  differing only past byte 72 would both validate. An avoidable footgun.
- **PBKDF2** is FIPS-validated, in-box, accepted by NIST SP 800-63B, and already implemented
  to a vetted spec by the framework hasher — satisfying NFR-2.2 ("a vetted, maintained
  implementation") and CLAUDE.md's ban on hand-rolled crypto. Its weakness is that it is
  memory-cheap; the mitigation available to us is iteration count.

Consequences, stated rather than hidden:

- **The default is 100,000 iterations, not 210,000** (measured on this machine, .NET 8). We
  override it to 210,000 — the current OWASP figure for PBKDF2-HMAC-SHA512.
- The v3 format is **self-describing**: format marker, PRF, iteration count and salt are
  encoded in the stored string. Raising the work factor later needs no migration and no schema
  change; old hashes keep verifying and `Verify` returns `SuccessRehashNeeded`, at which point
  we re-hash inside the successful-login path only.
- We take `Microsoft.Extensions.Identity.Core` for the hasher type alone — **not** ASP.NET Core
  Identity. Application and Domain never see it.
- **Unknown-username path (FR-3.6):** `ValidateUserService` holds a dummy hash computed at
  startup from a random password with identical parameters. On a lookup miss it calls
  `Verify(dummyHash, submittedPassword)` and discards the outcome, so one PBKDF2 evaluation
  happens either way. This is why it is a service-level rule, not a controller-level one.
- **The 128-character cap is enforced before hashing** (FR-5.2): PBKDF2 over unbounded input
  is a CPU-exhaustion vector, and the hash is the expensive part.

---

## 5. Endpoints

### `POST /api/users` — create (anonymous, rate limited)

Request `{ "userName": "...", "password": "..." }`

| Outcome | Status | Body |
|---|---|---|
| Created | **201** + `Location: /api/users/{id}` | `{ userId, userName }` |
| Username fails policy | **400** | ProblemDetails naming the failed rule |
| Password fails policy | **400** | ProblemDetails naming the failed rule, never echoing the password (FR-5.7) |
| Username taken | **409** | ProblemDetails, distinct from 400 (FR-1.7) |
| Rate limited | **429** | ProblemDetails + `Retry-After` |

Duplicates are detected by the store's unique constraint through `TryAddAsync`, never by a
prior existence check. Concurrent identical requests: exactly one 201, the rest 409. The 409
does disclose that a username exists — an accepted, documented trade-off (`REQUIREMENTS.md`
§3.2), mitigated only by the rate limit.

### `POST /api/users/validate` — validate (anonymous, rate limited)

Request `{ "userName": "...", "password": "..." }`

| Outcome | Status | Body |
|---|---|---|
| Valid | **200** | `{ userId, userName, token, expiresAt }` |
| Unknown username | **401** | fixed ProblemDetails — identical to the row below |
| Wrong password | **401** | fixed ProblemDetails — identical to the row above |
| Missing/empty field, or password > 128 chars | **400** | rejected before any hashing; concerns the submitted value only, so it reveals nothing about any account |
| Rate limited | **429** | |

The two 401s share a status and a body; the only differing field is the request-scoped
correlation id, which is not derived from account state (F7). Failure logs the event with
**no username** (NFR-2.3); success logs the resolved `userId`.

### `DELETE /api/users/{userId:guid}` — delete (authenticated)

| Outcome | Status | Body |
|---|---|---|
| Own account deleted | **204** | empty |
| No / invalid / expired token | **401** | ProblemDetails |
| `userId` ≠ token subject | **403** | ProblemDetails — **identical whether or not that id exists** (FR-2.4) |
| Own account already deleted | **404** | ProblemDetails (FR-2.5, FR-2.6) |

Order is authorization → existence, enforced by the service's control flow rather than by
controller discipline.

### Cross-cutting

Correlation id on every response (FR-4.4) · one ProblemDetails shape everywhere including
model-validation failures (FR-4.2) · outside Development no stack trace or store detail in any
error (FR-4.3) · Swagger registered **and** mapped only under `IsDevelopment()` (NFR-2.7) ·
HTTPS redirect + HSTS outside Development · fixed-window rate limiter (`AddRateLimiter`, in-box
in .NET 8) on both anonymous endpoints.

---

## 6. Testing strategy

Hand-written fakes only, plain xUnit asserts, no mocking library, no FluentAssertions.

**Fakes** (`Tests.Unit/Fakes/`): `FakeUserRepository` (with a seam to force `TryAddAsync` to
lose the race), `CountingPasswordHasher` (records every `Hash`/`Verify` call), `FakeIdGenerator`,
`FakeClock`, `RecordingSecurityEventLog`, `RecordingLogger`.

**Unit — Domain:** password policy at every boundary (11/12/128/129 chars, deny-listed,
equal-to-username, spaces and non-ASCII accepted, no composition rule imposed); username policy
and trimming.

**Unit — Application:** each service's result cases against fakes, no HTTP involved.

**Repository contract suite:** one `abstract class UserRepositoryContractTests` with an abstract
factory and three concrete subclasses (NFR-3.2). Each subclass builds a `ServiceProvider` via
`AddInfrastructure` and resolves `IUserRepository`, so the test project touches neither EF nor
the internal repository types (F2). Covers round-trip, case-insensitive lookup and uniqueness
**including a non-ASCII pair** (F3), trim-on-store, delete-then-miss, and the concurrent-insert
race.

**Integration:** `WebApplicationFactory<Program>` over real HTTP against in-memory and SQLite
(temp file per fixture), plus a Production-environment factory for the environment-dependent
tests.

### The tests that prove the security properties

| # | Test | Proves | Method |
|---|---|---|---|
| 1 | `Responses_never_expose_a_password_member` | FR-1.4, FR-4.1 | Reflection over every type reachable from any controller action's return type, plus `User`: assert no member name contains `password`/`hash`/`salt`. Plus a raw-JSON substring assertion on each happy path. |
| 2 | `Delete_of_another_users_id_is_forbidden` | FR-2.3 | A deletes B → 403; B still validates afterwards. |
| 3 | `Delete_of_nonexistent_id_matches_delete_of_unowned_id` | **FR-2.4** | The 403 for a never-issued GUID compared field-for-field with the 403 for a real other user's id. Catches any "existence check first" regression. |
| 4 | `Unknown_username_still_verifies_a_hash` | **FR-3.6** | `CountingPasswordHasher.VerifyCount == 1` after validating a username that was never registered. |
| 5 | `Unknown_user_and_wrong_password_are_indistinguishable` | **FR-3.5** | Same status; bodies equal after normalising the correlation id, and no other field differs. |
| 6 | `Concurrent_creates_of_one_username_yield_exactly_one_201` | **FR-1.8** | 20 parallel POSTs via `Task.WhenAll`: one 201, nineteen 409s, zero 500s. Runs in the contract suite against all three stores. |
| 7 | `Failed_authentication_logs_no_username` | **NFR-2.3** | `RecordingLogger` scanned for the submitted username after a failure. Reinforced structurally by the parameterless `AuthenticationFailed()`. |
| 8 | `No_log_entry_contains_a_password_or_token` | NFR-2.3 | Whole-suite log scan for the known test passwords and issued tokens. |
| 9 | `Client_supplied_userId_is_ignored` | FR-1.2 | POST with an extra `"userId"` property; the returned id differs. |
| 10 | `Credentials_are_not_accepted_outside_the_body` | FR-3.2 | `GET /api/users/validate?userName=..&password=..` → 405, never 200. |
| 11 | `Swagger_is_absent_outside_Development` | NFR-2.7 | Production factory: `/swagger` → 404. |
| 12 | `Errors_outside_Development_carry_no_internals` | FR-4.3 | Provoke an unhandled exception; body has no exception type name and no stack frames. |
| 13 | `Password_over_128_chars_is_rejected_before_hashing` | FR-5.2 | `CountingPasswordHasher.HashCount == 0` after an over-length create. |
| 14 | `Stores_agree_on_case_folding_for_non_ASCII_usernames` | FR-1.5 (F3) | Contract suite: every store gives the same answer for `Élodie`/`élodie`. |

---

## 7. Open questions — answered

Three need **your** decision; the rest I will apply as recommended unless you say otherwise.

### Needs your decision

- **OQ-5 — Time budget.** Never established, and it is the constraint on how much of
  `REQUIREMENTS.md` §4 could be pulled into scope. **Only you can answer this.** Everything
  below assumes the scope as written, no more.
- **OQ-6 — `PrivateAssets="all"` on the EF packages.** Measured: it breaks the app at runtime
  (§Revision log F1). **Recommendation: change the CLAUDE.md invariant to
  `PrivateAssets="compile"`**, which blocks Api from compiling against EF — the invariant's
  actual purpose — while letting runtime assets flow. This edits an explicit written invariant,
  so I will not do it silently; the amendment goes in the same commit as the change.
- **OQ-10 — Is CI in scope?** NFR-3.3 requires build, tests, and a dependency vulnerability
  check in CI, but no provider has been agreed and it depends on OQ-5. **Recommendation:** a
  single GitHub Actions workflow running `dotnet build`, `dotnet test`, and
  `dotnet list package --vulnerable --include-transitive` — roughly 30 lines, and NFR-3.4
  already puts the repo on GitHub.

### My call, recorded here

- **OQ-1 — Self-service or administrative delete?** Keep **self-service**, per
  `REQUIREMENTS.md` §3.4: the administrative reading needs a role attribute, and the schema is
  fixed at three fields. Recorded as a considered disagreement, not an oversight; the README
  says so plainly.
- **OQ-2 — Username format.** **3–64 characters, ASCII `[A-Za-z0-9._-]`, trimmed, must start
  with a letter or digit.** This stopped being cosmetic once F3 showed SQLite and .NET disagree
  on non-ASCII case folding: restricting to ASCII makes all three stores agree by construction
  instead of by luck. The limitation is documented as a scope decision, not a claim that
  non-ASCII names are unsupportable.
- **OQ-3 — Rate limit creation too?** **Yes.** §3.2 leans on rate limiting to mitigate
  registration-based enumeration, so limiting only authentication was inconsistent.
- **OQ-4 — Token lifetime.** **15 minutes**, no refresh, no revocation (§4.7). Long enough to
  register-then-delete in a demo, short enough that a leaked token expires before it is useful.
- **OQ-7 — The JSON store cannot honour FR-1.8 across processes.** **Keep it, document the
  boundary.** In-process it is safe (semaphore + atomic temp-file replace); across processes
  there is no atomic compare-and-insert. It stays in the concurrency contract test — which
  passes single-process — and the README states that the JSON provider is a demonstration store
  whose uniqueness guarantee is process-local. SQLite is the durable provider to reach for.
- **OQ-8 — Rate-limit parameters.** **Fixed window, partitioned by client IP: 10/minute on
  validate, 5/minute on create.** Not by username: that recreates the lockout-by-proxy vector
  §4.6 deliberately avoided. Per-IP is weak against a distributed attack, which is why §4.5 and
  §4.9 name MFA and breached-password screening as the controls that actually matter.
- **OQ-9 — `UserPassword` names a field holding a hash.** **Keep the name** — it is fixed by
  the specified schema and the no-fourth-field invariant. Mitigated by an XML doc comment on
  the property, the `Create(… string passwordHash)` parameter name, and security test #1.

---

## Verification

1. `dotnet build` — clean, no new warnings (DoD 1).
2. `dotnet test` — all green, including the 14 security tests (DoD 2).
3. Provider switching without a code change (NFR-1.3), config only:
   `dotnet run --Persistence:Provider=InMemory`, `=Sqlite`, `=JsonFile`.
4. Restart durability (NFR-1.2): create a user under `Sqlite`, stop, restart, validate the same
   credentials → 200.
5. Startup failure (NFR-1.4): `--Persistence:Provider=Nonsense` fails at startup with a clear
   message, not at first request.
6. Manual probe of all three endpoints via the `.http` file, specifically the 403-before-404
   ordering and the two indistinguishable 401s.
7. The `security-review` skill returns PASS before every commit (DoD 3, CLAUDE.md Git gate).
