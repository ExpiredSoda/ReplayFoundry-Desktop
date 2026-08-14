# Replay Foundry Windows distribution

Replay Foundry has two explicit Windows x64 profiles. Both are per-user installs and never modify `PATH`.

| Profile | Works offline after setup download | Installed capability |
| --- | --- | --- |
| **Base** | Yes | Self-contained WPF app plus verified FFmpeg/ffprobe pack. Deterministic evidence and Moment Finder remain usable with no AI model. |
| **Advanced** | No; the current one-EXE bootstrapper needs a connection during installation | Base plus Silero VAD, whisper.cpp CPU runtime, the multilingual Whisper small model, and the locally qualified Qwen3-VL CUDA runtime/model. |

Advanced does not change the deterministic fallback. If an optional pack is unavailable or corrupt, its capability is reported unavailable; Replay Foundry does not search `PATH`, substitute another model, or silently weaken the selected analysis mode. Generated Qwen wording remains editable and reviewable rather than carrying an accuracy guarantee. The Whisper small selection is locally qualified for the shipped caption workflow, not a universal transcription-accuracy claim.

The app and runtime packs are deliberately separate. Native executables, DLLs, Python wheels, and model weights must exist on disk to run. They live in compact, full-SHA-256 content-addressed directories under `%LOCALAPPDATA%\ReplayFoundry\R`, while the app lives under `%LOCALAPPDATA%\Programs\Replay Foundry`. The compact physical layout is intentional: several pinned Python native modules cannot load from deeply nested Windows paths. Package names, versions, kinds, licenses, and full hashes remain in the verified manifests and Settings projection.

Package installation reads the small canonical manifests first and hashes only
the target payload plus its selected dependency closure. A package with no
dependencies never rescans unrelated installed models or runtimes. Explicit
repair and full verification still hash every file they are asked to audit;
the optimization does not weaken integrity checks.

## Reproducible build flow

All payload roots and outputs must be outside the repository. First generate the exact Python/wheel notices. Missing wheel license text is a hard failure unless a hash-pinned official override manifest is supplied.

```powershell
eng\New-PythonRuntimeNotices.ps1 `
  -PythonHome <cpython-root> `
  -SitePackages <pinned-site-packages> `
  -LicenseOverrideManifest <reviewed-official-overrides.json> `
  -OutputDirectory <external-notice-output>
```

Then assemble fixed Base or Advanced packs. `CreatedAtUtc` is an input so identical payloads and provenance produce deterministic manifest hashes.

```powershell
eng\Build-ReplayFoundryRuntimePacks.ps1 `
  -Profile Base `
  -OutputDirectory <external-pack-output> `
  -CreatedAtUtc 2026-08-02T00:00:00Z `
  -MediaToolsRoot <pinned-lgpl-ffmpeg-root> `
  -MediaToolsArchiveSha256 <sha256> `
  -MediaToolsArchiveUrl <permanent-https-binary-url> `
  -MediaToolsSourceArchiveUrl <permanent-https-corresponding-source-url> `
  -MediaToolsSourceArchiveSha256 <sha256>
```

Advanced additionally requires the pinned Silero ONNX model, the official
whisper.cpp GGML Silero model (`-WhisperVadModelPath`), whisper.cpp, the
Whisper model, relocatable CPython/site-packages, Qwen
host/model/configuration, qualification lock, and license paths. The builder
copies only fixed package kinds, seals every file with size and SHA-256,
verifies each pack, and launch-checks the relocated Qwen host before emitting
the build index.

For an online Advanced build, create a catalog from the verified pack index. The catalog records HTTPS URLs, byte lengths, archive hashes, fixed kinds, versions, and reviewed redirect hosts.

```powershell
eng\New-ReplayFoundryRuntimePackCatalog.ps1 `
  -RuntimePackBuildRoot <advanced-pack-output> `
  -BaseUri https://downloads.example.com/runtime-packs/0.2.0/ `
  -ApprovedRedirectHosts cdn.example.com `
  -OutputPath <external-output>\advanced-runtime-catalog.json
```

Finally compile an installer profile:

```powershell
eng\Build-ReplayFoundryInstaller.ps1 `
  -Version 0.2.0 `
  -YouTubeClientId YOUR_DESKTOP_CLIENT_ID.apps.googleusercontent.com `
  -AdvancedInstallerUri https://downloads.example.com/ReplayFoundry-Advanced.exe `
  -Profile Base `
  -RuntimePackBuildRoot <base-pack-output> `
  -ArtifactRoot <external-installer-output>

