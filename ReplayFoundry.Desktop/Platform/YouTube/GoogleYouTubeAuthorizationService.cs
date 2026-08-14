using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Platform.YouTube;

internal interface IYouTubeAuthorizationService
{
    Task<YouTubeAccessCredential?> GetAccessCredentialAsync(
        bool forceRefresh,
        CancellationToken cancellationToken);

    Task<YouTubeAccessCredential> ConnectAsync(
        CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}

internal sealed class GoogleYouTubeAuthorizationService :
    IYouTubeAuthorizationService,
    IDisposable
{
    private static readonly Uri AuthorizationEndpoint =
        new("https://accounts.google.com/o/oauth2/v2/auth");
    private static readonly Uri TokenEndpoint =
        new("https://oauth2.googleapis.com/token");
    private static readonly Uri RevokeEndpoint =
        new("https://oauth2.googleapis.com/revoke");
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly YouTubeOAuthClientConfiguration _configuration;
    private readonly IYouTubeCredentialStore _credentialStore;
    private readonly ISystemBrowser _browser;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private YouTubeAccessCredential? _active;
    private bool _disposed;

    public GoogleYouTubeAuthorizationService(
        YouTubeOAuthClientConfiguration configuration,
        IYouTubeCredentialStore credentialStore,
        ISystemBrowser browser,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(httpClient);
        _configuration = configuration;
        _credentialStore = credentialStore;
        _browser = browser;
        _httpClient = httpClient;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    public async Task<YouTubeAccessCredential?> GetAccessCredentialAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh &&
                _active is not null &&
                _active.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return _active;
            }
            YouTubeStoredCredential? stored = _credentialStore.Read();
            if (stored is null)
            {
                _active = null;
                return null;
            }
            _active = await RefreshAsync(
                    stored,
                    cancellationToken)
                .ConfigureAwait(false);
            return _active;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<YouTubeAccessCredential> ConnectAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(backlog: 1);
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = new Uri(
                $"http://127.0.0.1:{port}/oauth2/callback");
            string state = CreateRandomBase64Url(32);
            string verifier = CreateRandomBase64Url(64);
            string challenge = Base64Url(
                SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            Uri authorizationUri = BuildAuthorizationUri(
                _configuration,
                redirectUri,
                state,
                challenge);

            _browser.Open(authorizationUri);
            OAuthCallback callback;
            try
            {
                using var timeout = CancellationTokenSource
                    .CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                try
                {
                    callback = await ReceiveCallbackAsync(
                            listener,
                            timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    throw new YouTubePublishingException(
                        "YouTube connection timed out. Choose Connect when you are ready to finish in the browser.",
                        "youtube.oauth.callback-timeout");
                }
            }
            finally
            {
                listener.Stop();
            }
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(state),
                    Encoding.UTF8.GetBytes(callback.State ?? string.Empty)))
            {
                throw new YouTubePublishingException(
                    "Replay Foundry rejected an invalid YouTube sign-in response.",
                    "youtube.oauth.state-mismatch");
            }
            if (!string.IsNullOrWhiteSpace(callback.Error))
            {
                throw new YouTubePublishingException(
                    callback.Error.Equals(
                        "access_denied",
                        StringComparison.Ordinal)
                        ? "YouTube connection was cancelled in the browser."
                        : "Google could not authorize YouTube publishing.",
                    "youtube.oauth.authorization-denied",
                    callback.ErrorDescription ?? callback.Error);
            }
            if (string.IsNullOrWhiteSpace(callback.Code))
            {
                throw new YouTubePublishingException(
                    "Google returned no authorization code.",
                    "youtube.oauth.missing-code");
            }

            YouTubeAccessCredential credential = await ExchangeCodeAsync(
                    callback.Code,
                    verifier,
                    redirectUri,
                    cancellationToken)
                .ConfigureAwait(false);
            credential = credential with
            {
                Scopes = ResolveGrantedScopes(
                    credential.Scopes,
                    _configuration.Scopes),
            };
            if (string.IsNullOrWhiteSpace(credential.RefreshToken))
            {
                throw new YouTubePublishingException(
                    "Google did not return offline access. Remove Replay Foundry from your Google account permissions, then connect again.",
                    "youtube.oauth.missing-refresh-token");
            }
            _credentialStore.Write(new YouTubeStoredCredential(
                credential.RefreshToken,
                DateTimeOffset.UtcNow,
                credential.Scopes));
            _active = credential;
            return credential;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            YouTubeStoredCredential? stored = _credentialStore.Read();
            try
            {
                if (stored is not null)
                {
                    using var content = new FormUrlEncodedContent(
                        new Dictionary<string, string>
                        {
                            ["token"] = stored.RefreshToken,
                        });
                    using HttpResponseMessage response = await _httpClient
                        .PostAsync(
                            RevokeEndpoint,
                            content,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode &&
                        response.StatusCode != HttpStatusCode.BadRequest)
                    {
                        throw await CreateOAuthFailureAsync(
                                response,
                                "youtube.oauth.revoke-failed",
                                "Google could not revoke the YouTube connection.",
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _credentialStore.Delete();
                _active = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static Uri BuildAuthorizationUri(
        YouTubeOAuthClientConfiguration configuration,
        Uri redirectUri,
        string state,
        string codeChallenge)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(redirectUri);
        if (!redirectUri.IsLoopback ||
            redirectUri.Scheme != Uri.UriSchemeHttp ||
            string.IsNullOrWhiteSpace(state) ||
            string.IsNullOrWhiteSpace(codeChallenge))
        {
            throw new ArgumentException(
                "Google desktop authorization requires a loopback redirect, state, and PKCE challenge.");
        }
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = configuration.ClientId,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', configuration.Scopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent select_account",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
        };
        string query = string.Join(
            '&',
            parameters.Select(pair =>
                Uri.EscapeDataString(pair.Key) + "=" +
                Uri.EscapeDataString(pair.Value)));
        return new UriBuilder(AuthorizationEndpoint)
        {
            Query = query,
        }.Uri;
    }

    private async Task<YouTubeAccessCredential> ExchangeCodeAsync(
        string code,
        string verifier,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = _configuration.ClientId,
                ["client_secret"] = _configuration.ClientSecret,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri.AbsoluteUri,
            });
        using HttpResponseMessage response = await _httpClient.PostAsync(
                TokenEndpoint,
                content,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateOAuthFailureAsync(
                    response,
                    "youtube.oauth.token-exchange-failed",
                    "Google could not finish connecting YouTube.",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        return await ReadTokenAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<YouTubeAccessCredential> RefreshAsync(
        YouTubeStoredCredential stored,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = _configuration.ClientId,
                ["client_secret"] = _configuration.ClientSecret,
                ["refresh_token"] = stored.RefreshToken,
                ["grant_type"] = "refresh_token",
            });
        using HttpResponseMessage response = await _httpClient.PostAsync(
                TokenEndpoint,
                content,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            if (body.Contains("invalid_grant", StringComparison.Ordinal))
            {
                _credentialStore.Delete();
                _active = null;
                throw new YouTubePublishingException(
                    "Your YouTube connection expired or was revoked. Connect again to continue.",
                    "youtube.oauth.reauthorization-required",
                    body);
            }
            throw new YouTubePublishingException(
                "Replay Foundry could not refresh the YouTube connection.",
                "youtube.oauth.refresh-failed",
                body);
        }
        YouTubeAccessCredential access = await ReadTokenAsync(
                response,
                cancellationToken)
            .ConfigureAwait(false);
        return access with
        {
            RefreshToken = stored.RefreshToken,
            Scopes = stored.Scopes,
        };
    }

    private static async Task<YouTubeAccessCredential> ReadTokenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        TokenResponse? payload = await JsonSerializer.DeserializeAsync<TokenResponse>(
                stream,
                JsonReadOptions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.AccessToken) ||
            payload.ExpiresIn <= 0)
        {
            throw new YouTubePublishingException(
                "Google returned an incomplete OAuth token response.",
                "youtube.oauth.invalid-token-response");
        }
        string[] scopes = string.IsNullOrWhiteSpace(payload.Scope)
            ? []
            : payload.Scope.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        return new YouTubeAccessCredential(
            payload.AccessToken,
            DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn),
            payload.RefreshToken,
            Array.AsReadOnly(scopes));
    }

    private static async Task<OAuthCallback> ReceiveCallbackAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(
                cancellationToken)
            .ConfigureAwait(false);
        await using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4_096,
            leaveOpen: true);
        string? requestLine = await reader.ReadLineAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine) ||
            !requestLine.StartsWith("GET ", StringComparison.Ordinal) ||
            requestLine.Length > 8_192)
        {
            throw new YouTubePublishingException(
                "Replay Foundry received an invalid local OAuth response.",
                "youtube.oauth.invalid-loopback-request");
        }
        string target = requestLine.Split(' ', 3)[1];
        var callbackUri = new Uri("http://127.0.0.1" + target);
        if (!callbackUri.AbsolutePath.Equals(
                "/oauth2/callback",
                StringComparison.Ordinal))
        {
            throw new YouTubePublishingException(
                "Replay Foundry received an OAuth response on an unexpected local path.",
                "youtube.oauth.invalid-loopback-path");
        }
        Dictionary<string, string> query = ParseQuery(callbackUri.Query);
        bool success = query.ContainsKey("code") &&
                       !query.ContainsKey("error");
        string body = BuildLoopbackResponseHtml(success);
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(
                Encoding.ASCII.GetBytes(headers),
                cancellationToken)
            .ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken)
            .ConfigureAwait(false);
        return new OAuthCallback(
            query.GetValueOrDefault("code"),
            query.GetValueOrDefault("state"),
            query.GetValueOrDefault("error"),
            query.GetValueOrDefault("error_description"));
    }

    internal static IReadOnlyList<string> ResolveGrantedScopes(
        IReadOnlyList<string> responseScopes,
        IReadOnlyList<string> requestedScopes)
    {
        ArgumentNullException.ThrowIfNull(responseScopes);
        ArgumentNullException.ThrowIfNull(requestedScopes);
        IReadOnlyList<string> source = responseScopes.Count == 0
            ? requestedScopes
            : responseScopes;
        if (source.Count == 0 || source.Any(string.IsNullOrWhiteSpace))
        {
            throw new YouTubePublishingException(
                "Google returned no usable YouTube permission scope.",
                "youtube.oauth.missing-scope");
        }
        return Array.AsReadOnly(source.ToArray());
    }

    internal static string BuildLoopbackResponseHtml(bool success)
    {
        string title = success
            ? "YouTube connected · ReplayFoundry"
            : "Connection stopped · ReplayFoundry";
        string eyebrow = success ? "CONNECTION APPROVED" : "CONNECTION STOPPED";
        string heading = success
            ? "YouTube is connected"
            : "Nothing was connected";
        string message = success
            ? "Your approval reached ReplayFoundry. The app is securely finishing the connection now."
            : "No YouTube permission was saved. ReplayFoundry has the details if you want to try again.";
        string accent = success ? "#4de7d3" : "#ffba66";
        string stateMark = success ? "&#10003;" : "&#8212;";
        string stateLabel = success
            ? "Authorization received"
            : "No access granted";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <meta name="color-scheme" content="dark">
              <title>{{title}}</title>
              <style>
                :root { color-scheme: dark; font-family: Inter, ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif; }
                * { box-sizing: border-box; }
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 32px; color: #edf6ff; background: radial-gradient(circle at 18% 18%, rgba(42, 191, 210, .18), transparent 34%), radial-gradient(circle at 85% 84%, rgba(255, 178, 84, .13), transparent 32%), #06111f; }
                .card { width: min(680px, 100%); overflow: hidden; border: 1px solid rgba(138, 181, 205, .24); border-radius: 28px; background: linear-gradient(145deg, rgba(14, 34, 53, .98), rgba(7, 20, 34, .98)); box-shadow: 0 28px 90px rgba(0, 0, 0, .48); }
                .brand { display: flex; align-items: center; gap: 14px; padding: 24px 28px; border-bottom: 1px solid rgba(138, 181, 205, .14); color: #cfe8f4; font-weight: 750; letter-spacing: .02em; }
                .mark { width: 40px; height: 40px; flex: 0 0 40px; border-radius: 8px; box-shadow: 0 0 28px rgba(21, 153, 200, .26); }
                main { padding: 46px 46px 40px; }
                .eyebrow { margin: 0 0 12px; color: {{accent}}; font-size: 12px; font-weight: 850; letter-spacing: .18em; }
                h1 { margin: 0; font-size: clamp(34px, 6vw, 54px); line-height: 1.02; letter-spacing: -.045em; }
                .message { max-width: 540px; margin: 20px 0 28px; color: #a9c1d0; font-size: 18px; line-height: 1.65; }
                .status { display: flex; align-items: center; gap: 12px; padding: 16px 18px; border: 1px solid color-mix(in srgb, {{accent}} 35%, transparent); border-radius: 16px; background: color-mix(in srgb, {{accent}} 8%, transparent); }
                .state { display: grid; place-items: center; width: 30px; height: 30px; border-radius: 50%; color: #071522; background: {{accent}}; font-weight: 950; }
                .status strong { display: block; }
                .status small { display: block; margin-top: 2px; color: #8faabd; }
                .next { margin: 28px 0 0; color: #d9e8ef; font-weight: 720; }
                .privacy { margin: 10px 0 0; color: #7895a8; font-size: 13px; line-height: 1.55; }
                footer { padding: 19px 28px; border-top: 1px solid rgba(138, 181, 205, .14); color: #6f8da1; font-size: 12px; }
                @media (max-width: 560px) { main { padding: 36px 26px 32px; } .brand, footer { padding-left: 22px; padding-right: 22px; } }
              </style>
            </head>
            <body>
              <section class="card" aria-labelledby="result-title">
                <header class="brand">
                  <svg class="mark" viewBox="0 0 64 64" aria-hidden="true">
                    <rect width="64" height="64" rx="8" fill="#071014"/>
                    <path d="M10 10h20v20H10z" fill="#1599C8"/>
                    <path d="M35 10h19v14H35z" fill="#1599C8"/>
                    <path d="M35 29h19v25H35z" fill="#58D6FF"/>
                    <path d="M10 35h20v19H10z" fill="#1599C8"/>
                    <path d="M17 17h7v7h-7z" fill="#FFC85A"/>
                  </svg>
                  <span>ReplayFoundry</span>
                </header>
                <main>
                  <p class="eyebrow">{{eyebrow}}</p>
                  <h1 id="result-title">{{heading}}</h1>
                  <p class="message">{{message}}</p>
                  <div class="status"><span class="state" aria-hidden="true">{{stateMark}}</span><span><strong>{{stateLabel}}</strong><small>This browser page contains no video or channel data.</small></span></div>
                  <p class="next">You can close this tab and return to ReplayFoundry.</p>
                  <p class="privacy">Your Google password is never shared with ReplayFoundry. When approved, the refresh credential is protected locally by Windows Credential Manager.</p>
                </main>
                <footer>Local-first publishing · ReplayFoundry</footer>
              </section>
            </body>
            </html>
            """;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in query.TrimStart('?').Split(
                     '&',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pieces = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(pieces[0].Replace('+', ' '));
            string value = pieces.Length == 2
                ? Uri.UnescapeDataString(pieces[1].Replace('+', ' '))
                : string.Empty;
            if (!result.TryAdd(key, value))
            {
                throw new YouTubePublishingException(
                    "Replay Foundry received duplicate OAuth response fields.",
                    "youtube.oauth.duplicate-loopback-field");
            }
        }
        return result;
    }

    private static string CreateRandomBase64Url(int bytes) =>
        Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static async Task<YouTubePublishingException>
        CreateOAuthFailureAsync(
            HttpResponseMessage response,
            string code,
            string message,
            CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(
                cancellationToken)
            .ConfigureAwait(false);
        return new YouTubePublishingException(
            message,
            code,
            $"HTTP {(int)response.StatusCode}: {body}");
    }

    private sealed record OAuthCallback(
        string? Code,
        string? State,
        string? Error,
        string? ErrorDescription);

    private sealed class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }
}
