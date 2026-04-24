# Project Workflow Instructions

## Pull Requests

Work through small pull requests. Prefer one focused change per PR.

- Avoid broad PRs like "Implemented everything".
- Prefer focused PRs such as "Add query diagnostics model" or "Add evaluation dashboard page".
- Ideal PR size is 100-400 lines changed.
- If a PR is larger, explain why it could not be split cleanly.

Every PR must include:

- `Summary`: what changed.
- `Why`: why the change is needed.
- `Testing`: how it was tested.
- `Screenshots`: required when UI changed, otherwise `N/A`.

## Commits

Use Conventional Commits for commit messages and PR titles.

Examples:

- `feat: add audio file import`
- `fix: handle missing ffmpeg binary`
- `docs: add architecture overview`
- `test: add transcription job unit tests`
- `refactor: extract whisper service interface`
- `ci: add GitHub Actions build pipeline`
