# Replay Foundry change log

This file records user-visible product changes and the release-engineering work
that supports them. Versions follow the public GitHub releases. The current
downloadable version is [1.0.0 Beta 3](https://github.com/ExpiredSoda/ReplayFoundry-Desktop/releases/tag/v1.0.0-beta.3).

## Unreleased

No user-visible changes have been recorded since 1.0.0 Beta 3.

## 1.0.0 Beta 3 — 2026-09-01

### Generate and moment selection

- Added a quality-qualified gameplay-event coverage policy. When a generation
  contains at least three clips and the source contains a strong gameplay event,
  the portfolio can include action, combat, movement, or a visual payoff instead
  of allowing dialogue-heavy candidates to occupy every slot. Commentary-focused
  generations retain their explicit preference and weak action is not forced.
- Added natural-ending refinement that can follow continuing speech and short
  scene tails while rejecting unsafe or premature cuts.
- Expanded candidate evidence, portfolio diversity, event ranking, and the
  plain-language explanation of why each clip was selected.
- Redesigned game lookup to show a distinct result, hover, and selected state.
  Search results provide clearer game, edition, year, developer, series, and
  non-game disambiguation before Next becomes available.
- Kept Wikimedia lookup opt-in and limited its request to the confirmed game
  name. Added bounded retries, cached attributable results, and offline recovery.
- Invalid supported game-context memory is now quarantined and rebuilt instead
  of preventing the generation setup window from opening.

### Local AI titles and descriptions

- Fixed Local AI appearing selected while its runtime was unavailable. Generation
  now waits for an explicit available writing choice; Simple / No AI is never
  selected silently.
- Added precise compatibility checks for the qualified Qwen runtime `0.8.22`,
  Qwen3-VL model `4.0.18`, and editorial prompt `1.40`. An older or mismatched
  pack is rejected before generation with an actionable explanation.
- Required-AI workflows fail closed when the provider, runtime, or model cannot
  finish. Heuristic copy is used only when the creator explicitly selects the
  no-AI writing mode.
- Improved grounded metadata with commentary-aware evidence, canonical game
  hashtags, title-family novelty checks, description/title redundancy review,
  generic or overly literal wording rejection, and bounded corrective rewrites.
- Retained the best safe grounded draft when a preferred rewrite is weaker, so
  useful AI work is not discarded solely because a later novelty attempt fails.
- Shared the qualified visual-model lease across observation and editorial work
  to avoid unnecessary model reloads during one generation.

### Captions and audio timing

- Corrected Whisper/VAD time remapping so aligned words no longer stretch across
  removed silence into real pauses.
- Punchy captions now cap a visible phrase at three words and split longer speech
  runs at meaningful pauses. Karaoke and word-focus presentation uses exact word
  timing when available and a localized phrase fallback only when exact alignment
  cannot be proven.
- Unified timed preview and rendered ASS caption presentation, and prevented
  edited caption text from retaining stale word timings.
- Preserved the full timed transcript when a Studio cut is extended and when a
  clip is added from second-look moments.
- Added one atomic action for applying the current caption look to every
  captioned clip in a project.

### Studio and second-look moments

- Metadata review is optional rather than a prerequisite for queueing, rendering,
  or handing a clip off. Queueing saves the exact visible title, description,
  tags, and current cut.
- Made the complete clip card selectable while preserving nested Add, Exclude,
  Restore, and edit actions. Clarified button, destructive-action, selection,
  and focus treatment without duplicate rims.
- Added a background queue for alternate moments. **Add to Studio** now enqueues
  preparation and advances immediately; the queue remains active if the review
  window closes and is visible when the window reopens.
- Alternate items now run the same caption preparation and current AI editorial
  pipeline as primary clips before being committed to Studio. Items expose
  queued, preparing, ready, retry, needs-attention, cancel, and cancel-all states.
- Added Previous and Next navigation independent of Skip or Add, animated
  liveness feedback, elapsed-time messaging, and bounded preview warmup for the
  current and next alternate.
- Added safer project switching, pending-edit commits, render cancellation, and
  protection against overwriting a newer saved project version.

### Loading, playback, and rendering

- Introduced shared compact, standard, and featured progress components with a
  real indeterminate animation. Countable work such as transcription remains
  determinate; friendly generation-stage copy remains separate from the shared
  control.
- Applied the shared loading treatment to Generate, second-look preparation, and
  Studio rendering so long work visibly remains active.
- Playing a video after it reaches the end now restarts at zero.
- Corrected the zero-position playhead in Studio, Library, and Publish and
  hardened seeking, stale media-event rejection, stalled playback recovery, and
  preview-cache leases.

### Library

- Added file-availability monitoring so a video restored outside Replay Foundry
  becomes usable again without restarting the app.
- Added deduplicated background thumbnail regeneration through the qualified
  FFmpeg runtime when a sidecar thumbnail is missing. Invalid image files fail
  soft instead of crashing the Library.
- Fixed mouse-wheel scrolling over the Projects area and added a recycling,
  virtualized card layout for larger local libraries.
- Fixed replay-from-end behavior in Library preview.

### Publish and YouTube

- A drop onto a past calendar date now gives clear in-calendar feedback instead
  of silently doing nothing or opening a misleading preparation dialog.
- Added a scalable local YouTube release log with a five-item recent view and a
  full history window supporting title search, status and date filters, newest or
  oldest ordering, grouping, paging, record inspection, and trusted YouTube links.
- Improved resumable upload completion by polling for the finalized YouTube video
  ID after terminal byte transfer rather than reporting a false completion.
- Hardened Publish shutdown, command completion, draft persistence, metadata
  history, and editorial rerolls.

### App-wide interface and accessibility

- Consolidated text, control, button, card, input, scroll, tab, progress, and
  window styles. Replaced technical status copy with task-focused language and
  made interactive text and buttons visually distinct.
- Removed duplicate focus and selection borders, reduced oversized delete icons,
  improved dark-surface contrast, and strengthened high-contrast, keyboard,
  responsive-layout, reduced-motion, and accessible-name behavior.
- Replaced bulky service labels with compact, truthful shell indicators. The AI
  indicator lights only when usable local AI tooling is installed; Wikimedia is
  reported separately when context lookup is relevant.

### Installer, runtime packs, and local data

- Redesigned installer Welcome and Finish artwork, inner-page branding, spacing,
  setup copy, and progress presentation. Advanced AI remains optional, unchecked,
  labeled at approximately 12.5 GB, and recommended for compatible NVIDIA PCs.
- Corrected shortcut naming and legacy shortcut cleanup, and expanded installer
  branding, high-contrast, and real Inno Setup compile validation.
- Prepared updated runtime definitions for media tools `8.1.2.32`, Silero speech
  `6.2.1.1`, whisper.cpp `1.9.1`, multilingual Whisper small `1.0.0`, the Qwen
  runtime `0.8.22`, and the Qwen3-VL 4B model `4.0.18`.
- Added stricter cross-pack compatibility, manifest sealing, active-pack checks,
  and transactional runtime mutation.
- Isolated Production, Development, and Test mutable data while continuing to
  share only immutable runtime assets. Cache and preference cleanup preserves
  projects, Library records, outputs, and runtime packs unless explicitly chosen.
- Kept diagnostics sanitized, local by default, and transmitted only after the
  creator reviews and explicitly sends a report.

### Repository and release engineering

- Reorganized source into `src/`, `tests/`, `tools/`, `docs/`, `eng/`, and
  `installer/`, with one `ReplayFoundry.slnx` and one `eng/ReplayFoundry.ps1`
  console for build, verification, runtime, installer, and production-export work.
- Added shared test infrastructure, smaller composition responsibilities, source
  budgets, and focused architecture, security, UI/UX, runtime, installer, and
  release guards.
- Added a Git-index-based production export with exact public allowlists. The
  public snapshot excludes development hosts, research and diagnostic tooling,
  retained evidence, models, native runtimes, media, binaries, machine-local
  data, credentials, signing output, and local review artifacts.
- Strengthened installer, manifest, signature, runtime-pack, data-channel, payload,
  and clean-tree checks used before a production source snapshot can be published,
  including indexed archive verification that keeps large runtime packs practical
  without repeatedly comparing every manifest record with every ZIP entry.
- Corrected the public release-boundary scanner so valid zero-byte text files are
  treated as empty content instead of stopping release verification.

### Known limitations

- Beta 3 remains prerelease software. Back up important work before testing it.
- Gameplay coverage selects a strong qualifying event when one exists; it does
  not promise combat in every source or generation.
- Exact word-level captions depend on reliable word alignment. Replay Foundry
  uses a truthful short-phrase fallback when exact alignment is unavailable.
- The alternate-moment queue survives closing its review window, but not an app
  restart; app shutdown cancels remaining work.
- YouTube history is stored on this PC and is not a cloud-synchronized history.
- StrategyWiki is not an active online integration.

Production source: [v1.0.0-beta.3](https://github.com/ExpiredSoda/ReplayFoundry-Desktop/tree/v1.0.0-beta.3)

## 1.0.0 Beta 2 — 2026-08-14

- Published the first Microsoft-signed public beta for Windows 10 and 11.
- Added a small Base installer and an optional Advanced AI setup path. Advanced
  downloaded six hash-verified local packs (about 12.5 GB) for speech timing,
  transcription, visual observation, and grounded title/description writing.
- Added strict runtime-pack manifests, dependency and compatibility checks,
  content hashes, verified activation, repair, and side-by-side installation.
- Added local transcription and qualified Qwen3-VL editorial generation while
  keeping source media and AI processing on the creator's PC.
- Published the reviewed production source snapshot, installer manifest, notices,
  product workflow demo, and release notes alongside the signed installer.

Production source: [`04c2c9b`](https://github.com/ExpiredSoda/ReplayFoundry-Desktop/commit/04c2c9b)

## 1.0.0 Beta 1 — 2026-08-14

- Built the initial private, unsigned Base beta used to verify clean installation,
  the Generate → Studio → Library → Publish workflow, and complete removal on a
  fresh Windows PC.
- Did not include the Advanced AI, transcription, or production-signing path.
