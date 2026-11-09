using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Kronix.Services;

public sealed class ReleaseRepositoryStore
{
    public const string DefaultRepository = "roemertimon/Kronix";
    private readonly string _path;

    public ReleaseRepositoryStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kronix", "update-repository.txt");
    }

    public string Load() => File.Exists(_path) ? File.ReadAllText(_path).Trim() : DefaultRepository;

    public void Save(string repository)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, repository);
    }
}

public sealed class ReleaseUpdateService
{
    private static readonly HttpClient DefaultClient = CreateClient();
    private static readonly Regex RepositoryPattern = new(
        @"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9_.-]{1,100}$",
        RegexOptions.CultureInvariant);
    private readonly HttpClient _client;

    public ReleaseUpdateService(HttpClient? client = null) => _client = client ?? DefaultClient;

    public static bool TryNormalizeRepository(string input, out string repository)
    {
        repository = input.Trim();
        if (Uri.TryCreate(repository, UriKind.Absolute, out Uri? uri))
        {
            if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                repository = string.Empty;
                return false;
            }

            repository = uri.AbsolutePath.Trim('/');
        }

        return RepositoryPattern.IsMatch(repository) && !repository.EndsWith("/.", StringComparison.Ordinal)
            && !repository.EndsWith("/..", StringComparison.Ordinal);
    }

    public static string? InstalledVersion
    {
        get
        {
            string? info = typeof(ReleaseUpdateService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return info is not null && info.StartsWith("release-", StringComparison.Ordinal)
                ? info["release-".Length..].Split('+')[0]
                : null;
        }
    }

    public static int? CompareVersions(string? installed, string latest)
    {
        if (!TryParseVersion(installed, out Version? installedVersion) ||
            !TryParseVersion(latest, out Version? latestVersion))
        {
            return null;
        }

        return installedVersion!.CompareTo(latestVersion);
    }

    private static bool TryParseVersion(string? text, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            value = value[1..];
        }

        if (!Regex.IsMatch(value, @"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        return Version.TryParse(value, out version);
    }

    public async Task<string> GetLatestVersionAsync(string repository, CancellationToken cancellationToken)
    {
        if (!TryNormalizeRepository(repository, out string normalized))
        {
            throw new ArgumentException("Bitte ein gültiges GitHub-Repository angeben.", nameof(repository));
        }

        using HttpResponseMessage response = await _client.GetAsync(
            $"https://api.github.com/repos/{normalized}/releases/latest", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new HttpRequestException("GitHub-API-Ratenlimit erreicht. Bitte später erneut versuchen.", null, System.Net.HttpStatusCode.Forbidden);
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("tag_name", out JsonElement tag) ||
            tag.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tag.GetString()))
        {
            throw new InvalidDataException("Das GitHub-Release enthält keine gültige Versionsangabe.");
        }

        return tag.GetString()!;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Kronix-UpdateCheck");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
