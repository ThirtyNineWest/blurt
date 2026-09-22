namespace Blurt.Engine.Config;

/// <summary>Outcome of an <see cref="ApiKeyValidator"/> attempt.</summary>
public enum ApiKeyValidationResult
{
    /// <summary>AssemblyAI accepted the key.</summary>
    Valid,

    /// <summary>
    /// AssemblyAI rejected the request as a client error meaning a bad, malformed, or unauthorized
    /// key (status 400, 401, 403, or 422).
    /// </summary>
    Invalid,

    /// <summary>
    /// Couldn't reach AssemblyAI, or got a transient or server status — couldn't determine whether the
    /// key is good. The key is not saved on this outcome.
    /// </summary>
    Unreachable,
}

/// <summary>Verifies an AssemblyAI API key with one cheap authenticated request.</summary>
/// <param name="transport">The seam the request is sent through.</param>
/// <param name="baseUrl">The AssemblyAI API root; defaults to the production endpoint.</param>
public sealed class ApiKeyValidator(IHttpTransport transport, Uri? baseUrl = null)
{
    private static readonly Uri DefaultBaseUrl = new("https://api.assemblyai.com");

    private readonly Uri _baseUrl = baseUrl ?? DefaultBaseUrl;

    /// <summary>
    /// Sends <c>GET {base}/v2/transcript?limit=1</c> with <paramref name="key"/> raw in the
    /// Authorization header, and maps the response's status to a verdict. A blank key is reported
    /// invalid without making a request.
    /// </summary>
    public async Task<ApiKeyValidationResult> ValidateAsync(string key)
    {
        var trimmed = key.TrimmedNonEmpty();
        if (trimmed is null)
        {
            return ApiKeyValidationResult.Invalid;
        }

        var uri = new UriBuilder(_baseUrl) { Path = "/v2/transcript", Query = "limit=1" }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Authorization", trimmed);
        request.Headers.TryAddWithoutValidation("User-Agent", HostIdentity.Current.ProductName);

        try
        {
            using var response = await transport.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            return status switch
            {
                >= 200 and < 300 => ApiKeyValidationResult.Valid,
                400 or 401 or 403 or 422 => ApiKeyValidationResult.Invalid,
                _ => ApiKeyValidationResult.Unreachable,
            };
        }
        catch (Exception)
        {
            return ApiKeyValidationResult.Unreachable;
        }
    }
}
