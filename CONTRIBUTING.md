# Contributing

## Pull Request Size

Work through small pull requests. A PR should represent one focused, reviewable change.

Bad PR:

- Implemented everything

Good PRs:

- Add transcription job model
- Add ffmpeg service abstraction
- Add basic file import screen
- Add export to TXT

Ideal PR size:

- 100-400 lines changed

Larger PRs are acceptable only when the scope cannot be split cleanly. In that case, explain why in the PR description.

## Pull Request Description

Every PR should include:

```md
## Summary

What was changed.

## Why

Why this change is needed.

## Testing

How it was tested.

## Screenshots

If UI changed.
```

## Conventional Commits

Use Conventional Commits for commit messages and PR titles.

Examples:

- `feat: add audio file import`
- `fix: handle missing ffmpeg binary`
- `docs: add architecture overview`
- `test: add transcription job unit tests`
- `refactor: extract whisper service interface`
- `ci: add GitHub Actions build pipeline`

This keeps history readable and supports changelogs and releases later.
