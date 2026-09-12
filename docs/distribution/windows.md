# Windows distribution

Replay Foundry uses one signed, per-user Windows x64 installer and never modifies `PATH`.

| Setup choice | Network requirement | Installed capability |
| --- | --- | --- |
| **Base** | The downloaded setup works offline. | Self-contained WPF application plus the verified FFmpeg/ffprobe pack. Deterministic evidence and moment finding remain available without an AI model. |
| **Advanced AI** | Downloads only when selected during setup or added later from Settings. | Base plus Silero VAD, whisper.cpp, the qualified multilingual Whisper model, and the qualified local Qwen3-VL CUDA runtime and model. The unchecked option states its approximate download size. |

Base moment finding remains available without optional AI packs. If a selected
AI writing workflow cannot use its qualified pack, Replay Foundry stops and
explains the unavailable capability instead of silently substituting Simple / No
AI or an unknown executable or model. Simple / No AI writing runs only after the
creator explicitly selects it. Generated wording remains editable and
reviewable.

## Installed layout

The application and its runtime packs are deliberately separate:

- The application lives under `%LOCALAPPDATA%\Programs\Replay Foundry`.
- Runtime packs live in compact, full-SHA-256 content-addressed directories under `%LOCALAPPDATA%\ReplayFoundry\R`.
- The runtime-maintenance executable ships inside the application at `Tools\RuntimeInstaller\ReplayFoundry.RuntimeInstaller.exe`.

The compact runtime path is intentional because deeply nested Windows paths can prevent pinned native Python modules from loading. Manifests retain full package names, versions, kinds, licenses, dependencies, file sizes, and hashes even though physical directory names are shortened.


## Application updates

Settings → About & updates provides **Check for updates** and an optional daily
check. Automatic checks default off and the choice is stored per user and release
channel. Checks contact GitHub; they do not upload projects, recordings or learning
data. Download and installation require the user's choice. Development builds do
not contact the production feed.

