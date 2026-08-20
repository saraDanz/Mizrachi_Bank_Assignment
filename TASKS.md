# Tasks

Execution order for `PLAN.md`. Each task is one commit, touches as few files as possible,
depends only on earlier tasks, and has a done condition you can run.

**Applies to every task, not repeated below:** `dotnet build` clean with no new warnings ·
`dotnet test` green · the `security-review` skill returns PASS · conventional-commit message
citing the FR/NFR ids · explicit pathspec, nothing swept in (CLAUDE.md, Definition of Done).

**Blocked until answered:** OQ-6 gates task 8 — `PrivateAssets="all"` breaks the app at
runtime and the CLAUDE.md invariant needs amending to `"compile"` in that same commit.
OQ-5 and OQ-10 affect scope, not order; task 13 assumes CI is in.

---

- [ ] **1. Solution skeleton**
  Rename `Mizrachi_Bank_Assignment` → `Mizrachi.Api` (folder, csproj, `.sln`, namespaces).
  Add `Mizrachi.Domain`, `Mizrachi.Application`, `Mizrachi.Infrastructure`,
  `Mizrachi.Tests.Unit`, `Mizrachi.Tests.Integration`. Wire project references per PLAN §1.
  Delete `WeatherForecastController.cs` and `WeatherForecast.cs`.
  *Files:* `.sln`, 6 csproj, `Program.cs`, 2 deletions.
  **Done:** `dotnet build` succeeds on all 6 projects; `dotnet test` runs and reports 0 tests;
  `grep -rn PackageReference Mizrachi.Domain` returns nothing.
  `chore: restructure into four layers plus test projects`

- [ ] **2. Domain: entity and policy rules**
  `User`, `PolicyResult`, `IPasswordDenyList`, `UserNamePolicy`, `PasswordPolicy`.
  Username policy per OQ-2: 3–64 chars, ASCII `[A-Za-z0-9._-]`, must start alphanumeric,
  trimmed. Password policy per FR-5.1–5.7. No I/O, no packages.
  *Files:* 5 new in `Mizrachi.Domain`.
  **Done:** builds; Domain still has zero `PackageReference`; `User` exposes exactly
  `UserId`, `UserName`, `UserPassword`.
  `feat: add user entity and password/username policies (FR-5.1-5.7)`

- [ ] **3. Tests: domain policy boundaries**
  Password policy at 11/12/128/129 chars, deny-listed, equal-to-username, spaces and
  non-ASCII accepted, no composition rule imposed. Username policy accept/reject table and
  trimming. Plain xUnit asserts, no mocking library.
  *Files:* 2 new in `Mizrachi.Tests.Unit`.
  **Done:** every FR-5.x rule has at least one passing test on each side of its boundary.
  `test: cover password and username policy boundaries (FR-5.1-5.7)`

- [ ] **4. Application: ports and result types**
  `IUserRepository`, `IPasswordHasher` + `PasswordVerification`, `ITokenIssuer` +
  `IssuedToken`, `IIdGenerator`, `IClock`, `ISecurityEventLog`, and the three result
  hierarchies. Declarations only — no service logic yet.
  *Files:* ~8 new in `Mizrachi.Application`.
  **Done:** builds; `IUserRepository` has no `ExistsAsync` (FR-1.8);
  `ISecurityEventLog.AuthenticationFailed()` takes no parameters (NFR-2.3);
  `ValidateUserResult` has exactly one failure case (FR-3.5).
  `feat: add application ports and use-case result types`

- [ ] **5. Application: the three use-case services**
  `CreateUserService`, `ValidateUserService`, `DeleteUserService`. Validation ordering,
  the dummy-hash path for an unknown username, and the ownership gate that returns
  `Forbidden` before any repository call.
  *Files:* 3 new in `Mizrachi.Application`.
  **Done:** builds; `DeleteUserService` contains no repository call on the non-owner path —
  verified by reading the method top to bottom (FR-2.4).
  `feat: add create/validate/delete use cases (FR-1, FR-2, FR-3)`

- [ ] **6. Tests: use cases against hand-written fakes**
  `FakeUserRepository` (with a seam to lose the `TryAddAsync` race),
  `CountingPasswordHasher`, `FakeIdGenerator`, `FakeClock`, `RecordingSecurityEventLog`.
  Every result case of all three services.
  *Files:* ~5 fakes + 3 test classes in `Mizrachi.Tests.Unit`.
  **Done:** unknown username yields `VerifyCount == 1` (FR-3.6); non-owner delete returns
  `Forbidden` with `FindByIdAsync` never called (FR-2.4); duplicate insert yields
  `DuplicateUserName`.
  `test: cover use-case behaviour incl. FR-2.4 and FR-3.6`

