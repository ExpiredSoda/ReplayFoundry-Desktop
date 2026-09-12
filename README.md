# ReplayFoundry

A Windows app for finding moments in gameplay recordings, editing clips, styling captions, and preparing releases. Media analysis, editing, and rendering run locally. Connecting YouTube is optional.

## Download and install

The current public release is **[1.0.0 Beta 5.1](https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/tag/v1.0.0-beta.5.1)**.

- [Download the publisher-signed Windows x64 installer](https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/download/v1.0.0-beta.5.1/ReplayFoundry-1.0.0-beta.5.1-Base-win-x64-setup.exe).
- Windows 10 or 11, x64. Setup installs for the current user.
- Advanced AI is optional and unchecked by default. Setup shows its download size before installation; the qualified visual AI configuration uses a compatible NVIDIA GPU with 16 GB VRAM. Readiness also depends on available memory.
- The Base app works without the optional AI package. Use Settings or the installer to add, repair, or remove optional capabilities.

**Beta 5.1 adds in-app updates** under Settings → About & updates. Install this version over your current copy once; future updates can be downloaded and installed from inside the app. Automatic checks are optional, and updates preserve projects, feedback and installed AI packs.

**Beta 5 adds** full-recording timelines, visually reviewed Balanced AI picks, draggable clip ranges, clearer Studio controls, preview-area ratings, animated caption previews, and a local personal learning model in official downloads. Titles and descriptions use refreshed scene evidence, and remembered game names require confirmation for each recording. First-time visual analysis takes longer; completed reviews are reused. New profiles start without trained preferences, and independent quality checks are required before personal ranking activates. Review generated captions and wording before publishing. [CHANGELOG.md](CHANGELOG.md) records the release scope.

## Prepare and publish

The refreshed Beta 5 also reduces repeated work in AI titles and descriptions.
Optional wording learning keeps your saved edits and approvals on this PC for
future personal-writer training. Advanced AI includes the pretrained writer
base; personal adapters activate only after sufficient feedback and independent
quality checks. Existing AI writing works before personalization is ready.

Review the cut, captions, title, and description in Studio before exporting. Keep the generated subtitle files and publishing guide with local publishing packages. YouTube uploads and scheduled releases require an explicit publishing action.

- [Website and installation status](https://replayfoundry.com/download)
- [Windows packaging, signing, and deployment](docs/distribution/windows.md)
- [Third-party distribution and license requirements](docs/distribution/third-party-compliance.md)
- [Release documentation](docs/README.md)
- [Security reporting](SECURITY.md)

Official Windows downloads include ReplayFoundry’s proprietary personal learning engine. The public source repository includes its integration interface and an unavailable adapter, without the neural training implementation, personal examples, or learned model files.

The [workflow demonstration on the website](https://replayfoundry.com/) shows installation through publishing. Its displayed beta label identifies the version recorded.

ReplayFoundry is a product of Expired Soda Studios LLC. For installation or release support, contact [support@replayfoundry.com](mailto:support@replayfoundry.com).
