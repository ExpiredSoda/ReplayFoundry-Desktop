# Local media tools

Replay Foundry does not commit arbitrary FFmpeg binaries to the repository.

For a Debug build, provide both `ffprobe.exe` and `ffmpeg.exe` through one of these supported sources:

1. Explicit full paths in `REPLAYFOUNDRY_FFPROBE_PATH` and `REPLAYFOUNDRY_FFMPEG_PATH`.
2. The verified active media-tools runtime pack.
3. `src/ReplayFoundry.Desktop/Tools/FFmpeg/` or its `bin/` child.
4. A directory listed in Windows `PATH`.

Debug resolution follows that order so an explicit test configuration remains deterministic. Structural inspection records the resolved ffprobe path and version; deterministic evidence analysis records the resolved FFmpeg path and version.

Release builds accept only the verified active media-tools pack. They do not silently substitute a repository-local or `PATH` executable when the qualified pack is missing or corrupt.

Before distributing a build, review the exact FFmpeg configuration, enabled components, source and binary hashes, notices, and corresponding-source location. The [third-party compliance record](../distribution/third-party-compliance.md) and [Windows distribution guide](../distribution/windows.md) define the release boundary.
