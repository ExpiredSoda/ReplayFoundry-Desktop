# Replay Foundry documentation

This directory contains the maintained guidance for developing and distributing Replay Foundry. Product behavior belongs in code and automated verification; these documents explain the workflows and decisions a contributor must perform outside the application.

The technical [change log](../CHANGELOG.md) distinguishes the current public
Beta 2 release from work that is still being tested. User-visible changes must
be recorded there before a production snapshot is proposed.

## Development

- [Local media tools](development/media-tools.md) — configure FFmpeg and ffprobe for Debug builds without committing binaries.
- [Brand assets](development/brand-assets.md) — maintain the canonical application marks and deterministic Windows icon derivative.

This index covers the production source, its verification suites, and the
reviewed distribution guidance needed to build and evaluate a release candidate.

## Distribution

- [Windows distribution](distribution/windows.md) — build runtime packs, publish the app, create the installer, sign release candidates, and verify clean-machine behavior.
- [Third-party compliance](distribution/third-party-compliance.md) — review the pinned media, speech, Python, CUDA, and model payload provenance for each release.

## Repository map

| Directory | Purpose |
| --- | --- |
| `src/` | Shipped application and reusable runtime components. |
| `tools/` | Shipped runtime-pack maintenance executable. |
| `tests/` | Executable verification suites and shared test support. |
| `eng/` | Build, verification, packaging, signing, and runtime-host automation. |
| `installer/` | Inno Setup definition and installer-owned assets. |

## Component map

| Component | Role | Ships to users |
| --- | --- | --- |
| `ReplayFoundry.Desktop` | Windows application, MVVM presentation, workflows, and platform adapters. | Yes |
| `ReplayFoundry.RuntimePacks` | Shared contracts and validation for versioned local runtime packages. | Yes |
| `ReplayFoundry.VisualSemanticHost` | Python process used by the optional local visual-semantic runtime. | In a verified runtime pack |
| `ReplayFoundry.RuntimeInstaller` | Small maintenance executable that installs, repairs, or removes runtime packs independently of the desktop process. | Yes |
| `ReplayFoundry.Testing` | Shared framework-free runner and repository-location support for executable test suites. | No |
| `ReplayFoundry.*Tests` | Focused verification by responsibility; they are grouped under **Verification** in the solution rather than mixed with product code. | No |

There is one production codebase and one production solution. The solution
groups the shipped **Product** and **Product Tools** separately from verification
for **Desktop**, **Runtime Packs**, and **Shared** test support.

Release history and community expectations remain at the repository root:
[change log](../CHANGELOG.md), [contributing](../CONTRIBUTING.md),
[security](../SECURITY.md), and [community standards](../CODE_OF_CONDUCT.md).
