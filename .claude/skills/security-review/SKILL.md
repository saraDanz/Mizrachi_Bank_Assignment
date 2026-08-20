---
name: security-review
description: Banking-grade security review of changed code for this user-management API. Run before every commit (Definition of Done #3), and whenever asked to review, audit, or check code for security issues. Reports findings by severity with file:line and ends in a PASS/FAIL verdict.
---

# Security review

A commit gate, not advice. The task is not done until this returns PASS.

1. **Scope.** `git status --porcelain && git diff HEAD`. Review every changed line, reading
   the whole file around it — a clean diff can sit in a broken function. A deletion matters
   when it removes a control. Nothing changed: say so and stop; never review the whole repo.
2. **Apply.** Read `references/checklist.md` and work every item, running its greps.
3. **Verify.** Confirm each finding by reading the code. Drop anything you cannot anchor to
   a `file:line` — but never drop a `!` invariant violation for looking unexploitable.
4. **Report** in the format below, verdict last.

Severity — **Critical**: credential exposure or auth bypass (password recoverable, hash in
a response, non-owner delete succeeds). **High**: enumeration oracle, missing authorization
control, committed secret, unbounded hashing input, broken token validation. **Medium**:
missing audit event, weak error hygiene, missing rate limit or security test. **Low**:
hardening and defence in depth.

```
## Security review — <N files, M changed lines>

[CRITICAL] Api/Controllers/UsersController.cs:47 — SEC-6.1
Returns the User entity, exposing UserPassword.
  Why: any caller of POST /users reads the stored hash. Violates FR-1.4.
  Fix: return CreatedUserResponse(UserId, UserName).

### Verdict
FAIL — 1 Critical. Fix all Critical and High findings, then re-run.
```

Nothing to report: keep the header, write `No findings.`, give the verdict.

**Verdict rule.** FAIL on any Critical, any High, or any `!` violation; PASS otherwise,
still listing Medium and Low as advisories. Never soften a verdict because the fix is
inconvenient or the change is small.
