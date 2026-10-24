using NUnit.Framework;

namespace DietPlanner.E2ETests;

[TestFixture]
public sealed class StaticAssetCachingTests
{
    [TestCase("DietPlanner.styles.css")]
    [TestCase("css/app.css")]
    [TestCase("js/sidebar.js")]
    public async Task UnfingerprintedAsset_IsServedWithNoCache_SoDeploysAreNotMaskedByStaleCopies(string path)
    {
        using HttpClient client = TestRunSetup.Factory.CreateAppClient();

        using HttpResponseMessage response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.That(response.Headers.CacheControl?.NoCache, Is.True);
    }
}
