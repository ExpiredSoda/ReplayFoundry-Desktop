# Contributing

Replay Foundry is in public beta. Before starting a broad change, open an issue or discussion so its scope and user impact can be agreed before implementation.

Changes must keep the application local-first, preserve explicit upload consent, avoid committing media, models, runtime payloads, unreviewed build artifacts, or secrets, and add focused verification for behavior changes. Every user-visible change also needs a precise entry under `Unreleased` in [CHANGELOG.md](CHANGELOG.md); the public website can translate those entries into shorter creator-facing language only after the technical record is accurate. A change is ready for review only when the solution builds with zero warnings, the relevant executable test harnesses pass, formatting is clean, and the repository payload and security guards remain green. The [documentation index](docs/README.md) covers the supported developer and distribution workflows.

Keep responsibilities narrow and names specific. View models expose presentation
state and delegate durable storage, platform access, projection, and workflow
coordination to focused collaborators. A ten-line method is a useful review
signal, not a mechanical target: extract behavior when the result has a coherent
name, state boundary, or test surface, and do not hide oversized classes behind
partial files or generic `Helpers`, `Utils`, or `Managers` folders. The repository
architecture guard enforces project boundaries, shared test infrastructure,
documentation placement, and shrinking source-file budgets.

Use a focused branch and keep commits reviewable. Do not combine generated artifacts, broad mechanical rewrites, and behavior changes in one commit. A release proposal must keep the README, technical change log, website update page, download page, GitHub release notes, and signed artifact version in agreement; unreleased work must never be presented as the current download. Security reports follow [SECURITY.md](SECURITY.md), not the normal issue workflow.
