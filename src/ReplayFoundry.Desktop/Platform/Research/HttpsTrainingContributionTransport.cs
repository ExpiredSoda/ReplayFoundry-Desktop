using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.Research;

internal static class HttpsTrainingContributionTransport
{
    internal const string Endpoint = "https://replayfoundry.com/api/v1/training-contributions";
    internal static async Task SendAsync(string token, string json, bool delete, CancellationToken cancellationToken)
    {
        if (token.Length != 64 || Encoding.UTF8.GetByteCount(json) > 65_536) throw new ArgumentException("Contribution exceeds its contract.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 4096 };
        using var request = new HttpRequestMessage(delete ? HttpMethod.Delete : HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!delete) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!receipt.RootElement.TryGetProperty(delete ? "deleted" : "accepted", out var accepted) || accepted.ValueKind != JsonValueKind.True)
            throw new HttpRequestException("The service did not confirm the contribution operation.");
    }
}
