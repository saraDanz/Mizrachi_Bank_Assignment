# Mizrachi Bank Assignment — .NET 8 Web API take-home

## Authority
- `REQUIREMENTS.md` is the contract. Cite FR/NFR ids in commits and PRs.
- A decision recorded there is settled — do not re-open it unless asked.
- Scope change ⇒ update `REQUIREMENTS.md` in the same commit.
- Unknowns belong in `REQUIREMENTS.md` §5. Ask; do not silently assume.

## Environment
- Windows. PowerShell is primary; the Bash tool needs POSIX syntax.
- .NET SDK 8.0.302, target `net8.0`. Do not target another framework.
- No SQL Server, LocalDB, or Docker on this machine. Do not propose them.

## Invariants
- User schema is exactly three fields: UserId, UserName, UserPassword. Never add a fourth.
- UserId is a server-generated GUID. Never accept a client-supplied id.
- The Domain project has zero `PackageReference` entries.
- EF package refs use `PrivateAssets="compile"`; the DbContext is `internal`. The API project
  must not compile against EF types.
  *(Was `"all"`. Amended per OQ-6: `"all"` also withholds the runtime assets, so the API built
  clean, shipped zero EF assemblies, and threw `FileNotFoundException` at first use. `"compile"`
  keeps the API's compile surface free of EF — verified, CS0234 — while letting the assemblies
  reach its output.)*
- The persistence provider is selected by configuration only, never by a code change.

## Security rules
- No response type may declare a password or hash member. Grep before committing.
- Never log a password, a token, or a request body.
- Never log the submitted username on a *failed* authentication.
- Failed validation returns 401 with an identical body whether or not the account exists.
- An unknown username must still perform a hash verification before returning.
- Delete evaluates authorization *before* existence: an unowned id returns 403, never 404.
- Credentials go in the request body — never in a URL, query string, or route.
- No hand-written crypto. Use the framework password hasher behind our own abstraction.
- No secret in any committed file. Signing keys: user-secrets or environment variable only.
- Swagger/OpenAPI is registered only when the environment is Development.
- Error responses outside Development carry no stack trace or datastore detail.

## Tests
- No mocking library; hand-write fakes.
- No FluentAssertions (commercially licensed from v8). Plain xUnit asserts.
- Every repository implementation runs the same contract test suite.
- Security behaviours are tests, not comments: no-password-in-response,
  403-on-non-owner, hash-on-unknown-user.

## Definition of Done
A task is not complete until all four pass:
1. `dotnet build` succeeds with no new warnings.
2. `dotnet test` succeeds.
3. The `security-review` skill returns PASS on the exact diff being committed.
4. Changes are committed with a conventional-commit message
   (`feat:` `fix:` `docs:` `test:` `chore:`).

## Git
- **Gate: run the `security-review` skill immediately before every `git commit`, and only
  commit on PASS.** Every step of a multi-step task, every time — no exception for docs,
  config, tests, a one-line fix, a WIP commit, or an amend.
- A FAIL blocks the commit. Fix the findings, re-run `dotnet build` and `dotnet test`,
  then re-run the skill. Never soften or argue with a verdict to get past it.
- A PASS covers only the diff as it stood when the skill ran, and expires the moment any
  file changes. Changed something after the PASS — including a fix the skill asked for?
  Run it again.
- **Never push. Ask first, every time.**
- Commit with an explicit pathspec; never sweep unrelated working-tree changes in.
- Do not commit `exports/` or session transcripts unless asked.
