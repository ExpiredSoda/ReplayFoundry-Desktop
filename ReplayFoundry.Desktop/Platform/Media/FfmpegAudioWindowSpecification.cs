using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Signals;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed record FfmpegAudioWindowSpecification(
    int SampleRate,
    int SamplesPerWindow,
    TimeSpan ActualWindowDuration);
