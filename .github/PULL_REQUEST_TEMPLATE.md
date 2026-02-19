## Summary

- What changed?
- Why was it needed?

## Risk

- Risk level: `low | medium | high`
- Regression surface (frontend/backend/infra/docs/security/release):
- Security/runtime safety impact:

## Evidence

- Deterministic verification command: `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live__VERIFY__FullyQualifiedName!~RuntimeAttachSmokeTests"`
- Command output summary:
- Any justified skips:

## Rollback

- Rollback command or steps:
- Data/schema/runtime rollback impact:

## Scope Guard

- [ ] Change is minimal and task-focused
- [ ] No unrelated refactors included
- [ ] No secrets or private tokens added
