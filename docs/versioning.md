# Versioning

This project versions more than releases. Model-backed behavior depends on code, models, embeddings and pricing.

## Project Releases

- Use SemVer.
- Stay in `0.x` until extension points and public contracts stabilize.
- Treat `v0.1.0` as the first public reference release.
- Reserve `v1.0.0` for a future stable starter kit.

## Public Release Flow

The public repository treats `main` as release-ready history. A public pull request should be ready to become a GitHub Release as soon as it is merged.

- Public changes land through pull requests into protected `main`.
- Squash merge is the expected public merge strategy: one merged PR becomes one public release commit.
- Release PRs update `VERSION` with SemVer without a leading `v`, for example `0.2.0`.
- Release PRs also update `CHANGELOG.md` and add `docs/release-notes-v<version>.md`, for example `docs/release-notes-v0.2.0.md`.
- After a release PR that changes `VERSION` is merged to `main`, the `publish-release` workflow runs automatically on that `main` push.
- The workflow reads `VERSION`, builds the tag name (`v0.2.0`), verifies the matching release-notes file, reruns the release gate, tags the current `main` commit and creates the GitHub Release.
- Non-release PRs must not change `VERSION`; their merges do not run `publish-release`.
- If the tag already exists at the same `main` commit, the workflow can resume publishing. If the tag exists at another commit, the workflow fails instead of moving history.
- Normal releases do not require pressing `Run workflow`; the `VERSION`-changing merge to `main` is the release trigger.
- `workflow_dispatch` and direct tag pushes are recovery paths, not the normal release path.

Do not auto-increment release versions in CI. The version is part of the reviewed release PR so maintainers can choose patch, minor or major intentionally.

## API

- Use `/api/v1/...` from the start.
- Avoid breaking documented `v1` response contracts once examples depend on them.

## Database

- Keep schema changes in source control.
- Observability and tool-audit tables currently use explicit raw SQL/init scripts and small Npgsql adapters while the persistence surface is still stabilizing.
- If EF Core is introduced later for broader persistence, use migrations and name them after the use case or schema change.

## Pricing

Pricing records include effective dates so historical cost calculations remain reproducible.

## Tool Calls

- tool name;
- tool schema version;
- tool policy version.

These fields keep proposed, approved, rejected and executed tool calls
reproducible after a tool schema or backend policy changes.