eng\Build-ReplayFoundryInstaller.ps1 `
  -Version 0.2.0 `
  -YouTubeClientId YOUR_DESKTOP_CLIENT_ID.apps.googleusercontent.com `
  -AdvancedInstallerUri https://downloads.example.com/ReplayFoundry-Advanced.exe `
  -Profile Advanced `
  -RuntimePackBuildRoot <advanced-pack-output> `
  -AdvancedPayloadMode Online `
  -AdvancedCatalogPath <external-output>\advanced-runtime-catalog.json `
  -ArtifactRoot <external-installer-output>
```

Both examples create explicitly unsigned `Development` installers. They are
useful for local installation and VM validation, but they are intentionally
ineligible for a public release.

The installer build generates its presentation assets deterministically into
`<ArtifactRoot>\branding`; no generated PNG or manually edited installer
bitmap is committed. `eng\New-ReplayFoundryInstallerBranding.ps1` composites
the canonical 1024-pixel ReplayFoundry logo onto the restrained dark/cyan/yellow
installer canvas, records every input and output hash, and preserves the
497:360 Inno wizard aspect ratio at high DPI. The setup uses the same canonical
ICO as the app, clears the decorative background when Windows high contrast is
active, and keeps every label on standard Inno controls for accessibility.

The build recognizes official per-user Inno Setup 6.7 installations at
`%LOCALAPPDATA%\Programs\Inno`, system installations, and side-by-side Inno 7.
An exact compiler can still be selected with `-InnoCompilerPath`. Commercial
license activation remains owned by the installed Inno IDE/current-user
configuration; ReplayFoundry never reads, copies, or stores a license key.

Before producing a release candidate, run the focused visual/build guard:

```powershell
eng\Test-InstallerBranding.ps1
```

It regenerates the assets twice, compares their bytes, checks dimensions,
palette and source provenance, then compiles (but does not run) a minimal
branded setup using the discovered official Inno compiler.

## Microsoft Artifact Signing release flow

ReplayFoundry uses Microsoft Artifact Signing Public Trust for the public
Windows release. The legal publisher is `Expired Soda Studios LLC`, and all
installer product, support, and update links use `https://replayfoundry.com`.
No certificate, private key, OAuth token, or Azure credential is accepted by
the build scripts or stored in source control.

Install Microsoft's current official workstation tools:

```powershell
winget install -e --id Microsoft.Azure.ArtifactSigningClientTools
```

Microsoft also documents the signed `Microsoft.ArtifactSigning.Client` NuGet
payload for signing nodes that use an external tool cache. Verify its NuGet
author and repository signatures, keep the complete `bin/x64` directory
outside Git, and pass its `Azure.CodeSigning.Dlib.dll` with
`-ArtifactSigningDlibPath`. Do not copy only the dlib; its adjacent runtime
dependencies are required.

After Microsoft activates the organization identity and a Public Trust
certificate profile exists, build a signed release by passing the profile's
regional endpoint, account name, and profile name:

```powershell
eng\Build-ReplayFoundryInstaller.ps1 `
  -Version 1.0.0 `
  -YouTubeClientId YOUR_DESKTOP_CLIENT_ID.apps.googleusercontent.com `
  -AdvancedInstallerUri https://replayfoundry.com/download `
  -Profile Base `
  -RuntimePackBuildRoot <base-pack-output> `
  -ArtifactRoot <external-installer-output> `
  -ReleaseChannel Production `
  -SigningMode ArtifactSigning `
  -ArtifactSigningEndpoint https://<region>.codesigning.azure.net `
  -ArtifactSigningAccountName <account> `
  -ArtifactSigningCertificateProfileName <public-trust-profile> `
  -ArtifactSigningAuthenticationMode InteractiveBrowser `
  -InstallerDownloadUri https://replayfoundry.com/releases/ReplayFoundry-1.0.0-Base-win-x64-setup.exe
```

The release flow signs and verifies the ReplayFoundry app and runtime
maintenance executable before their hashes are sealed. Inno Setup then uses
the same signer for both the embedded uninstaller and the final setup EXE. The
Microsoft RFC 3161 timestamp service is mandatory because Artifact Signing
certificates are short-lived. Any missing signature, publisher mismatch, absent
timestamp, non-Microsoft signing endpoint, or unsigned Production request is a
hard failure. Production also requires a clean Git working tree so the signed
payload cannot drift from the source commit recorded in its manifests.

Each build emits two external records:

- `app/release-manifest.json` seals the signed application payload.
- `installer-release-manifest.json` seals the installer, pack index, optional
  Advanced catalog, public download URL, signer identity, and source commit.

Run `eng\Test-ReleaseEngineering.ps1 -RequireArtifactSigningClient` on a
signing workstation before attempting a public build. A verified external
client payload can instead be supplied with `-ArtifactSigningDlibPath`. The current workstation
may use `InteractiveBrowser`; unattended release agents should use an approved
Azure CLI, workload, managed, or environment identity with the least-privilege
Artifact Signing Certificate Profile Signer role.