WinSparkle 0.9.4 downloads the existing signed Inno installer and verifies Ed25519
signatures with the public key in eng/ReplayFoundry.Updates.psd1. Packaging verifies
the official SDK archive's pinned SHA-256 and includes the x64 DLL and full notices.
See the [integration guide](https://winsparkle.org/guides/integrating-winsparkle/).

Every production build requires -BuildRevision (1–65535), increasing within its
three-part product version. The first updater release is 1.0.0-beta.5.1, file
version 1.0.0.1. Later Beta 5 updates increment both suffix and revision. Never
replace an installer already referenced by a feed. Beta and stable feeds are
separate beta.xml and stable.xml assets on the public repository's app-updates
release. Publish a stable feed only when a stable product is available.

Initialize-ReplayFoundryUpdateSigning.ps1 creates a key once and retains it encrypted
by Windows DPAPI under the current user's protected LOCALAPPDATA/ReplayFoundryBuildSecrets
directory, outside source and distribution roots. Subsequent runs recover the same
key instead of rotating it. A company backup of the signing identity is needed
before moving release builds to another account: the encrypted file is tied to
this Windows account and is not itself a portable backup. Never commit or package
a private key. The current Inno installer license is already owned.

After Authenticode-signing the installer, run New-ReplayFoundryUpdateAppcast.ps1 with
-InstallerManifestPath, -OutputPath, -ReleaseNotes and -PreviousAppcastPath. Omit the
previous feed only for the first release in a channel. Feed generation rejects
unsigned, dirty, non-increasing or mismatched packages and verifies the finished
EdDSA signature. Upload the immutable installer and release evidence to its
v<product-version> release first; verify public bytes match before replacing the
channel feed. Failed or offline checks leave the current application usable.

Restart is blocked during generation, rendering, publishing, local training,
pending edits and open setup workflows. The native installer callback uses fixed
application-controlled arguments. Setup waits up to 60 seconds for the initiating
process to finish its normal save/close flow before replacing any files. It never
force-terminates a render. Updates preserve desktop-shortcut choice, retain installed
Advanced AI packs and relaunch afterward. Existing users need one install-over-existing
upgrade to receive the updater. Do not uninstall first: the uninstaller removes app data.

Qualify current/no-update checks, a newer signed update, signature rejection after
byte modification, same/older versions, offline errors, deferred restart and an
installed upgrade with saved-data fingerprints compared before and after. Confirm
runtime packs remain available.

## Build runtime packs

This Beta 5 build requires visual runtime `0.8.28` and model pack `4.0.24` as a matching
set. The weights remain Qwen3-VL 4B Instruct; the model pack carries a fresh
structured-decoding qualification lock for CPython `3.13.15`, PyTorch
`2.13.0+cu130`, TorchVision `0.28.0+cu130`, TorchCodec `0.16.0`, and Transformers
`5.16.1`. The release also updates Pillow, pip, and setuptools. Exact installed
distribution versions, retained licenses, and dependency-audit results belong
in the external release evidence. Do not reuse the Beta 4 qualification lock.

Accelerate is pinned to the reproducible local wheel `1.14.0+replayfoundry.1`.
`eng/New-AccelerateSecurityWheel.py` verifies the official 1.14.0 wheel hash,
applies the bounded checkpoint-shard fix for `GHSA-4j2p-28q2-5m79`, and rebuilds
wheel metadata and file hashes. Its regression tests exercise both real loading
APIs with hostile indexes and valid sibling safetensors. The advisory audit
retains the upstream finding and accepts this remediation only for the exact
patched loader hash. All other advisories still block release. Stage this wheel
in a fresh dependency directory, regenerate notices and the GPU qualification
lock, then build new runtime packs; never edit a sealed or installed pack.

For a pre-installation Debug qualification, the existing explicit Qwen override
opt-in also accepts `REPLAYFOUNDRY_QWEN_SITE_PACKAGES` as an absolute existing
directory. It uses the same restricted child-process environment as packaged
execution. This override is absent from Release builds.

The editorial acceleration source requires a freshly built visual runtime pack;
the published `0.8.25` pack does not contain its resident worker. Qualify the
desktop and host together before publishing these new runtime pack versions or
updating the installer catalog. Keep the product version at Beta 5.

### Personal writer qualification

The private runtime includes a local writer training workflow. Advanced packaging
requires `-WriterBaseRoot <verified-base>` and includes the pinned Qwen3-0.6B base
under `writer-base` in the visual model pack. Its manifest must match the
third-party compliance record. Packaging copies only its eight pinned files and
manifest, never personal examples or adapters. Installation does not activate a
personal writer.
Existing qualified AI writing remains available without a personal adapter.

With wording learning enabled in Settings, explicit title/description edits and
approvals retain their verified factual context under the channel's
`Personalization/Writer` directory. Clip ratings, renders and publishing events
do not approve wording. At least 64 explicit examples from six recordings are
required. Do not copy a user's examples into release artifacts or Git.

Run the following module with the qualified private runtime's Python and host
module path. Use the user's channel-specific writer directory as `<writer-root>`:

```text
python -B -m replayfoundry_visual_semantic.editorial.writer.workflow status --root <writer-root>
python -B -m replayfoundry_visual_semantic.editorial.writer.workflow train --root <writer-root> --base <verified-base>
python -B -m replayfoundry_visual_semantic.editorial.writer.workflow evaluate --root <writer-root> --base <verified-base> --candidate <candidate-directory> --partition development
python -B -m replayfoundry_visual_semantic.editorial.writer.workflow evaluate --root <writer-root> --base <verified-base> --candidate <candidate-directory> --partition qualification
python -B -m replayfoundry_visual_semantic.editorial.writer.workflow promote --root <writer-root> --base <verified-base> --candidate <candidate-directory>
```

Training writes a candidate without changing the active writer. Evaluation
reserves whole recordings and produces outputs for independent review. Fill in
the comparison outputs/timings from the qualified production writer and the
human review fields in `evaluation.json`; do not infer approval from training
loss. Promotion requires every held-out comparison, supported facts, valid
wording, at least a 60% preference score and a median latency ratio at most 0.8.
Synthetic qualification runs always remain ineligible. Promotion writes
`active.json` only after the gate passes; inference rechecks the evidence and
retains the existing writer if the personal adapter is unavailable.

New feedback uses `foundry-writer-example-2`: only explicitly edited or approved
fields receive supervised or preference loss. Scene-copy examples retain the
production system prompt and four-field grammar. Factual corrections supply a
reviewed central event without replacing the captured original facts. Clip
ratings never become title approvals. Recording assignments in
`recording-splits.json` persist across runs; development comparisons cannot
qualify a writer. The 64-example/six-recording minimum starts an attempt, not an
activation guarantee. Keep additional untouched recordings for final checks
after tuning against any previously inspected test results.

Prepare a local video review with the installed media tool:

```text
python -B -m replayfoundry_visual_semantic.editorial.writer.workflow prepare-review --root <writer-root> --output <new-review-directory> --ffmpeg <installed-ffmpeg>
python -B -m replayfoundry_visual_semantic.editorial.writer.workflow train-video --root <writer-root> --base <verified-video-model> --review-pack <review.json> --output <new-candidate-directory> --model-manifest-sha256 <trusted-installed-manifest-hash> --steps 96
```

Review prepared frames and the original audio before supplying `targets` and
marking a pack item reviewed. Speaker labels require actual audio review;
unknown labels remain null. The video trainer uses chronological frames and
reviewed speech, freezes the visual encoder, and adapts language attention
with LoRA. It preserves a 2 GiB GPU reserve and requires 12 GiB free before
starting. `--smoke --steps 2` tests machinery in a separate split registry and
never qualifies or activates a model. Video candidates need independent
production integration and source-video evaluation before release activation.
Normal app use collects montage order for future sequence-quality review;
rendering alone does not prove a coherent montage. No personal or experimental
weights are included in the Beta 5 installer.

All payload roots and outputs must remain outside the repository. Generate exact Python and wheel notices first; a missing license text is a hard failure unless a reviewed, hash-pinned official override is supplied.

```powershell
.\eng\New-PythonRuntimeNotices.ps1 `
  -PythonHome <cpython-root> `
  -SitePackages <pinned-site-packages> `
  -LicenseOverrideManifest <reviewed-official-overrides.json> `
  -OutputDirectory <external-notice-output>
```

Then assemble and seal the required profile. `CreatedAtUtc` is an explicit input so identical payloads and provenance produce deterministic manifest hashes.

```powershell
.\eng\Build-ReplayFoundryRuntimePacks.ps1 `
  -Profile Base `
  -OutputDirectory <external-pack-output> `
  -CreatedAtUtc <canonical-utc-timestamp> `
  -MediaToolsRoot <pinned-lgpl-ffmpeg-root> `
  -MediaToolsArchiveSha256 <sha256> `
  -MediaToolsArchiveUrl <permanent-https-binary-url> `
  -MediaToolsSourceArchiveUrl <permanent-https-source-url> `
  -MediaToolsSourceArchiveSha256 <sha256>
```

Advanced also requires the pinned Silero models, whisper.cpp runtime and model, relocatable CPython environment, Qwen host and model, qualification lock, and corresponding license inputs. The builder copies only fixed package kinds, seals every file by length and SHA-256, verifies each pack, and launch-checks the relocated Qwen host before writing its build index.

Create the online Advanced catalog from that verified index. Each entry binds an HTTPS archive URL and length to both the archive hash and installed manifest hash.

```powershell
.\eng\New-ReplayFoundryRuntimePackCatalog.ps1 `
  -RuntimePackBuildRoot <advanced-pack-output> `
  -BaseUri <https-runtime-pack-root> `
  -ApprovedRedirectHosts <reviewed-cdn-hosts> `
  -OutputPath <external-output>\advanced-runtime-catalog.json
```

## Build an installer

The normal online profile keeps the signed setup small and offers Advanced AI as an unchecked download choice:

Every installer build also requires the Google Desktop OAuth client secret in
the current process environment. Load it from the approved secret manager; do
not put it in source files, command arguments, shell profiles, or build logs.

```powershell
# Set this process-only value from the approved secret manager before building.
$env:REPLAYFOUNDRY_YOUTUBE_CLIENT_SECRET = <approved-secret-manager-value>
```

```powershell
.\eng\Build-ReplayFoundryInstaller.ps1 `
  -Version <version> `
  -YouTubeClientId <desktop-client-id>.apps.googleusercontent.com `
  -AdvancedInstallerUri https://replayfoundry.com/download `
  -Profile Base `
  -RuntimePackBuildRoot <advanced-pack-output> `
  -AdvancedPayloadMode Online `
  -AdvancedCatalogPath <external-output>\advanced-runtime-catalog.json `
  -ArtifactRoot <external-installer-output>
```

The Base installer embeds only the media-tools archive. When it also offers the
optional online Advanced AI choice, the verified Advanced build root is required
so setup can bind the embedded Base payload and online catalog to the same sealed
pack set.

Remove the process value immediately after the build, whether the build succeeds
or fails:

```powershell
Remove-Item Env:\REPLAYFOUNDRY_YOUTUBE_CLIENT_SECRET -ErrorAction SilentlyContinue
```

Without production signing arguments, this produces a Development installer for local and VM validation. It is intentionally ineligible for public release.

Installer artwork is generated into `<ArtifactRoot>\branding` from the canonical brand assets. The build supports licensed per-user Inno Setup 6.7 installations, system installations, and side-by-side Inno 7; `-InnoCompilerPath` can select an exact compiler. Replay Foundry does not read or store an Inno license key.

Before packaging, verify deterministic branding and compile a minimal setup:

```powershell
.\eng\Test-InstallerBranding.ps1
```

## Sign a production candidate

Production uses Microsoft Artifact Signing Public Trust for `Expired Soda Studios LLC`. No certificate, private key, OAuth token, or Azure credential belongs in source control or script arguments.

The personal learning engine is proprietary. Official installers are built from the private repository and include its neural training, evaluation, and local storage implementation. Public source exports retain the integration contracts and an explicitly unavailable adapter; they do not include personal learning. The exporter excludes the learning implementation directory by default, allowing only the reviewed contract file. Never publish the private Git history, personal training examples, or learned checkpoints with a source release.

Install Microsoft's official workstation tools:

```powershell
winget install -e --id Microsoft.Azure.ArtifactSigningClientTools
```

Then supply the approved regional endpoint, account, certificate profile, and authentication mode to the installer build:

```powershell
.\eng\Build-ReplayFoundryInstaller.ps1 `
  -Version <version> `
  -YouTubeClientId <desktop-client-id>.apps.googleusercontent.com `
  -AdvancedInstallerUri https://replayfoundry.com/download `
  -Profile Base `
  -RuntimePackBuildRoot <advanced-pack-output> `
  -AdvancedPayloadMode Online `
  -AdvancedCatalogPath <external-output>\advanced-runtime-catalog.json `
  -ArtifactRoot <external-installer-output> `
  -ReleaseChannel Production `
  -SigningMode ArtifactSigning `
  -ArtifactSigningEndpoint https://<region>.codesigning.azure.net `
  -ArtifactSigningAccountName <account> `
  -ArtifactSigningCertificateProfileName <profile> `
  -ArtifactSigningAuthenticationMode InteractiveBrowser `
  -InstallerDownloadUri <public-https-installer-url>
```

The release flow signs and verifies the application and runtime-maintenance executable before sealing their hashes. Inno Setup uses the same signer for the embedded uninstaller and setup EXE. A missing signature, publisher mismatch, absent Microsoft RFC 3161 timestamp, non-Microsoft signing endpoint, dirty production source tree, or unsigned Production request is a hard failure.

Each successful build writes external release records for the application payload and final installer. Run the release guard on the signing workstation before producing a candidate:

```powershell
.\eng\Test-ReleaseEngineering.ps1 -RequireArtifactSigningClient
```

## Installation guarantees

- Manifests are validated before payload copying.
- Archive traversal, absolute paths, duplicate case-insensitive paths, undefined kinds or roles, invalid UTC values, missing hashes, and dependency cycles are rejected.
- Archives are checked for exact length and SHA-256 before extraction; declared files are checked during staging and at final install.
- Activation changes only after a content-addressed directory is complete.
- Versions install side by side and existing content is never overwritten in place.
- Repair restores the previous directory when replacement fails.
- Removal refuses to break retained dependencies; removing Advanced AI keeps Base media tools.
- Release startup resolves only active verified packs and never falls back to `PATH`.
- Temporary download and staging files are cleaned on success, failure, or cancellation.

## Release checklist

1. Finalize the version and date only after the code scope is frozen. Move the
   reviewed entries from `Unreleased` in [CHANGELOG.md](../../CHANGELOG.md) into
   that exact version without rewriting the historical record.
2. Update the root README, website Updates and Download pages, GitHub release
   notes, installer version, and artifact names from the same release record.
   Until the signed release exists, every public surface must continue to label
   the work as in testing and keep the previous release as the current download.
3. Build from a reviewed, committed, clean source tree. Run the repository
   payload, data-boundary, security, architecture, release-engineering, and full
   `verify` gates before creating a production snapshot.
4. Export production source from the reviewed Git index into a new empty
   directory. The manifest is an exact allowlist: never copy the development
   worktree, local `.codex-*` review directories, binaries, media, runtime/model
   archives, retained evidence, credentials, signing output, or machine data.
5. Compare the candidate with a fresh clone of the public repository. Preserve
   reviewed public-only gallery assets intentionally, verify every relative
   Markdown link and workflow action pin, and publish through a review branch or
   pull request rather than replacing `main` from a mutable local checkout.
6. Revalidate the Google OAuth production configuration and least-privilege
   Artifact Signing role.
7. Review the exact generated notices and every item in the
   [third-party compliance record](third-party-compliance.md).
   Advanced runtime packaging also checks the exact installed Python package inventory against OSV.
   An unavailable advisory service or a known advisory stops packaging. Upgrade affected dependencies,
   rerun the frozen model and structured-decoding qualification, and regenerate signed manifests;
   never patch files inside an already sealed or installed runtime pack.
8. Upload runtime archives and corresponding source to their final HTTPS
   locations, then regenerate and verify the catalog against those URLs.
9. Build, sign, timestamp, and verify the application, runtime-maintenance
   executable, uninstaller, and setup executable.
10. Run clean install, upgrade, repair, add/remove Advanced AI, uninstall,
    YouTube connect/disconnect, generation, Studio, render, Library, and Publish
    tests in clean supported Windows VMs.
    Launch setup and the installed app from Windows Explorer or the Start menu,
    outside a packaged development host. MSIX app-data redirection can make tools
    appear installed to a developer-launched process while remaining unavailable
    to a normal desktop launch. Verify the same media and AI capabilities after
    closing the app and reopening its installed shortcut. Also exercise repair
    with a missing cached installer and a deleted media tool in an isolated test
    installation; never delete a user's recording for this check.
11. Submit the signed candidate to Defender and reputation checks without
    bypassing Smart App Control or antivirus.
12. Publish only the signed installer, its release manifest, reviewed production
    source snapshot, required notices and corresponding source, release notes,
    and support links. Re-run the website production checks after deployment and
    verify the public download, health, and release-history URLs.

## References

- [.NET single-file deployment](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview)
- [Microsoft Artifact Signing integration](https://learn.microsoft.com/azure/artifact-signing/how-to-signing-integrations)
- [Windows code-signing options](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options)
- [Microsoft Smart App Control](https://learn.microsoft.com/windows/apps/develop/smart-app-control/overview)
- [Inno Setup components](https://jrsoftware.org/ishelp/topic_componentssection.htm)
- [Inno Setup signed files](https://jrsoftware.org/ishelp/topic_issig.htm)
- [Inno Setup commercial licenses](https://jrsoftware.org/isorder.php)
- [FFmpeg legal guidance](https://ffmpeg.org/legal.html)
