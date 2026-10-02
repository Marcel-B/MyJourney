using System.Net.Http.Headers;

namespace MyJourney.Api.Backup;

/// <summary>WebDAV-Minimalclient fürs Backup: Zielordner anlegen und Datei hochladen.</summary>
public interface IWebDavClient
{
    /// <summary>Legt die Collection (den Zielordner) an. Gibt <c>null</c> bei Erfolg zurück, sonst die Fehlermeldung.</summary>
    Task<string?> EnsureCollection(
        string baseUrl, string username, string password, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Lädt eine lokale Datei per PUT hoch. Gibt <c>null</c> bei Erfolg zurück, sonst die Fehlermeldung.</summary>
    Task<string?> PutFile(
        string baseUrl, string filename, string localPath, string username, string password,
        TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class HttpWebDavClient(IHttpClientFactory httpClientFactory) : IWebDavClient
{
    public async Task<string?> EnsureCollection(
        string baseUrl, string username, string password, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("webdav");
        using var request = new HttpRequestMessage(new HttpMethod("MKCOL"), baseUrl.TrimEnd('/'));
        request.Headers.Authorization = Basic(username, password);
        try
        {
            using var timeoutCts = Timeout(timeout, cancellationToken);
            using var response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            // 405 = existiert schon, 409 = Elternordner fehlt (dann scheitert erst der PUT mit klarer Meldung).
            return status is >= 200 and <= 299 or 405 or 409
                ? null
                : $"WebDAV MKCOL fehlgeschlagen (HTTP {status}): {baseUrl}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"HTTP-Timeout nach {(int)timeout.TotalSeconds}s bei WebDAV MKCOL";
        }
        catch (HttpRequestException ex)
        {
            return $"WebDAV MKCOL fehlgeschlagen: {ex.Message}";
        }
    }

    public async Task<string?> PutFile(
        string baseUrl, string filename, string localPath, string username, string password,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!File.Exists(localPath))
        {
            return $"Datei nicht gefunden: {localPath}";
        }

        var client = httpClientFactory.CreateClient("webdav");
        var url = $"{baseUrl.TrimEnd('/')}/{filename.TrimStart('/')}";
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = Basic(username, password);
        await using var stream = File.OpenRead(localPath);
        request.Content = new StreamContent(stream);
        try
        {
            using var timeoutCts = Timeout(timeout, cancellationToken);
            using var response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? null
                : $"WebDAV PUT fehlgeschlagen (HTTP {(int)response.StatusCode}): {url}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"HTTP-Timeout nach {(int)timeout.TotalSeconds}s beim Nextcloud-Upload";
        }
        catch (HttpRequestException ex)
        {
            return $"Nextcloud-Upload fehlgeschlagen: {ex.Message}";
        }
    }

    private static CancellationTokenSource Timeout(TimeSpan timeout, CancellationToken cancellationToken)
    {
        // Timeout pro Request statt am (gemeinsam genutzten) HttpClient.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return cts;
    }

    private static AuthenticationHeaderValue Basic(string username, string password) =>
        new("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}")));
}