When the support website is ready, either command may additionally pass
`-UserReportEndpoint https://support.example.com/api/reports`. The value is
optional: omitting it keeps feedback and crash reports local. The app accepts
only one fixed HTTPS URL, follows no redirects, and has no fallback endpoint.

The current Advanced payload is approximately 12.5 GB. Inno Setup requires external disk slices once compressed setup data exceeds 4.2 GB, so this payload cannot honestly be delivered as one offline EXE. The build script rejects an oversized `Embedded` profile early. `Embedded` remains available only for a future verified payload below the conservative 4.0 GB one-file ceiling. The current online bootstrapper remains one user-facing EXE while keeping pack repair and upgrades independent.

## Installation and maintenance guarantees

- A manifest is validated before payload copying.
- Archive traversal, absolute paths, duplicate case-insensitive paths, undefined kinds/roles, invalid UTC, missing hashes, and dependency cycles are rejected.
- Every archive is checked for exact length and SHA-256 before extraction.
- Every declared file is checked before staging, after staging, and at final install.
- Activation changes only after a content-addressed directory is complete.
- Versions install side by side; existing content is never overwritten in place.
- Repair re-verifies/reinstalls from an approved source and restores the previous directory on failure.
- Removal refuses to break retained dependencies. “Remove Advanced AI” keeps Base media tools.
- Startup resolves only active verified packs. It does not use `PATH` in Release builds.
- Temporary download and staging files are cleaned on success, failure, or cancellation.

Settings displays each installed capability, size, version, license summary, and availability. It can open the Advanced installer, invoke repair when a retained installer is local, remove optional packs, or open the package folder. Settings does not contain download, hashing, process, or package-store logic.

## External trust approvals confirmed

The following publisher-controlled prerequisites were verified in their
authoritative portals on August 13, 2026. They are release inputs, not values
stored in source control:

- Google OAuth branding for ReplayFoundry is verified, the external audience
  is in production, and the requested YouTube management scope is verified.
- Microsoft Artifact Signing organization identity is approved and the
  `replayfoundry-public` Public Trust certificate profile is active for
  `Expired Soda Studios LLC`.

Revalidate both states before every production build. Artifact Signing uses
short-lived certificates and stops renewing them if the underlying identity
validation expires. The production build must still perform a real signed
transaction and verify the publisher and RFC 3161 timestamp; portal status by
itself is not release proof.

## Public-release checklist

The implementation is release-shaped, but locally generated proof installers
are **not public release candidates** until all of these publisher-owned steps
are complete:

1. Grant the release identity the least-privilege Artifact Signing Certificate Profile Signer role, install the official client tools, and prove a signed and timestamped candidate transaction.
2. Supply the verified Google Desktop OAuth client pair from the release environment. Keep the values out of source control and logs; the installed-app secret is distributed with the signed application and is not a confidential server credential.
3. Host each catalog archive at its pinned HTTPS URL and publish the reviewed catalog through the signed installer.
4. Resolve every item in [THIRD-PARTY-COMPLIANCE.md](THIRD-PARTY-COMPLIANCE.md), especially FFmpeg/OpenH264 redistribution provenance.
5. Purchase and retain an appropriate Inno Setup commercial license before the first production release. The publisher is an LLC with planned paid add-ons; the current compiler reports non-commercial use. Inno states that commercial licenses are requested rather than technically enforced, so this remains a publisher compliance record rather than a build-secret requirement.
6. Run clean install, upgrade, repair, remove-Advanced, uninstall, YouTube connect/disconnect, and generation/render smoke tests in clean Windows 11 VMs.
7. Submit signed candidates to Defender/reputation checks without bypassing Smart App Control or antivirus. Artifact Signing removes the unknown publisher state, but SmartScreen reputation still accumulates per released file hash.

The Google Desktop OAuth identity and optional report endpoint are application
configuration. The publish script reads the paired desktop secret from the
process environment and never writes it into the repository. Signing material
is never accepted by these scripts or written into the repository.

## References

- [.NET single-file deployment](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview)
- [Microsoft Artifact Signing integration](https://learn.microsoft.com/azure/artifact-signing/how-to-signing-integrations)
- [Windows code-signing options](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options)
- [Microsoft Smart App Control](https://learn.microsoft.com/windows/apps/develop/smart-app-control/overview)
- [Inno Setup components](https://jrsoftware.org/ishelp/topic_componentssection.htm)
- [Inno Setup signed files](https://jrsoftware.org/ishelp/topic_issig.htm)
- [Inno Setup commercial licenses](https://jrsoftware.org/isorder.php)
- [FFmpeg download and legal pages](https://ffmpeg.org/download.html)
