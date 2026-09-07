# Third-party compliance record

This document records engineering provenance and release requirements; it is not legal advice. The generated manifests and notice trees for the exact candidate remain authoritative. Review them before every public release because a dependency, build configuration, source archive, or license can change independently of this baseline.

| Component | Qualified baseline | Required release evidence |
| --- | --- | --- |
| .NET Windows Desktop Runtime | Exact .NET 10 `win-x64` runtime resolved by the release SDK for the self-contained app | Record the SDK and runtime versions, preserve Microsoft's license and third-party notices, and hash the final framework files shipped with the candidate. |
| Microsoft.ML.OnnxRuntime | NuGet package `1.28.0` | MIT text, official NuGet package identity and hash, native-runtime inventory, and final shipped-file hashes. |
| Corrected English caption alignment | On-demand `Xenova/wav2vec2-base-960h` INT8 ONNX revision `a19f851b3d42865797e410752b4c570c871e4825`,95,286,046 bytes, SHA256 `CD5040C147381580ED73258143DD8E0C28E800A09E74EE42EE2B3E8CB4D760A3` | Base model card declares Apache-2.0. Embedded license/provenance are written beside the verified model; preserve these and the exact conversion provenance. Qualified CPU phrase workflow is separate from general ASR accuracy. This on-demand artifact is not an already published runtime-pack release. |
| English semantic text retrieval | On-demand `Xenova/all-MiniLM-L6-v2` INT8 ONNX revision `751bff37182d3f1213fa05d7196b954e230abad9`; model 22,972,370 bytes, SHA256 `AFDB6F1A0E45B715D0BB9B11772F032C399BABD23BFC31FED1C170AFC848BDB1`; vocabulary 231,508 bytes, SHA256 `07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3` | Apache-2.0 license and conversion provenance are embedded in the application and written beside the verified downloads. Preserve both pinned artifacts and their notices. Similarity is retrieval relevance, not proof of an event. The model is downloaded on first use and is separate from the fixed Advanced AI runtime-pack catalog. |
| Caption fonts | Installed Windows font families selected by name; unavailable families resolve through the shared preview/export fallback | No font files are bundled by the current desktop project. Keep this distinction in release notes; do not add or redistribute font files without the corresponding license and packaging review. |
| FFmpeg and ffprobe | Replay Foundry Windows build from FFmpeg commit `8c9502e9b048e21e1cae96477e338ac0635645ba` and FFmpeg-Builds commit `2a3249ec58228c661e7ff8fdc9ea997b18aa912b` | LGPL text, exact configuration, executable and DLL hashes, pinned build scripts, dependency sources, and the [corresponding-source archive](https://downloads.replayfoundry.com/corresponding-source/ReplayFoundry-FFmpeg-8.1.2.32-corresponding-source.tar.zst), SHA-256 `3F1ED7FACB12DD4AE66798D3961202FBB0C77617015CE46F8F9131F38DB6A82E`. |
| H.264 encoding | Windows Media Foundation `h264_mf` software fallback; optional locally qualified NVENC, QSV, AMF, or Media Foundation hardware encoder from the same pinned FFmpeg | Preserve the bitrate and profile policy. Admit hardware only after a successful local encode/decode check; retry the software path when the real hardware encode or final validation fails. Retain exact runtime/driver qualification evidence for release testing. OpenH264 remains excluded from the FFmpeg build and application command paths. Review applicable codec and patent obligations. |
| Silero VAD | `v6.2.1` ONNX | MIT text, official source, model hash, and generated manifest entry. |
| whisper.cpp | `v1.9.1`, commit `f049fff95a089aa9969deb009cdd4892b3e74916` | MIT text, official release URL, and archive, executable, and DLL hashes. |
| Whisper multilingual small | Official GGML conversion at revision `5359861c739e955e79d9a303bcbc70fb988958b1` | OpenAI Whisper MIT text and model hash `1BE3A9B2063867B937E64E2EC7483364A79917E157FA98C5D94B5C1FFFEA987B`. Qualification applies to the bounded Replay Foundry caption workflow, not universal transcription accuracy. |
| CPython | `3.13.15` | PSF license text, official distribution source, and executable hash. |
| Python, CUDA, and Qwen dependencies | Exact qualified environment, including PyTorch `2.13.0+cu130` | Generated component inventory and a retained license text and hash for every distribution. Missing wheel texts require reviewed, official, hash-pinned overrides. Review NVIDIA and CUDA terms for the exact redistributed files. |
| Qwen3-VL 4B Instruct | Model revision `ebb281ec70b05090aa6165b016eac8ec08e71b17` | Apache-2.0 text, official source, model and configuration hashes, prompt manifest, and qualification lock. Generated wording remains subject to user review; qualification is not a universal semantic-accuracy claim. |
| Optional personal writer base | `Qwen/Qwen3-0.6B`, revision `c1899de289a04d12100db370d81485cdf75e47ca`; weights SHA-256 `F47F71177F32BCD101B7573EC9171E6A57F4F4D31148D38E382306F42996874B` | Preserve the Apache-2.0 license and all eight pinned model/tokenizer files under `writer-base` in model pack `4.0.22`. The canonical local manifest hash is `f76861238256bfe8537860a92d1918fc3a0fe781d56c184609e48ce57a322256`. Test adapters cannot activate a personal writer. Keep training examples, checkpoints and the proprietary training implementation outside the public source export. |
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
