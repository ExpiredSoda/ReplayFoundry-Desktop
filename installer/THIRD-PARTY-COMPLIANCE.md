# Third-party distribution compliance gate

This file records engineering provenance; it is not a legal opinion. Public redistribution remains blocked until the publisher reviews the exact generated pack manifests and the unresolved items below.

| Component | Pinned candidate | License/provenance retained | Current decision |
| --- | --- | --- | --- |
| FFmpeg/ffprobe | Replay Foundry shared Windows build from FFmpeg commit `8c9502e9b048e21e1cae96477e338ac0635645ba` and FFmpeg-Builds commit `8c736b2d6fe5da2a10a8896d01e53bfb0ca4f665` | LGPL text, exact build configuration, executable/DLL hashes, pinned build scripts, dependency sources, and corresponding-source archive | Engineering-qualified only after the hosted binary and corresponding-source URLs/hashes are sealed into media pack `8.1.2.32`. |
| H.264 encoding | Windows Media Foundation `h264_mf` software encoder | Shared deterministic bitrate/profile policy and model-free command-contract tests | OpenH264 is excluded from both the FFmpeg binary and application command paths. Publisher review of applicable codec/patent obligations remains a business/legal step. |
| Silero VAD | `v6.2.1` ONNX | MIT text, model hash, official GitHub source | Eligible after final manifest review. |
| whisper.cpp | `v1.9.1`, commit `f049fff95a089aa9969deb009cdd4892b3e74916` | MIT text, archive/executable/DLL hashes, official release URL | Eligible after final manifest review. |
| Whisper multilingual small | official GGML conversion at revision `5359861c739e955e79d9a303bcbc70fb988958b1` | OpenAI Whisper MIT text, model hash `1BE3A9B2063867B937E64E2EC7483364A79917E157FA98C5D94B5C1FFFEA987B` | Redistributable candidate. It produced tighter timings and materially better wording than base across the bounded five-clip creator-footage regression set; broad accuracy remains a research limitation. |
| CPython | `3.11.9` | PSF license text and executable hash | Eligible after generated notice review. |
| Python/Qwen wheel set | exact installed environment, including PyTorch `2.12.0+cu130` | Generated 40-component inventory plus a retained text/hash for every distribution. Missing wheel texts require explicit official, hash-pinned overrides. | Technically inventoried; publisher must review CUDA/NVIDIA and every generated notice before release. |
| Qwen3-VL 4B Instruct | model revision `ebb281ec70b05090aa6165b016eac8ec08e71b17` | Apache-2.0 text, model/config hashes, prompt manifest, qualification lock | Locally qualified for the bounded shipped workflow. Generated wording remains user-reviewable; no universal semantic-accuracy claim is made. |

Each generated pack repeats its own license/source/build provenance and hashes. The generated Qwen notice tree is external build input and is copied into the runtime pack; it is not committed to Git.

Official references:

- [FFmpeg legal considerations](https://ffmpeg.org/legal.html)
- [OpenH264 licensing FAQ](https://www.openh264.org/faq.html)
- [whisper.cpp model documentation](https://github.com/ggml-org/whisper.cpp/blob/master/models/README.md)
- [Qwen3-VL 4B pinned model tree](https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct/tree/ebb281ec70b05090aa6165b016eac8ec08e71b17)
- [Python embeddable/package guidance](https://docs.python.org/3/using/windows.html#the-embeddable-package)
- [Python virtual environments are not portable](https://docs.python.org/3/library/venv.html)
