using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonStudioProjectStore : IStudioProjectStore
{
    private const string FileName = "studio-project.json";
    private const string BackupFileName = "studio-project.json.bak";
    private const long MaximumDocumentBytes = 64L * 1024 * 1024;
    private const int MaximumLegacyClaims = 4;
    private readonly string _root;
    private readonly JsonSerializerOptions _options;

    public JsonStudioProjectStore(string? root = null)
    {
        _root = ReplayFoundryLocalDataPaths.Resolve(
            root,
            Path.Combine("Projects", "placeholder"));
        if (root is null)
        {
            _root = Path.GetDirectoryName(_root)!;
        }
        else
        {
            _root = Path.GetFullPath(root);
        }
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            MaxDepth = 128,
        };
        _options.Converters.Add(new JsonStringEnumConverter());
    }

    public string Root => _root;

    public void Save(
        GenerationOutputProject project,
        long revision,
        StudioProjectRecoveryState? recovery = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        string directory = ResolveProjectDirectory(project.Id);
        Directory.CreateDirectory(directory);
        string contentId = Path.GetFileName(directory);
        using var saveMutex = new Mutex(
            initiallyOwned: false,
            "Local\\ReplayFoundry.StudioProject." + contentId);
        bool ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = saveMutex.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }
            if (!ownsMutex)
            {
                throw new IOException(
                    "Another Replay Foundry process is still saving this Studio project.");
            }

            SaveExclusive(project, revision, recovery, directory);
        }
        finally
        {
            if (ownsMutex)
            {
                saveMutex.ReleaseMutex();
            }
        }
    }

    private void SaveExclusive(
        GenerationOutputProject project,
        long revision,
        StudioProjectRecoveryState? recovery,
        string directory)
    {
        string target = Path.Combine(directory, FileName);
        string backup = Path.Combine(directory, BackupFileName);
        CleanupAbandonedStaging(directory);
        string temporary = Path.Combine(
            directory,
            FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            StudioProjectDocument? existing = null;
            if (File.Exists(target))
            {
                try
                {
                    existing = ReadSupportedDocument(target);
                }
                catch (UnsupportedStudioProjectSchemaException exception)
                {
                    throw new InvalidOperationException(
                        "Replay Foundry will not overwrite a Studio project from a newer schema.",
                        exception);
                }
                catch (Exception exception) when (IsInvalidDocument(exception))
                {
                    // A new valid save may repair a corrupt primary while the
                    // atomic replacement retains it as diagnostic history.
                }
            }
            if (existing is not null && existing.Revision >= revision)
            {
                throw new InvalidOperationException(
                    "A Studio project save cannot replace the same or a newer revision.");
            }

            StudioProjectDocument document =
                StudioProjectDocumentMapper.Capture(
                    project,
                    revision,
                    DateTimeOffset.UtcNow,
                    recovery);
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                document,
                _options);
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 64 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            StudioProjectDocument staged = ReadSupportedDocument(temporary);
            _ = StudioProjectDocumentMapper.Restore(staged);
            if (File.Exists(target))
            {
                File.Replace(
                    temporary,
                    target,
                    backup,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, target);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public StudioProjectLoadResult Load(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        string directory = ResolveProjectDirectory(projectId);
        string target = Path.Combine(directory, FileName);
        string backup = Path.Combine(directory, BackupFileName);
        if (!File.Exists(target))
        {
            return new StudioProjectLoadResult(
                StudioProjectLoadOutcome.NotFound,
                "No durable Studio project was found.");
        }

        try
        {
            StudioProjectDocument document = ReadSupportedDocument(target);
            return ValidateSources(document, recovered: false);
        }
        catch (UnsupportedStudioProjectSchemaException exception)
        {
            return new StudioProjectLoadResult(
                StudioProjectLoadOutcome.UnsupportedSchema,
                exception.Message);
        }
        catch (Exception exception) when (IsInvalidDocument(exception))
        {
            if (File.Exists(backup))
            {
                try
                {
                    StudioProjectDocument recovered =
                        ReadSupportedDocument(backup);
                    return ValidateSources(recovered, recovered: true);
                }
                catch (UnsupportedStudioProjectSchemaException backupException)
                {
                    return new StudioProjectLoadResult(
                        StudioProjectLoadOutcome.UnsupportedSchema,
                        backupException.Message);
                }
                catch (Exception backupException)
                    when (IsInvalidDocument(backupException))
                {
                }
            }

            return new StudioProjectLoadResult(
                StudioProjectLoadOutcome.Corrupt,
                "The Studio project and its previous-save backup are unreadable. The files were preserved for diagnosis.");
        }
    }

    public bool Exists(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return false;
        }
        return File.Exists(Path.Combine(
            ResolveProjectDirectory(projectId),
            FileName));
    }

    public void Delete(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        string directory = ResolveProjectDirectory(projectId);
        if (!Directory.Exists(directory))
        {
            return;
        }
        Directory.Delete(directory, recursive: true);
    }

    public string ResolveProjectDirectory(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        string hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(projectId.Trim()))).ToLowerInvariant();
        string path = Path.GetFullPath(Path.Combine(_root, hash));
        string normalizedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(_root)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The Studio project identity resolved outside its local store.");
        }
        return path;
    }

    private StudioProjectLoadResult ValidateSources(
        StudioProjectDocument document,
        bool recovered)
    {
        GenerationOutputProject project =
            StudioProjectDocumentMapper.Restore(document);
        string[] missing = document.Sources
            .Where(static source => !File.Exists(source.FullPath))
            .Select(static source => source.FullPath)
            .ToArray();
        if (missing.Length > 0)
        {
            return new StudioProjectLoadResult(
                StudioProjectLoadOutcome.MissingSource,
                "The Studio project was preserved, but one or more source files are missing.",
                project,
                document,
                missing);
        }

        string[] changed = document.Sources.Where(source =>
        {
            var info = new FileInfo(source.FullPath);
            return info.Length != source.Length ||
                   new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) !=
                   source.LastWriteUtc;
        }).Select(static source => source.FullPath).ToArray();
        if (changed.Length > 0)
        {
            return new StudioProjectLoadResult(
                StudioProjectLoadOutcome.ChangedSource,
                "The Studio project was preserved, but one or more source files changed after generation.",
                project,
                document,
                changed);
        }

        return new StudioProjectLoadResult(
            recovered
                ? StudioProjectLoadOutcome.RecoveredPreviousSave
                : StudioProjectLoadOutcome.Loaded,
            recovered
                ? "Studio recovered the last valid previous save."
                : "Studio loaded the durable project without rerunning analysis.",
            project,
            document);
    }

    private StudioProjectDocument ReadSupportedDocument(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > MaximumDocumentBytes)
        {
            throw new InvalidDataException(
                "The Studio project exceeds the supported local document boundary.");
        }
        byte[] payload = File.ReadAllBytes(path);
        using JsonDocument envelope = JsonDocument.Parse(
            payload,
            new JsonDocumentOptions
            {
                MaxDepth = 128,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        if (!envelope.RootElement.TryGetProperty(
                "schemaVersion",
                out JsonElement schema) ||
            schema.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "The Studio project has no schema identity.");
        }
        string? value = schema.GetString();
        if (value is not StudioProjectDocument.CurrentSchemaVersion and
                not StudioProjectDocument.LegacySchemaVersion and
                not StudioProjectDocument.PreviousSchemaVersion)
        {
            throw new UnsupportedStudioProjectSchemaException(value);
        }

        string serialized = value == StudioProjectDocument.CurrentSchemaVersion
            ? envelope.RootElement.GetRawText()
            : MigrateLegacyDocument(envelope.RootElement);
        StudioProjectDocument? document = JsonSerializer.Deserialize<
            StudioProjectDocument>(
            serialized,
            _options);
        return document ?? throw new InvalidDataException(
            "The Studio project document was empty.");
    }

    private string MigrateLegacyDocument(JsonElement rootElement)
    {
        JsonObject root = JsonNode.Parse(
            rootElement.GetRawText(),
            documentOptions: new JsonDocumentOptions
            {
                MaxDepth = 128,
                CommentHandling = JsonCommentHandling.Disallow,
            })?.AsObject() ?? throw new InvalidDataException(
                "The legacy Studio project document was empty.");
        MigrateEditorialContexts(root["assets"] as JsonArray);
        MigrateEditorialContexts(root["hiddenMoments"] as JsonArray);
        MigrateHiddenMomentEditorialPreferences(
            root["hiddenMoments"] as JsonArray);
        root["schemaVersion"] = StudioProjectDocument.CurrentSchemaVersion;
        return root.ToJsonString(_options);
    }

    private static void MigrateHiddenMomentEditorialPreferences(
        JsonArray? hiddenMoments)
    {
        foreach (JsonNode? hiddenMoment in hiddenMoments ?? [])
        {
            if (hiddenMoment is JsonObject candidate)
            {
                // Studio schemas 1.0 and 1.1 inferred this value from scan
                // depth; it was not an explicit choice to disable AI.
                candidate["editorialPreference"] = nameof(
                    ClipEditorialGenerationPreference.AiRequired);
            }
        }
    }

    private static void MigrateEditorialContexts(JsonArray? candidates)
    {
        foreach (JsonNode? candidate in candidates ?? [])
        {
            if (candidate?["editorialContext"] is not JsonObject context)
            {
                continue;
            }
            JsonObject? legacy = context["gameKnowledge"] as JsonObject;
            if (context["selectedGameKnowledge"] is null && legacy is not null)
            {
                context["selectedGameKnowledge"] =
                    ProjectLegacyGameKnowledge(legacy);
            }
            context.Remove("gameKnowledge");
        }
    }

    private static JsonObject? ProjectLegacyGameKnowledge(JsonObject legacy)
    {
        if (legacy["snapshot"] is not JsonObject snapshot ||
            legacy["matches"] is not JsonArray legacyMatches)
        {
            return null;
        }
        string gameName = RequiredString(legacy, "gameName", 120);
        JsonObject provider = RequiredObject(snapshot, "provider");
        string providerName = RequiredString(provider, "name", 120);
        string providerVersion = RequiredString(provider, "version", 40);
        string retrievedAtUtc = RequiredString(
            snapshot,
            "retrievedAtUtc",
            80);
        JsonArray snapshotSources = RequiredArray(snapshot, "sources");
        JsonArray snapshotPassages = RequiredArray(snapshot, "passages");
        if (snapshotSources.Count > GameKnowledgeSnapshot.MaximumSources ||
            snapshotPassages.Count > GameKnowledgeSnapshot.MaximumPassages ||
            legacyMatches.Count > GameKnowledgeSnapshot.MaximumPassages)
        {
            throw new InvalidDataException(
                "Legacy game context exceeds its bounded collection limits.");
        }
        var sourcesById = snapshotSources
            .OfType<JsonObject>()
            .ToDictionary(
                source => RequiredString(source, "id", 160),
                StringComparer.Ordinal);
        var retainedSourceIds = new HashSet<string>(StringComparer.Ordinal);
        var passageIds = new HashSet<string>(StringComparer.Ordinal);
        var passages = new JsonArray();
        var matches = new JsonArray();

        foreach (JsonObject match in legacyMatches
                     .OfType<JsonObject>()
                     .Take(MaximumLegacyClaims))
        {
            JsonObject passage = RequiredObject(match, "passage");
            string passageId = RequiredString(passage, "id", 160);
            string sourceId = RequiredString(passage, "sourceId", 160);
            if (!passageIds.Add(passageId))
            {
                continue;
            }
            _ = RequiredString(passage, "section", 160);
            _ = RequiredString(
                passage,
                "text",
                GameKnowledgePassage.MaximumTextLength);
            _ = RequiredString(passage, "contentSha256", 64);
            if (!sourcesById.ContainsKey(sourceId))
            {
                throw new InvalidDataException(
                    "A legacy selected excerpt has no attributed source.");
            }

            retainedSourceIds.Add(sourceId);
            passages.Add(new JsonObject
            {
                ["id"] = passageId,
                ["sourceId"] = sourceId,
                ["section"] = passage["section"]!.DeepClone(),
                ["text"] = passage["text"]!.DeepClone(),
                ["contentSha256"] = passage["contentSha256"]!.DeepClone(),
            });
            matches.Add(ProjectLegacyMatch(match, passageId));
        }
        if (matches.Count == 0)
        {
            return null;
        }

        var sources = new JsonArray();
        foreach (string sourceId in retainedSourceIds)
        {
            sources.Add(ProjectLegacySource(sourcesById[sourceId]));
        }
        return new JsonObject
        {
            ["gameName"] = gameName,
            ["providerName"] = providerName,
            ["providerVersion"] = providerVersion,
            ["retrievedAtUtc"] = retrievedAtUtc,
            ["sources"] = sources,
            ["passages"] = passages,
            ["matches"] = matches,
        };
    }

    private static JsonObject ProjectLegacySource(JsonObject source)
    {
        _ = RequiredString(source, "id", 160);
        _ = RequiredString(source, "kind", 80);
        if (source["role"] is not null)
        {
            _ = RequiredString(source, "role", 80);
        }
        _ = RequiredString(source, "title", 240);
        _ = RequiredString(source, "pageUri", 2_048);
        _ = RequiredString(source, "revisionId", 120);
        _ = RequiredString(source, "revisionTimestampUtc", 80);
        _ = RequiredString(source, "licenseIdentifier", 80);
        _ = RequiredString(source, "licenseUri", 2_048);
        _ = RequiredString(source, "attribution", 500);
        _ = RequiredString(source, "contentSha256", 64);
        return new JsonObject
        {
            ["id"] = source["id"]!.DeepClone(),
            ["kind"] = source["kind"]!.DeepClone(),
            ["role"] = source["role"]?.DeepClone() ?? "PrimaryArticle",
            ["title"] = source["title"]!.DeepClone(),
            ["pageUri"] = source["pageUri"]!.DeepClone(),
            ["revisionId"] = source["revisionId"]!.DeepClone(),
            ["revisionTimestampUtc"] =
                source["revisionTimestampUtc"]!.DeepClone(),
            ["licenseIdentifier"] =
                source["licenseIdentifier"]!.DeepClone(),
            ["licenseUri"] = source["licenseUri"]!.DeepClone(),
            ["attribution"] = source["attribution"]!.DeepClone(),
            ["contentSha256"] = source["contentSha256"]!.DeepClone(),
        };
    }

    private static JsonObject ProjectLegacyMatch(
        JsonObject match,
        string passageId)
    {
        string strength = RequiredString(match, "strength", 80);
        string temporal = match["temporalRelation"] is null
            ? "Unspecified"
            : RequiredString(match, "temporalRelation", 80);
        double relevance = match["relevance"]?.GetValue<double>() ??
            throw new InvalidDataException(
                "A legacy game-context match has no relevance value.");
        return new JsonObject
        {
            ["passageId"] = passageId,
            ["strength"] = strength,
            ["temporalRelation"] = temporal,
            ["relevance"] = relevance,
            ["matchedTerms"] = ProjectStringArray(
                match["matchedTerms"] as JsonArray,
                24,
                80),
            ["clipEvidenceIds"] = ProjectStringArray(
                match["clipEvidenceIds"] as JsonArray,
                24,
                160),
        };
    }

    private static JsonArray ProjectStringArray(
        JsonArray? values,
        int maximumCount,
        int maximumLength)
    {
        var result = new JsonArray();
        foreach (JsonNode? value in (values ?? []).Take(maximumCount))
        {
            string text = value?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength)
            {
                throw new InvalidDataException(
                    "A legacy game-context evidence value exceeded its boundary.");
            }
            result.Add(text);
        }
        return result;
    }

    private static JsonObject RequiredObject(
        JsonObject parent,
        string propertyName) =>
        parent[propertyName] as JsonObject ?? throw new InvalidDataException(
            $"Legacy game context has no {propertyName} object.");

    private static JsonArray RequiredArray(
        JsonObject parent,
        string propertyName) =>
        parent[propertyName] as JsonArray ?? throw new InvalidDataException(
            $"Legacy game context has no {propertyName} list.");

    private static string RequiredString(
        JsonObject parent,
        string propertyName,
        int maximumLength) =>
        OptionalString(parent, propertyName, maximumLength) ??
        throw new InvalidDataException(
            $"Legacy game context has no valid {propertyName} value.");

    private static string? OptionalString(
        JsonObject parent,
        string propertyName,
        int maximumLength)
    {
        string? value = parent[propertyName]?.GetValue<string>();
        return !string.IsNullOrWhiteSpace(value) &&
               value.Length <= maximumLength
            ? value
            : null;
    }

    private static void CleanupAbandonedStaging(string directory)
    {
        foreach (string path in Directory.EnumerateFiles(
                     directory,
                     FileName + ".*.tmp",
                     SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }
    }

    private static bool IsInvalidDocument(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidDataException or ArgumentException or
            InvalidOperationException;

    private sealed class UnsupportedStudioProjectSchemaException :
        Exception
    {
        public UnsupportedStudioProjectSchemaException(string? value)
            : base(
                "The Studio project uses an unsupported schema" +
                (string.IsNullOrWhiteSpace(value)
                    ? "."
                    : $" ({value}).") +
                " Replay Foundry left it unchanged.")
        {
        }
    }
}
