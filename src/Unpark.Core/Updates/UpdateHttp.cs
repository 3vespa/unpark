using System.Net.Http.Headers;

namespace Unpark.Core.Updates;

/// <summary>Accès réseau des mises à jour (remplacé par une fausse version dans les tests). HTTPS uniquement.</summary>
public interface IUpdateHttp
{
    Task<string> GetStringAsync(Uri url, TimeSpan timeout, long maxBytes, CancellationToken cancel);

    /// <summary>Télécharge dans <paramref name="destination"/> ; <paramref name="progress"/> reçoit le nombre d'octets reçus.</summary>
    Task DownloadAsync(Uri url, Stream destination, long maxBytes, IProgress<long>? progress, CancellationToken cancel);
}

public sealed class HttpUpdateClient : IUpdateHttp, IDisposable
{
    private readonly HttpClient _http;

    public HttpUpdateClient(string userAgentVersion)
    {
        // SocketsHttpHandler ne suit jamais une redirection de HTTPS vers HTTP ; on vérifie quand même l'adresse finale.
        _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Unpark-Updater", userAgentVersion));
    }

    public async Task<string> GetStringAsync(Uri url, TimeSpan timeout, long maxBytes, CancellationToken cancel)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        cts.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, RequireHttps(url));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        RequireHttps(response.RequestMessage?.RequestUri ?? url);
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(await response.Content.ReadAsStreamAsync(cts.Token), buffer, maxBytes, null, cts.Token);
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    public async Task DownloadAsync(Uri url, Stream destination, long maxBytes, IProgress<long>? progress, CancellationToken cancel)
    {
        using var response = await _http.GetAsync(RequireHttps(url), HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        RequireHttps(response.RequestMessage?.RequestUri ?? url);
        await CopyLimitedAsync(await response.Content.ReadAsStreamAsync(cancel), destination, maxBytes, progress, cancel);
    }

    private static async Task CopyLimitedAsync(Stream source, Stream destination, long maxBytes, IProgress<long>? progress, CancellationToken cancel)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancel)) > 0)
        {
            total += read;
            if (total > maxBytes) throw new UpdateRejectedException("Fichier plus gros que prévu : téléchargement arrêté.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancel);
            progress?.Report(total);
        }
    }

    private static Uri RequireHttps(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps ? url : throw new UpdateRejectedException("Adresse non sécurisée (HTTPS obligatoire) : mise à jour refusée.");

    public void Dispose() => _http.Dispose();
}
