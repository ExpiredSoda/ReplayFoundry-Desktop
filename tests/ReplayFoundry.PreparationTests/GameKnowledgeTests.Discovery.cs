using System.Net;
using System.Text;
using ReplayFoundry.Desktop.Platform.GameKnowledge;

namespace ReplayFoundry.PreparationTests;

internal static partial class GameKnowledgeTests
{
    private static async Task InformalGameNamesUseFullTextDiscovery()
    {
        var handler = new RecordingHttpHandler(request =>
        {
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            string json = query.Contains("action=wbsearchentities", StringComparison.Ordinal)
                ? """{"search":[]} """
                : query.Contains("list=search", StringComparison.Ordinal)
                ? """{"query":{"search":[{"title":"Q999001"},{"title":"Q110055360"},{"title":"Q110055360"},{"title":"not-an-entity"}]}}"""
                : query.Contains("ids=Q110055360", StringComparison.Ordinal)
                ? """{"entities":{"Q110055360":{"lastrevid":101,"modified":"2026-01-01T00:00:00Z","labels":{"en":{"value":"Warhammer 40,000: Space Marine II"}},"descriptions":{"en":{"value":"2024 hack-n-slash video game"}},"aliases":{},"sitelinks":{},"claims":{"P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q7889"}}}}]}}}}"""
                : query.Contains("ids=Q999001", StringComparison.Ordinal)
                ? """{"entities":{"Q999001":{"lastrevid":102,"modified":"2026-01-01T00:00:00Z","labels":{"en":{"value":"Space Marines"}},"descriptions":{"en":{"value":"fictional military organization"}},"aliases":{},"sitelinks":{},"claims":{}}}}"""
                : """{"entities":{"Q7889":{"labels":{"en":{"value":"video game"}}}}}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        using var client = new HttpClient(handler);
        var provider = new WikimediaGameKnowledgeProvider(client,
            "https://example.test/wikipedia-api", "https://example.test/wikidata-api");
        var result = await provider.DiscoverAsync(DiscoveryRequest("Space Marines 40K"), CancellationToken.None);
        TestAssert.Equal(1, result.Candidates.Count, "Full-text matches must be deduplicated and verified as video games.");
        TestAssert.Equal("Q110055360", result.Candidates[0].WikidataEntityId, "The actual game identity remains selectable.");
        var fallback = handler.Requests.Single(uri => uri.Query.Contains("list=search", StringComparison.Ordinal));
        TestAssert.True(Uri.UnescapeDataString(fallback.Query).Contains("srsearch=Space Marines 40K", StringComparison.Ordinal),
            "Fallback sends only the same user-supplied game name.");
        TestAssert.True(handler.Requests.All(uri => uri.AbsolutePath == "/wikidata-api" && !uri.Query.Contains("maxlag")),
            "Interactive discovery stays within the authorized Wikidata service.");
    }
}
