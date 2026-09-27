# Community writer pilot

Stages 1–2 provide owner review and scheduled **candidate** training. Stages 3–4
add independent evaluation, owner release approval and export into the existing
signed runtime-pack/installer pipeline. No model becomes public merely because
training loss improves, and no quality improvement is assumed before review.

The private `/learning` workspace on replayfoundry.com uses Sites sign-in and an
owner email allowlist. The existing D1 database holds opt-in text contributions,
reviews, immutable dataset membership and run receipts. Audio/video is excluded.
Sharing remains off until each creator consents; existing local edits are not
uploaded retroactively.

## Review and dataset selection

- Review original/saved wording, exact additions/removals and supporting text.
- Approve only explicitly edited/approved fields with supported facts and a
  completed privacy check. Generated drafts alone are not supervision.
- Factual corrections require a reviewed corrected event. Automated screening
  excludes incomplete evidence and obvious personal information/credentials;
  human review remains necessary.
- A batch requires 256 eligible examples, six contributors, twelve recordings
  and two games. It caps each contributor at 128 examples and removes duplicate
  wording. Train/development/final partitions remain disjoint by contributor.
- Shared data uses a distinct text-context protocol: it cannot reconstruct the
  original visual prompt or independently verify the raw media. Keep this
  limitation in evaluations before any future promotion.

## PC runner

`eng/Install-CommunityWriterPilot.ps1 -RuntimeRoot <qualified runtime root>
-WriterBase <qualified writer-base directory> -RegisterSchedule` installs a
private copy under `%LOCALAPPDATA%\ReplayFoundryCommunity`. Run it in PowerShell
as the PC user in Windows PowerShell 5.1. It prints only the credential's SHA-256 hash for the server.
`-InstallRoot <private directory>` supports a durable path visible to Task Scheduler
when a development host virtualizes AppData. Keep this directory out of source
control, website archives and app packages; it contains private training text.

Configure `LEARNING_ADMIN_EMAIL`, `LEARNING_RUNNER_TOKEN_SHA256` and the initial
`LEARNING_PILOT_ENABLED=1` in Sites runtime settings, then deploy. The initial
enabled value applies only before the control row exists; later pause/resume
choices in the dashboard take precedence.

The Windows task **Replay Foundry Community Writer Pilot** checks daily at 3 AM
and catches up when available while the user is logged in. It does not wake the
PC. The service admits at most one candidate per seven days and requires a new
eligible dataset. The worker defers while Replay Foundry is open and stops its
own training subprocess if the app opens. CUDA/BF16 and memory checks still
apply. Training uses the pinned Qwen3-0.6B base with the existing LoRA SFT/DPO
implementation. This is adapter training, not a foundation model trained from
scratch.

Credentials are DPAPI-protected for the Windows user; the server stores only a
hash. The runner accepts no remote code or arbitrary commands. Its scoped token
cannot approve contributions or change the schedule. The launcher checks the
copied Python source hashes. Reinstall the pilot after source/runtime upgrades.

`last-check.json` and `last-run.log` show the latest local result. Datasets,
private logs, adapters and receipts stay in `data\runs\<run-id>`. Personal app
learning files and installed model selection are untouched. Pause in the
dashboard or disable the Windows task to stop future training.

Deleting a contribution or changing its review invalidates affected candidates.
The PC purges revoked/expired run folders on its next successful check, including
checks when the app is open. This is not instantaneous while the PC is offline.
Deletion revokes associated evaluations and release authorizations, and removes
the release from website discovery. It cannot erase knowledge from an already
downloaded model. Issue a newer signed replacement for installed users; never
replace immutable artifacts or silently downgrade an installed copy.

## Qualification and release

After training, the PC generates comparisons against the installed production
model using the same text evidence. At least 24 reserved examples from two
unseen contributors are required. Contributor assignments persist between runs;
final examples are never added to optimization. The baseline comparison measures
the authoring model under the shared text protocol, not the full multimodal
generation pipeline or click-through rate.

Six additional probes cover malicious instructions in source text, uncertain
speaker attribution and invented outcomes. Automated screening flags obvious
personal information/credentials and unprompted 12-token training passages.
This screening is not a differential-privacy guarantee or a proof against
memorization. Every comparison and probe needs owner factual, privacy and
wording review in `/learning`. Release also requires at least 60% preference
score (ties count half) and a 95th-percentile latency ratio at most 1.25.

The owner approves an exact evaluation hash, app version and increasing build
revision. The runner credential cannot review, approve or publish. A separate
DPAPI-protected `release.dpapi` credential has only approved-export/publication
scope; configure its SHA-256 as `LEARNING_RELEASE_TOKEN_SHA256` on the website.
It is never inherited by the training subprocess.

`Export-CommunityWriterRelease.ps1` obtains fresh HTTPS authorization and checks
the exact adapter, training receipt, evaluation packet and inference protocol.
It exports exactly `adapter.safetensors` and `deployment.json`. Raw examples,
identifiers, training history and review text stay on the private PC/server.
`Build-ReplayFoundryRuntimePacks.ps1 -CommunityWriterReleaseId <id>
-CommunityWriterPilotRoot <private pilot root>` performs that export as part of
a new, immutable model pack. The existing signed installer seals its catalog
and the catalog binds every archive and file hash.

After signing and publishing the installer and runtime archives, run
`Complete-CommunityWriterRelease.ps1 -PilotRoot <root> -ReleaseId <id>
-InstallerManifestPath <manifest> -RuntimePackBuildRoot <packs>`. It validates
release boundaries, publisher signature, fresh approval, model identity and the
public installer's bytes before recording publication. The download page can
discover that receipt automatically. Publish the verified signed appcast using
the ordinary release workflow; automatic checks remain optional, and users
choose download/installation. Signing and public artifact publication remain
operator-controlled during this pilot.

The installed community adapter uses the same bounded evidence projection and
keeps the production planning, schema, factual, quality and reroll checks. A
qualified personal writer has priority. Missing or invalid community packages
leave the installed writer available. Whole-montage writing stays with the
existing writer until separately qualified. No community adapter is included
until sufficient real data passes these gates.

## Validation

The website tests cover authorization, source/privacy review, diversity gates,
dataset leases/export integrity, candidate-only completion and revocation. The
Python suite verifies reconstruction, exact edits, contributor partitions,
deferral and local revocation cleanup. A real community training run must wait
for sufficient reviewed contributions; synthetic tests are not community data.
