using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegAudioEvidenceCommandBuilder
{
    public static IReadOnlyList<string> BuildArguments(
        AudioStreamInfo audioStream,
        MediaEvidenceAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        ArgumentNullException.ThrowIfNull(options);

        FfmpegAudioWindowSpecification window =
            CreateWindowSpecification(
                audioStream,
                options.AudioSignalWindowDuration);

        string streamKey =
            audioStream.Index.ToString(
                CultureInfo.InvariantCulture);

        string silenceInput =
            $"[rf_silence_in_{streamKey}]";

        string signalInput =
            $"[rf_audio_signal_in_{streamKey}]";

        string silenceStart =
            $"[rf_silence_start_{streamKey}]";

        string silenceEnd =
            $"[rf_silence_end_{streamKey}]";

        string silenceSink =
            $"[rf_silence_sink_{streamKey}]";

        string mappedOutput =
            $"[rf_audio_{streamKey}]";

        var graph =
            new StringBuilder();

        graph.Append(
            $"[0:{audioStream.Index}]");
        graph.Append(
            $"asplit=2{silenceInput}{signalInput};");

        graph.Append(silenceInput);
        graph.Append(
            "ametadata=mode=delete,");
        graph.Append(
            "silencedetect=" +
            $"n={FfmpegEvidenceFilterLabels.Format(options.SilenceNoiseThresholdDb)}dB:" +
            $"d={FfmpegEvidenceFilterLabels.Format(options.MinimumSilenceDuration.TotalSeconds)},");
        graph.Append(
            $"asplit=3{silenceStart}{silenceEnd}{silenceSink};");

        AppendAudioEventBranch(
            graph,
            silenceStart,
            "lavfi.silence_start",
            FfmpegEvidenceCommandBuilder.SilenceRecordKind,
            audioStream.Index);

        graph.Append(';');

        AppendAudioEventBranch(
            graph,
            silenceEnd,
            "lavfi.silence_end",
            FfmpegEvidenceCommandBuilder.SilenceRecordKind,
            audioStream.Index);

        graph.Append(';');
        graph.Append(silenceSink);
        graph.Append("anull");
        graph.Append(mappedOutput);
        graph.Append(';');

        graph.Append(signalInput);
        graph.Append(
            $"ametadata=mode=delete,asetnsamples=n={window.SamplesPerWindow}:p=0,");
        graph.Append(
            "astats=metadata=1:reset=1:" +
            "measure_perchannel=none:" +
            "measure_overall=RMS_level+Peak_level+Number_of_samples,");
        AppendAudioAttributionAndPrint(
            graph,
            FfmpegEvidenceCommandBuilder.AudioSignalRecordKind,
            audioStream.Index);
        graph.Append(",anullsink");

        return
        [
            "-hide_banner",
            "-nostdin",
            "-nostats",
            "-v",
            "error",
            "-i",
            "{INPUT}",
            "-filter_complex",
            graph.ToString(),
            "-map",
            mappedOutput,
            "-vn",
            "-sn",
            "-dn",
            "-f",
            "null",
            "-",
        ];
    }

    internal static FfmpegAudioWindowSpecification
        CreateWindowSpecification(
            AudioStreamInfo audioStream,
            TimeSpan requestedDuration)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        MediaEvidenceAnalysisOptions
            .ValidateSignalCadence(
                requestedDuration,
                nameof(requestedDuration));

        int sampleRate =
            audioStream.SampleRate ??
            throw new ArgumentException(
                $"Audio stream {audioStream.Index} does not report a sample rate required for deterministic signal windows.",
                nameof(audioStream));

        int samplesPerWindow =
            checked(
                (int)Math.Round(
                    sampleRate *
                    requestedDuration.TotalSeconds,
                    MidpointRounding.AwayFromZero));
        samplesPerWindow =
            Math.Max(
                samplesPerWindow,
                1);

        return new FfmpegAudioWindowSpecification(
            sampleRate,
            samplesPerWindow,
            TimeSpan.FromSeconds(
                samplesPerWindow /
                (double)sampleRate));
    }

    private static void AppendAudioEventBranch(
        StringBuilder graph,
        string inputLabel,
        string selectedKey,
        string recordKind,
        int streamIndex)
    {
        graph.Append(inputLabel);
        graph.Append(
            $"ametadata=mode=select:key={selectedKey},");

        AppendAudioAttributionAndPrint(
            graph,
            recordKind,
            streamIndex);

        graph.Append(",anullsink");
    }

    private static void AppendAudioAttributionAndPrint(
        StringBuilder graph,
        string recordKind,
        int streamIndex)
    {
        graph.Append(
            $"ametadata=mode=add:key={FfmpegEvidenceCommandBuilder.RecordKindMetadataKey}:" +
            $"value={recordKind},");

        graph.Append(
            $"ametadata=mode=add:key={FfmpegEvidenceCommandBuilder.AudioStreamMetadataKey}:" +
            $"value={streamIndex},");

        graph.Append(
            "ametadata=mode=print:file=-:direct=1");
    }
}
