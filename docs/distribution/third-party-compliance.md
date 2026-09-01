# Third-party compliance record

This document records engineering provenance and release requirements; it is not legal advice. The generated manifests and notice trees for the exact candidate remain authoritative. Review them before every public release because a dependency, build configuration, source archive, or license can change independently of this baseline.

| Component | Qualified baseline | Required release evidence |
| --- | --- | --- |
| .NET Windows Desktop Runtime | Exact .NET 10 `win-x64` runtime resolved by the release SDK for the self-contained app | Record the SDK and runtime versions, preserve Microsoft's license and third-party notices, and hash the final framework files shipped with the candidate. |
| Microsoft.ML.OnnxRuntime | NuGet package `1.28.0` | MIT text, official NuGet package identity and hash, native-runtime inventory, and final shipped-file hashes. |
| FFmpeg and ffprobe | Replay Foundry Windows build from FFmpeg commit `8c9502e9b048e21e1cae96477e338ac0635645ba` and FFmpeg-Builds commit `2a3249ec58228c661e7ff8fdc9ea997b18aa912b` | LGPL text, exact configuration, executable and DLL hashes, pinned build scripts, dependency sources, and the [corresponding-source archive](https://downloads.replayfoundry.com/corresponding-source/ReplayFoundry-FFmpeg-8.1.2.32-corresponding-source.tar.zst), SHA-256 `3F1ED7FACB12DD4AE66798D3961202FBB0C77617015CE46F8F9131F38DB6A82E`. |
| H.264 encoding | Windows Media Foundation `h264_mf` software encoder | Preserve the deterministic bitrate and profile policy. OpenH264 must remain excluded from both the FFmpeg build and application command paths unless a separate review explicitly changes that decision. Review applicable codec and patent obligations. |
| Silero VAD | `v6.2.1` ONNX | MIT text, official source, model hash, and generated manifest entry. |
| whisper.cpp | `v1.9.1`, commit `f049fff95a089aa9969deb009cdd4892b3e74916` | MIT text, official release URL, and archive, executable, and DLL hashes. |
| Whisper multilingual small | Official GGML conversion at revision `5359861c739e955e79d9a303bcbc70fb988958b1` | OpenAI Whisper MIT text and model hash `1BE3A9B2063867B937E64E2EC7483364A79917E157FA98C5D94B5C1FFFEA987B`. Qualification applies to the bounded Replay Foundry caption workflow, not universal transcription accuracy. |
| CPython | `3.11.9` | PSF license text, official distribution source, and executable hash. |
| Python, CUDA, and Qwen dependencies | Exact qualified environment, including PyTorch `2.12.0+cu130` | Generated component inventory and a retained license text and hash for every distribution. Missing wheel texts require reviewed, official, hash-pinned overrides. Review NVIDIA and CUDA terms for the exact redistributed files. |
| Qwen3-VL 4B Instruct | Model revision `ebb281ec70b05090aa6165b016eac8ec08e71b17` | Apache-2.0 text, official source, model and configuration hashes, prompt manifest, and qualification lock. Generated wording remains subject to user review; qualification is not a universal semantic-accuracy claim. |
| Inno Setup | Exact supported Inno Setup 6.7 or 7 compiler selected for the release | Record the compiler version and hash, retain the applicable Inno license evidence, confirm the licensed build environment, and preserve the signed setup and uninstaller verification report. |

Each runtime pack repeats its own package identity, license, source, build provenance, dependency closure, file lengths, and SHA-256 hashes. Generated Python and Qwen notice trees are external build inputs copied into the runtime pack; they do not belong in Git.

For the complete packaging and signing sequence, see [Windows distribution](windows.md).

## Official references

- [FFmpeg legal guidance](https://ffmpeg.org/legal.html)
- [OpenH264 licensing FAQ](https://www.openh264.org/faq.html)
- [whisper.cpp model documentation](https://github.com/ggml-org/whisper.cpp/blob/master/models/README.md)
- [Qwen3-VL 4B pinned model tree](https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct/tree/ebb281ec70b05090aa6165b016eac8ec08e71b17)
- [Python embeddable package guidance](https://docs.python.org/3/using/windows.html#the-embeddable-package)
- [Python virtual environments are not portable](https://docs.python.org/3/library/venv.html)
- [.NET distribution license](https://dotnet.microsoft.com/dotnet_library_license.htm)
- [ONNX Runtime license](https://github.com/microsoft/onnxruntime/blob/main/LICENSE)
- [Inno Setup licensing](https://jrsoftware.org/isinfo.php)
