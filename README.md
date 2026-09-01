<div align="center">
  <img src="src/ReplayFoundry.Desktop/Assets/Branding/favicon.svg" width="88" alt="Replay Foundry logo" />
  <h1>Replay Foundry</h1>
  <p><strong>Turn long gameplay recordings into polished vertical clips—locally, deliberately, and under your control.</strong></p>
  <p>
    <a href="https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/latest"><img alt="Public beta" src="https://img.shields.io/badge/status-public%20beta-0e7490?style=flat-square" /></a>
    <a href="https://github.com/ExpiredSoda/ReplayFoundry-Desktop/actions/workflows/desktop-ci.yml"><img alt="Desktop source gate" src="https://github.com/ExpiredSoda/ReplayFoundry-Desktop/actions/workflows/desktop-ci.yml/badge.svg" /></a>
    <img alt="Windows" src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-38bdf8?style=flat-square" />
    <img alt="Local first" src="https://img.shields.io/badge/processing-local--first-f5c451?style=flat-square" />
    <img alt="License" src="https://img.shields.io/badge/source-MIT-94a3b8?style=flat-square" />
  </p>
  <p>
    <a href="https://replayfoundry.com">Website</a> ·
    <a href="https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/tag/v1.0.0-beta.2">Download Beta 2</a> ·
    <a href="CHANGELOG.md">Change log</a> ·
    <a href="docs/README.md">Documentation</a> ·
    <a href="https://replayfoundry.com/support">Support</a>
  </p>
</div>

![Replay Foundry workflow](.github/assets/replayfoundry-workflow-hero.gif)

Replay Foundry is a Windows desktop workflow for finding strong gameplay
moments, shaping vertical videos, styling captions, reviewing titles and
descriptions, organizing finished work, and preparing YouTube releases. Editing
and optional AI processing stay on the creator's PC; uploads happen only through
explicit Publish actions.

The current download is **Replay Foundry 1.0.0 Beta 2**, published August 14,
2026. The [Unreleased change log](CHANGELOG.md#unreleased) records work being
tested for the next update; it is deliberately kept separate from what is
available today.

## Watch the complete workflow

[![Watch the Replay Foundry start-to-finish demo](.github/assets/replayfoundry-demo-poster.jpg)](https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/download/v1.0.0-beta.2/ReplayFoundry-3-Minute-Workflow-Demo-1080p.mp4)

The 2 minute 37 second demo follows the released product from installer choices
through Generate, Studio, Library, and a scheduled YouTube release. Long local
analysis intervals are condensed and clearly labeled; the product interactions
themselves are shown directly.

## The workflow

| 01 · Generate | 02 · Studio |
| --- | --- |
| ![Generate finds strong moments in a gameplay recording](.github/assets/workflow-01-generate.gif) | ![Studio shapes a vertical gameplay clip](.github/assets/workflow-02-studio.gif) |
| Choose a recording, set the gameplay region, and let Replay Foundry surface candidate moments with local analysis. | Review the result, trim timing, compose the frame, mix audio, and style animated captions. |

| 03 · Library | 04 · Publish |
| --- | --- |
| ![Library organizes finished Replay Foundry videos](.github/assets/workflow-03-library.gif) | ![Publish reviews and schedules a YouTube release](.github/assets/workflow-04-publish.gif) |
| Keep finished videos and project context organized on the PC. | Review every field, connect YouTube deliberately, and upload now or schedule a release. |

## Install the current beta

Download the Microsoft-signed [Replay Foundry Beta 2 installer](https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/download/v1.0.0-beta.2/ReplayFoundry-1.0.0-beta.2-Base-win-x64-setup.exe).

### Preview of the next installer

The image below shows the redesigned **Unreleased** installer presentation now
being tested. It is not a screenshot of the current Beta 2 download.

![Base and Advanced AI installer choices](.github/assets/setup-base-advanced.png)

- **Base** installs the core local moment-finding, editing, rendering, Library,
  and Publish workflow together with verified media tools.
- **Advanced AI** is optional and starts unchecked. When selected, setup adds
  the qualified local speech, transcription, visual-analysis, and editorial
  packs. The optional Advanced AI download is about 12.5 GB and is intended for
  compatible NVIDIA systems.
- Setup verifies signed or hash-pinned release artifacts before using them.
  Model weights, native runtime archives, signing material, credentials, and
  local media are never stored in this source repository.

This is a prerelease. Back up important work and use the in-app reviewed
diagnostics flow when reporting a problem.

## Product principles

- **Local first:** source media, transcripts, project state, and optional local
  AI stay on the PC unless the user starts an upload or explicitly sends a
  reviewed report.
- **Review before action:** generated clips and metadata remain editable;
  publishing requires deliberate confirmation.
- **No silent substitutions:** qualified runtimes and models are verified by
  manifest and hash. Missing or incompatible capabilities are explained instead
  of replaced with an unknown tool or an unrequested no-AI workflow.
- **Recoverable workflows:** projects, renders, and publish drafts are durable,
  while rebuildable caches can be cleared independently.
- **Accessible motion:** interaction and caption effects respect reduced-motion,
  keyboard, and high-contrast settings.

## Build from source

Requirements:

- Windows 10 version 19041 or newer
- .NET 10 SDK
- PowerShell 7
- Visual Studio 2026 or another Windows desktop build environment

Those requirements are enough for a normal application build. The complete
`verify` gate additionally requires a Git checkout (not a GitHub source ZIP),
Python 3.11, current Inno Setup 6.7 or 7, and an x64 Windows SDK SignTool at
version 10.0.2261.755 or newer.

Use the repository console for both the normal build and the complete local
verification gate:

```powershell
.\eng\ReplayFoundry.ps1 build
.\eng\ReplayFoundry.ps1 verify -Configuration Release -PythonExecutable <python.exe>
```

`verify` builds once, runs the .NET and visual-semantic test suites in the
current public repository profile, then applies the architecture, security,
UI/UX, runtime, installer, data-boundary, and release guards. Run
`.\eng\ReplayFoundry.ps1 help` for the commands supported by the snapshot.

Start with the [documentation index](docs/README.md), then see the
[Windows distribution guide](docs/distribution/windows.md) and
[third-party compliance record](docs/distribution/third-party-compliance.md).

## Trust, privacy, and support

No signing keys, user access tokens, or release credentials are committed to
this repository. The signed app's Google Desktop OAuth configuration is supplied
by the protected release environment, while each user's tokens remain in Windows
Credential Manager. Release builds resolve only verified active runtime packs.
User reports are sanitized, remain local by default, and are sent only after
explicit review and consent. Please report security concerns through
[SECURITY.md](SECURITY.md).

Replay Foundry is free to download. If it saves you time and you want to help
fund continued development, [support Expired Soda on Buy Me a Coffee](https://buymeacoffee.com/expiredsoda).
Support is optional and never changes product access.

<div align="center">
  <a href="https://buymeacoffee.com/expiredsoda">
    <img src=".github/assets/buy-me-a-coffee-qr.png" width="240" alt="Scan to support Expired Soda on Buy Me a Coffee" />
  </a>
  <br />
  <sub>Scan the code or click it to open the optional support page.</sub>
</div>

Copyright © 2026 Expired Soda Studios LLC. Source code is available under the
[MIT License](LICENSE.txt); bundled third-party components retain their own
licenses and notices.
