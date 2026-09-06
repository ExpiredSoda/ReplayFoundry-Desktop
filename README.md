# ReplayFoundry

A Windows app for finding moments in gameplay recordings, editing clips, styling captions, and preparing releases. Media analysis, editing, and rendering run locally. Connecting YouTube is optional.

## Download and install

The current public release is **[1.0.0 Beta 4](https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/tag/v1.0.0-beta.4)**.

- [Download the publisher-signed Windows x64 installer](https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/download/v1.0.0-beta.4/ReplayFoundry-1.0.0-beta.4-Base-win-x64-setup.exe).
- Windows 10 or 11, x64. Setup installs for the current user.
- Advanced AI is optional and unchecked by default. Its verified download is about 12.7 GB; the qualified visual AI configuration uses a compatible NVIDIA GPU with 16 GB VRAM. Readiness also depends on available memory.
- The Base app works without the optional AI package. Use Settings or the installer to add, repair, or remove optional capabilities.

**Beta 4 adds** expanded caption styling and editing, output framing and audio controls, balanced title and description drafts, resumable rendering, and a refreshed workspace. Qualification includes Debug and Release tests, native workspace checks, verified runtime packs, installed local AI generation, and a freshly encoded caption styling test. AI wording can still require review; [CHANGELOG.md](CHANGELOG.md) records the release scope and measured performance limits.

## Prepare and publish

Review the cut, captions, title, and description in Studio before exporting. Keep the generated subtitle files and publishing guide with local publishing packages. YouTube uploads and scheduled releases require an explicit publishing action.

- [Website and installation status](https://replayfoundry.com/download)
- [Windows packaging, signing, and deployment](docs/distribution/windows.md)
- [Third-party distribution and license requirements](docs/distribution/third-party-compliance.md)
- [Release documentation](docs/README.md)
- [Security reporting](SECURITY.md)

The [Beta 3 workflow demonstration](https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/download/v1.0.0-beta.3/ReplayFoundry-3-Minute-Workflow-Demo-1080p.mp4) shows installation through publishing; its interface predates Beta 4.

ReplayFoundry is a product of Expired Soda Studios LLC. For installation or release support, contact [support@replayfoundry.com](mailto:support@replayfoundry.com).