- [ ] **7. Infrastructure: non-persistence adapters**
  `AspNetPasswordHasher` (`PasswordHasher<User>`, `IterationCount = 210_000` — the default
  is 100,000), `JwtTokenIssuer`, `SystemClock`, `GuidIdGenerator`,
  `EmbeddedPasswordDenyList` (embedded resource), `LoggingSecurityEventLog`, `JwtOptions`.
  Signing key from user-secrets or environment only.
  *Files:* ~7 new in `Mizrachi.Infrastructure` + one embedded deny-list resource.
  **Done:** a round-trip unit test hashes and verifies; a second asserts the stored hash
  contains neither the plaintext nor a fixed salt across two hashes of the same password;
  no secret in any committed file.
  `feat: add password hasher, token issuer and support adapters (NFR-2.1, NFR-2.2)`

- [ ] **8. Infrastructure: in-memory store and the configuration switch**
  `InMemoryUserRepository` (internal), `PersistenceOptions`, `AddInfrastructure` switching on
  `Persistence:Provider`, startup validation. **Amend the CLAUDE.md `PrivateAssets`
  invariant in this same commit** (OQ-6, scope change ⇒ same commit).
  *Files:* 3 new in `Mizrachi.Infrastructure`, `Program.cs`, `appsettings.json`, `CLAUDE.md`.
  **Done:** `--Persistence:Provider=Nonsense` fails at startup with a clear message, not at
  first request (NFR-1.4); `InMemory` starts clean; Api still cannot compile against EF.
  `feat: add in-memory store and config-driven provider selection (NFR-1.3, NFR-1.4)`

- [ ] **9. Tests: repository contract suite**
  `abstract class UserRepositoryContractTests` + the in-memory subclass. Round-trip,
  case-insensitive lookup and uniqueness **including `Élodie`/`élodie`**, trim-on-store,
  delete-then-miss, and 20-way concurrent `TryAddAsync`.
  Subclasses build a `ServiceProvider` via `AddInfrastructure` — the test project references
  neither EF nor the internal repository types.
  *Files:* 2 new in `Mizrachi.Tests.Unit`.
  **Done:** suite green against in-memory; concurrent insert yields exactly one `true`
  (FR-1.8).
  `test: add repository contract suite (NFR-3.2, FR-1.5-1.8)`

- [ ] **10. Infrastructure: SQLite store**
  `UsersDbContext` (internal), `SqliteUserRepository`, unique index with `NOCASE`, WAL and
  busy timeout, EF packages with `PrivateAssets="compile"`, schema created at startup.
  *Files:* 2 new in `Mizrachi.Infrastructure`, its csproj, one new contract subclass.
  **Done:** the whole contract suite passes against SQLite, concurrency test included;
  `--Persistence:Provider=Sqlite` runs; data survives a restart (NFR-1.2).
  `feat: add EF Core SQLite store (NFR-1.2, FR-1.8)`

- [ ] **11. Infrastructure: JSON file store**
  `JsonFileUserRepository` — semaphore-guarded, atomic temp-file replace, `OrdinalIgnoreCase`
  index, configured path validated (no traversal).
  *Files:* 1 new in `Mizrachi.Infrastructure`, one new contract subclass.
  **Done:** contract suite passes against JSON file; a killed process leaves no partial file;
  the process-local uniqueness boundary is recorded as a code comment for the README (OQ-7).
  `feat: add JSON file store (NFR-1.2)`

- [ ] **12. Api: DTOs, controller and authentication**
  `CreateUserRequest/Response`, `ValidateUserRequest/Response` (both requests overriding
  `ToString()`), `UsersController` with the three actions, JWT bearer wiring with all four
  `Validate*` flags on and the algorithm pinned.
  *Files:* 4 DTOs, 1 controller, `Program.cs`.
  **Done:** all three endpoints answer over HTTP with the status codes in PLAN §5; no
  response type declares a password or hash member; no credential binds from route or query.
  `feat: expose create/validate/delete endpoints (FR-1, FR-2, FR-3)`

- [ ] **13. Api: cross-cutting behaviour**
  Correlation id middleware, exception handling, one `ProblemDetails` shape including
  model-validation 400s, Swagger registered *and* mapped only under `IsDevelopment()`,
  HTTPS redirect + HSTS outside Development, per-IP rate limiter (10/min validate,
  5/min create).
  *Files:* 2 middleware, 1 problem-details helper, `Program.cs`.
  **Done:** every response carries a correlation id (FR-4.4); a forced exception returns
  generic ProblemDetails in Production (FR-4.3); an 11th validate attempt inside a minute
  returns 429 (NFR-2.4).
  `feat: add correlation ids, error shape, rate limiting and dev-only Swagger`

