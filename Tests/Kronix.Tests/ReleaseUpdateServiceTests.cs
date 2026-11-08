using System.Net;
using System.Net.Http;
using System.Text;
using Kronix.Services;

namespace Kronix.Tests;

public class ReleaseUpdateServiceTests
{
    [Theory]
    [InlineData("roemertimon/Kronix", "roemertimon/Kronix")]
    [InlineData(" https://github.com/roemertimon/Kronix/ ", "roemertimon/Kronix")]
    public void NormalizeRepository_AcceptsNamesAndGithubUrls(string input, string expected)
    {
        Assert.True(ReleaseUpdateService.TryNormalizeRepository(input, out string repository));
        Assert.Equal(expected, repository);
    }

    [Theory]
    [InlineData("https://example.com/owner/repo")]
    [InlineData("http://github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo/releases")]
    [InlineData("https://github.com/owner/repo?x=1")]
    [InlineData("owner/..")]
    [InlineData("owner/repo/extra")]
    public void NormalizeRepository_RejectsInvalidInputs(string input)
    {
        Assert.False(ReleaseUpdateService.TryNormalizeRepository(input, out _));
    }

    [Theory]
    [InlineData("1.0.9", "v1.0.10", -1)]
    [InlineData("1.0.10", "v1.0.10", 0)]
    [InlineData("2.0.0", "v1.9.9", 1)]
    public void CompareVersions_UsesNumericVersions(string installed, string latest, int expected)
    {
        Assert.Equal(expected, Math.Sign(ReleaseUpdateService.CompareVersions(installed, latest)!.Value));
    }

    [Theory]
    [InlineData(null, "v1.0.10")]
    [InlineData("1.0.0-beta", "v1.0.10")]
    [InlineData("1.0.0", "nightly")]
    public void CompareVersions_ReportsUncomparableVersions(string? installed, string latest)
    {
        Assert.Null(ReleaseUpdateService.CompareVersions(installed, latest));
    }

    [Fact]
    public void RepositoryStore_DefaultsPersistsAndCanBeDisabled()
    {
        string directory = Path.Combine(Path.GetTempPath(), "kronix-update-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "repository.txt");
        try
        {
            var store = new ReleaseRepositoryStore(path);
            Assert.Equal(ReleaseRepositoryStore.DefaultRepository, store.Load());
            store.Save("owner/repo");
            Assert.Equal("owner/repo", store.Load());
            store.Save(string.Empty);
            Assert.Equal(string.Empty, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task LatestVersion_ReadsReleaseTagFromGithub()
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, "{\"tag_name\":\"v1.0.42\"}"));
        var service = new ReleaseUpdateService(client);
        Assert.Equal("v1.0.42", await service.GetLatestVersionAsync("owner/repo", CancellationToken.None));
    }

    [Fact]
    public async Task LatestVersion_SurfacesHttpAndInvalidResponseErrors()
    {
        using var missingClient = new HttpClient(new StubHandler(HttpStatusCode.NotFound, ""));
        var missing = new ReleaseUpdateService(missingClient);
        await Assert.ThrowsAsync<HttpRequestException>(
            () => missing.GetLatestVersionAsync("owner/repo", CancellationToken.None));

        using var invalidClient = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        var invalid = new ReleaseUpdateService(invalidClient);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => invalid.GetLatestVersionAsync("owner/repo", CancellationToken.None));

        using var arrayClient = new HttpClient(new StubHandler(HttpStatusCode.OK, "[]"));
        var array = new ReleaseUpdateService(arrayClient);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => array.GetLatestVersionAsync("owner/repo", CancellationToken.None));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://api.github.com/repos/owner/repo/releases/latest", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
