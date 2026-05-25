# CodeQL — Abblix policy

**Current state: DISABLED.** This repository does not run CodeQL.

In May 2026, CodeQL via GitHub-managed Default Setup consumed 824 GHA minutes across the Abblix org (52% of total budget) before being disabled. This directory holds dormant scan-scope config so that if CodeQL is reactivated, the analysis is constrained from the start.

## Files

- `codeql-config.yml` — scan scope: `security-and-quality` query suite, paths-ignore for tests/build outputs/vendored code. Referenced by `config-file:` in either Default Setup or a custom workflow.

## Re-enable

GitHub-managed Default Setup cannot disable the weekly schedule or PR-trigger via API — `state=configured` always pairs with hardcoded triggers. **Prefer a custom workflow** at `.github/workflows/codeql.yml` for trigger control. See the active example in `Abblix/Oidc.Server` for the exact shape (push-to-default-branch + workflow_dispatch only, references this `codeql-config.yml`).

If Default Setup is acceptable (with the wider trigger set), enable it via:

```bash
gh api -X PATCH "repos/Abblix/<REPO>/code-scanning/default-setup" \
  -f state=configured \
  -f query_suite=default \
  -F 'languages[]=csharp'
```

The `query_suite=default` setting pairs with this `codeql-config.yml` to keep the lighter query set + path exclusions.

## Verify current state

```bash
gh api "repos/Abblix/<REPO>/code-scanning/default-setup" --jq .state
# expected: "not-configured"
```

## Disable again (Default Setup)

```bash
gh api -X PATCH "repos/Abblix/<REPO>/code-scanning/default-setup" \
  -f state=not-configured
```

## Why "Code Quality" workflow names in history map to CodeQL

In the Actions UI, CodeQL **Default Setup** runs appeared as `Code Quality: Push on develop`, `Code Quality: PR #N`, `Code Quality: Scheduled`. The underlying workflow path was `dynamic/github-code-scanning/codeql` (not editable). Despite the "Code Quality" label, there is no SonarCloud or other linter — it is purely CodeQL.
