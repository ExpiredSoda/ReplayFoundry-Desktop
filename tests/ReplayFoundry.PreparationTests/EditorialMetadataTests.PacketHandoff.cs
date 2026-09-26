using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static Task PacketReceiptsRequireVerifiedIdenticalPriorFacts()
    {
        const string hash = "facts";
        var receipt = new Qwen3VlGroundingPacketReceipt("request", 0, "candidate", "exact facts");
        var current = new Dictionary<string, Qwen3VlGroundingPacketReceipt>();
        TestAssert.Throws<Qwen3VlOutputParseException>(() =>
            Qwen3VlGroundedMetadataResultPolicyParser.ValidatePacketReceipt(hash, receipt, true, current),
            "A new process cannot claim packet reuse without a verified antecedent.");
        var prior = new Dictionary<string, Qwen3VlGroundingPacketReceipt> { [hash] = receipt };
        Qwen3VlGroundedMetadataResultPolicyParser.ValidatePacketReceipt(hash, receipt, true, current, prior);
        foreach (var changed in new[] { receipt with { RequestSha256 = "other" }, receipt with { SourceAttempt = 1 },
            receipt with { CandidateId = "other" }, receipt with { FactWitness = "different facts" } })
            TestAssert.Throws<Qwen3VlOutputParseException>(() =>
                Qwen3VlGroundedMetadataResultPolicyParser.ValidatePacketReceipt(hash, changed, true, current, prior),
                "Every inherited witness component must match exactly.");
        Qwen3VlGroundedMetadataResultPolicyParser.ValidatePacketReceipt(hash, receipt, false, current, prior);
        TestAssert.Throws<Qwen3VlOutputParseException>(() =>
            Qwen3VlGroundedMetadataResultPolicyParser.ValidatePacketReceipt(hash, receipt, false, current, prior),
            "The existing same-batch duplicate rebuilding check remains strict.");
        return Task.CompletedTask;
    }

    private static async Task PacketHandoffPromotesOnlyParsedBoundedPackets()
    {
        using var directory = new EditorialTestDirectory();
        using var handoff = new Qwen3VlGroundingPacketHandoff();
        string entry = PacketEntry("candidate", out string factHash, out var receipt);
        string exportPath = Path.Combine(directory.Path, Qwen3VlGroundingPacketHandoff.ExportFile);
        File.WriteAllText(exportPath, PacketEnvelope(entry));
        await handoff.RetainExportAsync(directory.Path, CancellationToken.None);
        var environment = handoff.Prepare(directory.Path, new Dictionary<string, string> { ["pinned"] = "unchanged" });
        TestAssert.False(environment.ContainsKey(Qwen3VlGroundingPacketHandoff.ImportHashFlag),
            "An export without a fully parsed receipt cannot become an import.");
        handoff.Receipts.Add(factHash, receipt);
        await handoff.RetainExportAsync(directory.Path, CancellationToken.None);
        environment = handoff.Prepare(directory.Path, new Dictionary<string, string> { ["pinned"] = "unchanged" });
        string importPath = Path.Combine(directory.Path, Qwen3VlGroundingPacketHandoff.ImportFile);
        TestAssert.Equal("unchanged", environment["pinned"], "Runtime environment entries are preserved.");
        TestAssert.Equal(Qwen3VlGroundingPacketHandoff.Hash(File.ReadAllText(importPath)),
            environment[Qwen3VlGroundingPacketHandoff.ImportHashFlag], "The exact child import bytes are sealed.");
        using var independent = new Qwen3VlGroundingPacketHandoff();
        TestAssert.False(independent.Prepare(directory.Path, new Dictionary<string, string>())
            .ContainsKey(Qwen3VlGroundingPacketHandoff.ImportHashFlag), "Another operation starts empty.");
        using var pending = handoff.Copy();
        using var rejectedDirectory = new EditorialTestDirectory();
        File.WriteAllText(Path.Combine(rejectedDirectory.Path, Qwen3VlGroundingPacketHandoff.ExportFile),
            PacketEnvelope(entry.Replace("canonicalFacts\":\"{}", "canonicalFacts\":\"{bad", StringComparison.Ordinal)));
        await pending.RetainExportAsync(rejectedDirectory.Path, CancellationToken.None);
        // The committed predecessor remains usable; a malformed new file adds nothing.
        TestAssert.Equal(1, pending.Receipts.Count, "Malformed handoff cannot create another accepted receipt.");
        pending.Prepare(rejectedDirectory.Path, new Dictionary<string, string>());
        TestAssert.Equal(File.ReadAllText(importPath), File.ReadAllText(
            Path.Combine(rejectedDirectory.Path, Qwen3VlGroundingPacketHandoff.ImportFile)),
            "A corrupt replacement cannot change the preceding sealed packet bytes.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await TestAssert.ThrowsAsync<OperationCanceledException>(() =>
            pending.RetainExportAsync(directory.Path, cancellation.Token), "Cancellation is never swallowed as a cache miss.");
        handoff.Dispose();
        TestAssert.Throws<ObjectDisposedException>(() => handoff.Copy(), "Disposed operation state cannot escape.");
    }

    private static async Task GroundedExecutorCleansHandoffAfterParseFailureAndCancellation()
    {
        using var fixture = new ModelFreeGroundedExecutorFixture();
        using Qwen3VlQualifiedEditorialRuntime runtime = fixture.Runtime;
        string? workspace = null;
        var runner = new FailureArtifactProcessRunner(process =>
        {
            workspace = process.WorkingDirectory ?? throw new InvalidOperationException("The owned workspace is required.");
            TestAssert.Equal("1", process.EnvironmentVariables[Qwen3VlGroundingPacketHandoff.EnvironmentFlag],
                "Only the scoped child opts into packet handoff.");
            File.WriteAllText(Path.Combine(workspace, Qwen3VlGroundingPacketHandoff.ExportFile), PacketEnvelope(""));
            string[] arguments = process.Arguments.ToArray();
            int output = Array.IndexOf(arguments, "--output");
            File.WriteAllText(arguments[output + 1], "{}");
            return new ProcessRunResult(0, "", "", TimeSpan.FromMilliseconds(1));
        });
        var executor = new Qwen3VlGroundedMetadataExecutor(runtime, runner,
            new SystemQwen3VlBatchWorkspaceFactory(), NullQwen3VlGroundedFailureArchive.Instance);
        using var pending = new Qwen3VlGroundingPacketHandoff();
        await TestAssert.ThrowsAsync<InvalidDataException>(() => executor.GenerateBatchAsync<int>(
            [fixture.CreateRequest()], (_, _) => throw new InvalidDataException("Strict output rejected"), pending,
            CancellationToken.None), "A successful child exit cannot promote a failed parse.");
        TestAssert.True(workspace is not null && !Directory.Exists(workspace),
            "Child imports/exports disappear with its workspace after parser failure.");
        using var cancel = new CancellationTokenSource();
        await TestAssert.ThrowsAsync<OperationCanceledException>(() => executor.GenerateBatchAsync(
            [fixture.CreateRequest()], (_, _) => { cancel.Cancel(); return 1; }, pending, cancel.Token),
            "Cancellation after parse prevents promotion.");
        TestAssert.True(workspace is not null && !Directory.Exists(workspace), "Cancelled workspace is cleaned.");
        TestAssert.Equal(0, pending.Receipts.Count, "Rejected execution never acquires packet receipts.");
    }

    private static async Task EditorialRetrySessionsAreOperationScoped()
    {
        var provider = new SessionFixtureProvider();
        var service = new ClipEditorialMetadataGenerationService(new RecordingFallbackMetadataGenerator(), provider);
        await service.GenerateBatchAsync([DiagnosticRequest("first")], CancellationToken.None);
        await service.GenerateBatchAsync([DiagnosticRequest("second")], CancellationToken.None);
        TestAssert.Equal(2, provider.Sessions.Count, "Each user operation receives its own session.");
        TestAssert.True(provider.Sessions.All(static session => session.Disposed && session.Calls == 2),
            "A session spans both actual retry calls and is disposed exactly at operation completion.");
        using var cancelled = new CancellationTokenSource();
        provider.Cancel = cancelled;
        await TestAssert.ThrowsAsync<OperationCanceledException>(() => service.GenerateBatchAsync(
            [DiagnosticRequest("cancel")], cancelled.Token), "A cancelled operation still releases its scope.");
        TestAssert.True(provider.Sessions[^1].Disposed, "Cancellation disposes the opened provider session.");
    }

    private static async Task GroundedSessionRejectsConcurrentUseAndCleansCancellation()
    {
        using var fixture = new ModelFreeGroundedExecutorFixture();
        var runner = new WaitingPacketProcessRunner();
        using var generator = new Qwen3VlGroundedMetadataGenerator(fixture.Runtime, runner,
            new SystemQwen3VlBatchWorkspaceFactory());
        using IClipEditorialMetadataBatchSession session = generator.CreateBatchSession();
        using var cancellation = new CancellationTokenSource();
        Task<ClipEditorialMetadataDraft> first = session.GenerateAsync(fixture.CreateRequest(), cancellation.Token);
        try
        {
            await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await TestAssert.ThrowsAsync<InvalidOperationException>(() => session.GenerateAsync(
                fixture.CreateRequest(), CancellationToken.None), "A session cannot mix concurrent retry chains.");
            session.Dispose();
        }
        finally { cancellation.Cancel(); }
        await TestAssert.ThrowsAsync<OperationCanceledException>(() => first,
            "Cancellation preserves its original provider failure and releases admission.");
        TestAssert.True(runner.Workspace is not null && !Directory.Exists(runner.Workspace),
            "Even a disposed active session retains ownership until its child workspace is cleaned.");
        await TestAssert.ThrowsAsync<ObjectDisposedException>(() => session.GenerateAsync(
            fixture.CreateRequest(), CancellationToken.None), "Disposed sessions cannot restart.");
        using IClipEditorialMetadataBatchSession independent = generator.CreateBatchSession();
        TestAssert.True(independent.IsAvailable, "Disposing operation state must not dispose the shared runtime.");
    }

    private static string PacketEntry(string candidate, out string factHash, out Qwen3VlGroundingPacketReceipt receipt)
    {
        string identity = JsonSerializer.Serialize(new { candidateId = candidate });
        string requestHash = Qwen3VlGroundingPacketHandoff.Hash(identity);
        string schema = Qwen3VlGroundedMetadataGenerator.GroundingPacketSchemaVersion;
        factHash = Qwen3VlGroundingPacketHandoff.Hash("{\"facts\":{},\"requestIdentitySha256\":\"" + requestHash + "\",\"schemaVersion\":\"" + schema + "\"}");
        receipt = new(requestHash, 0, candidate, "validated witness");
        return JsonSerializer.Serialize(new { schemaVersion = schema, requestIdentitySha256 = requestHash,
            canonicalRequestIdentity = identity, factSha256 = factHash, canonicalFacts = "{}", sourceAttempt = 0,
            groundingPassCount = 5, groundingElapsedSeconds = 123.25, runtimeIdentitySha256 = new string('b', 64),
            editorialBriefSha256 = new string('c', 64) });
    }

    private static string PacketEnvelope(string entries) =>
        "{\"schemaVersion\":\"" + Qwen3VlGroundingPacketHandoff.Schema + "\",\"entries\":[" + entries + "]}";

    private sealed class SessionFixtureProvider : IClipEditorialMetadataBatchGenerator, IClipEditorialMetadataBatchSessionFactory
    {
        public List<SessionFixture> Sessions { get; } = [];
        public CancellationTokenSource? Cancel { get; set; }
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } = new("Retry diagnostics fixture", "1.0");
        public bool IsAvailable => true;
        public IClipEditorialMetadataBatchSession CreateBatchSession()
        {
            var session = new SessionFixture(Cancel);
            Sessions.Add(session);
            return session;
        }
        public Task<ClipEditorialMetadataDraft> GenerateAsync(ClipEditorialMetadataRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An operation must enter its session.");
        public Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An operation must enter its session.");
    }

    private sealed class SessionFixture(CancellationTokenSource? cancel) : IClipEditorialMetadataBatchSession
    {
        private readonly ReconciledProvenanceMetadataGenerator _inner = new("UnstableReadableTextReuse");
        public bool Disposed { get; private set; }
        public int Calls { get; private set; }
        public ClipEditorialMetadataGeneratorIdentity Identity => _inner.Identity;
        public bool IsAvailable => !Disposed;
        public async Task<ClipEditorialMetadataDraft> GenerateAsync(ClipEditorialMetadataRequest request, CancellationToken cancellationToken) =>
            (await GenerateBatchAsync([request], cancellationToken))[0];
        public async Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken)
        {
            TestAssert.False(Disposed, "Provider session cannot be disposed between retries.");
            Calls++;
            cancel?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return await _inner.GenerateBatchAsync(requests, cancellationToken);
        }
        public async Task<IReadOnlyList<ClipEditorialMetadataBatchOutcome>> GenerateBatchOutcomesAsync(IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken) =>
            (await GenerateBatchAsync(requests, cancellationToken)).Select(static draft => new ClipEditorialMetadataBatchOutcome(draft, null)).ToArray();
        public void Dispose() => Disposed = true;
    }

    private sealed class WaitingPacketProcessRunner : IProcessRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Workspace { get; private set; }
        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            Workspace = request.WorkingDirectory;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The fixture must be cancelled.");
        }
    }
}
