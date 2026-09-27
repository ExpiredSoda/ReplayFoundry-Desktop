using System.Text.Json.Nodes;

namespace ReplayFoundry.Desktop.Platform.Research;

internal static class SharedTrainingEvidence
{
    internal static JsonObject? Copy(JsonNode? value) => value is null ? null : new()
    {
        ["titleBody"] = value["titleBody"]?.DeepClone(), ["description"] = value["description"]?.DeepClone(),
        ["tags"] = value["tags"]?.DeepClone(),
    };
    internal static JsonObject Project(JsonObject captured)
    {
        var context = JsonNode.Parse(captured["prompt"]![1]!["content"]!.GetValue<string>());
        var result = new JsonObject { ["authority"] = "ModelEvidenceNotHumanGroundTruth" };
        foreach (string key in new[] { "setupObservation", "outcomeObservation" })
            if (context?[key] is JsonValue text) result[key] = text.GetValue<string>();
        var reviewed = context?["reviewedContext"];
        bool complete = true;
        foreach (string kind in new[] { "speech", "sourceText" })
        {
            var rows = new JsonArray();
            if (reviewed?[kind] is JsonArray source)
            {
                complete &= source.Count <= 64;
                foreach (JsonNode? item in source.Take(64))
                {
                    var row = new JsonObject();
                    foreach (string key in new[] { "text", "start", "end", "role", "roleSource", "claim" })
                        if (item?[key] is JsonValue value) row[key] = value.DeepClone();
                    rows.Add(row);
                }
            }
            result[kind] = rows;
        }
        result["complete"] = complete;
        return result;
    }
}
