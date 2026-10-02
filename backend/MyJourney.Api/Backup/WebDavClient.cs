using System.Net.Http.Headers;

namespace MyJourney.Api.Backup;

/// <summary>WebDAV-Minimalclient fürs Backup: Zielordner anlegen und Datei hochladen.</summary>
public interface IWebDavClient
{
    /// <summary>Legt den Zielordner an, inklusive fehlender Elternordner. Gibt <c>null</c> bei Erfolg zurück, sonst die Fehlermeldung.</summary>
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
        // MKCOL legt immer nur eine Ebene an (fehlender Elternordner = HTTP 409),
        // deshalb jede Pfadebene unterhalb der WebDAV-Wurzel einzeln anlegen.
        foreach (var url in CollectionUrls(baseUrl))
        {
            var error = await Mkcol(url, username, password, timeout, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    /// <summary>
    /// Zerlegt die WebDAV-URL in die anzulegenden Ordnerebenen (äußerste zuerst). Ordner beginnen
    /// hinter der WebDAV-Wurzel (<c>remote.php/dav/files/&lt;benutzer&gt;</c> bzw. <c>remote.php/webdav</c>);
    /// bei unbekanntem URL-Aufbau bleibt es bei einem einzelnen MKCOL auf die volle URL.
    /// </summary>
    public static IReadOnlyList<string> CollectionUrls(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return [trimmed];
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var start = FolderStartIndex(segments);
        if (start < 0 || start >= segments.Length)
        {
            return [trimmed];
        }

        var urls = new List<string>();
        for (var depth = start + 1; depth <= segments.Length; depth++)
        {
            urls.Add($"{uri.Scheme}://{uri.Authority}/{string.Join('/', segments[..depth])}");
        }

        return urls;
    }

    private static int FolderStartIndex(string[] segments)
    {
        for (var i = 0; i < segments.Length; i++)
        {
            if (!segments[i].Equals("remote.php", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // remote.php/dav/files/<benutzer>/<ordner...>
            if (i + 2 < segments.Length &&
                segments[i + 1].Equals("dav", StringComparison.OrdinalIgnoreCase) &&
                segments[i + 2].Equals("files", StringComparison.OrdinalIgnoreCase))
            {
                return i + 4;
            }

            // remote.php/webdav/<ordner...> (älterer Alias)
            if (i + 1 < segments.Length &&
                segments[i + 1].Equals("webdav", StringComparison.OrdinalIgnoreCase))
            {
                return i + 2;
            }
        }

        return -1;
    }

    private async Task<string?> Mkcol(
        string url, string username, string password, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("webdav");
        using var request = new HttpRequestMessage(new HttpMethod("MKCOL"), url);
        request.Headers.Authorization = Basic(username, password);
        try
        {
            using var timeoutCts = Timeout(timeout, cancellationToken);
            using var response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            // 405 = existiert schon, 409 = Elternordner fehlt (möglich, wenn die URL-Struktur
            // nicht erkannt wurde – dann scheitert erst der PUT mit klarer Meldung).
            return status is >= 200 and <= 299 or 405 or 409
                ? null
                : $"WebDAV MKCOL fehlgeschlagen (HTTP {status}): {url}";
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