- [ ] **14. Tests: end-to-end integration**
  `WebApplicationFactory<Program>` fixtures for in-memory and SQLite (temp file per fixture),
  plus a Production-environment fixture. Happy paths only — register, validate, delete,
  re-validate.
  *Files:* 2 fixtures + 1 test class in `Mizrachi.Tests.Integration`.
  **Done:** the full lifecycle passes over real HTTP against both providers.
  `test: add end-to-end lifecycle tests over HTTP`

- [ ] **15. Tests: the security suite**
  The 14 tests in PLAN §6, with the two password-exposure proofs as the centrepiece:
  - **Never stored in plaintext** — register with a known sentinel password, then read the
    raw bytes of the SQLite `.db` and of the JSON file and assert the sentinel appears
    nowhere; assert the stored value parses as an Identity v3 hash; assert two users sharing
    a password have different stored values (per-user salt, NFR-2.1).
  - **Never returned in any response** — reflection over every type reachable from a
    controller action's return type plus `User`: no member name contains
    `password`/`hash`/`salt`; and the raw JSON of every endpoint's happy path contains the
    sentinel nowhere (FR-1.4, FR-4.1).
  Plus: identical 401s (FR-3.5), hash-on-unknown-user (FR-3.6), 403-before-404 (FR-2.4),
  concurrent-create (FR-1.8), no username in failed-auth logs (NFR-2.3), no password or
  token in any log line, client-supplied `userId` ignored (FR-1.2), credentials rejected
  outside the body (FR-3.2), Swagger absent in Production (NFR-2.7), no internals in
  Production errors (FR-4.3), over-length password rejected before hashing (FR-5.2).
  *Files:* ~3 test classes in `Mizrachi.Tests.Integration`, 1 in `Mizrachi.Tests.Unit`.
  **Done:** all 14 pass, and each one fails when its control is deliberately removed —
  verify by reverting the control locally, watching the test go red, and restoring it. A
  security test that cannot fail proves nothing.
  `test: prove the security properties (FR-1.4, FR-2.4, FR-3.5, FR-3.6, NFR-2.1, NFR-2.3)`

- [ ] **16. README, and the session transcript in `docs/`**
  README: what it is, how to run each provider, the three endpoints with example requests,
  the §4 out-of-scope list with what production would need instead, and the known
  limitations (JSON store's process-local uniqueness, ASCII-only usernames, no revocation).
  Then export the session transcript to `docs/`, **read it end to end before committing**
  and redact: any signing key, token, connection string, password used in testing, absolute
  paths containing the user's name, and anything else the `security-review` skill flags.
  Link it from the README as development history.
  *Files:* `README.md`, `docs/<transcript>.md`, `.gitignore` if needed.
  **Done:** a reader can clone and run from the README alone; `security-review` returns PASS
  over the transcript specifically — it is committed content like any other; CLAUDE.md's
  "do not commit session transcripts unless asked" is satisfied by this task being the ask.
  `docs: add README and development transcript`

---

## Notes on the split

**16 tasks, not 12.** Four splits are deliberate and I would not merge them:

- **10 and 11** (SQLite, JSON) are separate because a merged task fails for two unrelated
  reasons and you cannot tell which store broke from one red build.
- **9 before 10 and 11** so the contract suite exists before there is a second implementation
  to run it against — otherwise the suite gets written to fit whichever store came first.
- **12 and 13** are separate because the endpoints are verifiable without the middleware, and
  bundling them means a rate-limiter bug blocks review of the endpoint shapes.
- **14 and 15** are separate because integration tests answer "does it work" and the security
  suite answers "does it leak" — different failure meanings, and the security suite is the one
  that must be re-run whenever a control moves.

Tasks 2/3, 4/5/6 could each collapse into one commit if you want the count nearer 12; the
cost is that a policy bug and a test bug then arrive in the same commit.

---

## Note on task 16 — the implementation transcript

`docs/transcripts/` holds the three sessions that were exported before implementation began.
The implementation session itself — design, task breakdown, and tasks 1 to 16 — **is not
included**, because a session cannot export itself: the export is produced by the client after
the fact.

To complete the record, run `/export` in that session and add the result as
`docs/transcripts/03-implementation.md`, then add a row to the README's development-history
table. Redact the account email in the client banner on line 8, as was done for the others; the
existing three contained no keys, tokens or credentials.
