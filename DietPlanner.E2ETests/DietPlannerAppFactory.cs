using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using WireMock.Net.Testcontainers;

namespace DietPlanner.E2ETests;

/// <summary>
/// Runs the app as the actual Docker image that ships to production - built once per test run via
/// Testcontainers - rather than an in-process TestServer, so e2e runs exercise the same artifact
/// that gets deployed. Each instance starts a fresh, disposable container; since the image bundles
/// its own seeded SQLite db, every container gets an independent copy via Docker's writable layer,
/// so test runs never interfere with each other or with the git-tracked DietPlannerDatabase.db.
///
/// A WireMock container on the same Docker network stands in for the real Anthropic API - the app
/// container's Anthropic:BaseUrl is overridden to point at it, so tests can stub LLM responses via
/// <see cref="StubAnthropicResponseAsync"/> instead of calling out to the real API.
/// </summary>
public sealed class DietPlannerAppFactory : IAsyncDisposable
{
    private const ushort ContainerPort = 8080;
    private const string WireMockNetworkAlias = "anthropic-stub";

    public const string TestUsername = "e2e-test-user";
    public const string TestPassword = "e2e-test-password";

    private static readonly IFutureDockerImage Image = new ImageFromDockerfileBuilder()
        .WithDockerfileDirectory(CommonDirectoryPath.GetSolutionDirectory(), string.Empty)
        .WithDockerfile("Dockerfile")
        .Build();

    private static readonly SemaphoreSlim ImageBuildLock = new(1, 1);
    private static bool s_imageBuilt;

    private INetwork _network = null!;
    private WireMockContainer _wireMockContainer = null!;
    private IContainer _container = null!;
    public string ServerAddress { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        await EnsureImageBuiltAsync();

        _network = new NetworkBuilder().Build();
        await _network.CreateAsync();

        _wireMockContainer = new WireMockContainerBuilder()
            .WithNetwork(_network)
            .WithNetworkAliases(WireMockNetworkAlias)
            .Build();
        await _wireMockContainer.StartAsync();

        _container = new ContainerBuilder()
            .WithImage(Image)
            .WithNetwork(_network)
            .WithPortBinding(ContainerPort, true)
            .WithEnvironment("APP_USERNAME", TestUsername)
            .WithEnvironment("APP_PASSWORD", TestPassword)
            .WithEnvironment("Anthropic__BaseUrl", $"http://{WireMockNetworkAlias}/v1/messages")
            .WithEnvironment("Anthropic__ApiKey", "test-key")
            // The app now requires Basic Auth in Production, so the readiness probe (sent without
            // credentials) gets a 401 once the server is actually up - that counts as "ready" here.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
                .ForPort(ContainerPort)
                .ForStatusCodeMatching(code => code == System.Net.HttpStatusCode.Unauthorized)))
            .Build();

        await _container.StartAsync();

        ServerAddress = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(ContainerPort)}";
    }

    /// <summary>
    /// Registers a stub mapping on the WireMock container so the app's next call to the Anthropic
    /// Messages API (POST /v1/messages) returns the given raw response body instead of hitting the
    /// real API.
    /// </summary>
    public async Task StubAnthropicResponseAsync(string anthropicResponseBody)
    {
        const string mappingJson = """
            {
              "Request": {
                "Path": "/v1/messages",
                "Methods": [ "POST" ]
              },
              "Response": {
                "StatusCode": 200,
                "Headers": { "Content-Type": "application/json" },
                "Body": {{{body}}}
              }
            }
            """;

        string escapedBody = System.Text.Json.JsonSerializer.Serialize(anthropicResponseBody);
        string requestJson = mappingJson.Replace("{{{body}}}", escapedBody);

        using HttpClient adminClient = _wireMockContainer.CreateClient();
        using HttpResponseMessage response = await adminClient.PostAsync(
            "__admin/mappings", new StringContent(requestJson, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        if (_wireMockContainer is not null)
        {
            await _wireMockContainer.DisposeAsync();
        }

        if (_network is not null)
        {
            await _network.DeleteAsync();
        }
    }

    private static async Task EnsureImageBuiltAsync()
    {
        if (s_imageBuilt)
        {
            return;
        }

        await ImageBuildLock.WaitAsync();
        try
        {
            if (!s_imageBuilt)
            {
                await Image.CreateAsync();
                s_imageBuilt = true;
            }
        }
        finally
        {
            ImageBuildLock.Release();
        }
    }
}
