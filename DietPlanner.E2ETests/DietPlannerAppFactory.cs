using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using WireMock.Net.Testcontainers;

namespace DietPlanner.E2ETests;

/// <summary>
/// Runs the app as the actual Docker image that ships to production - built once via
/// Testcontainers - rather than an in-process TestServer, so e2e runs exercise the same artifact
/// that gets deployed. A single instance is started once for the whole test run and reused by
/// every test; <see cref="ResetStateAsync"/> wipes per-test data between tests instead of paying
/// for a fresh container each time.
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
            .WithEnvironment("E2E_TESTING", "true")
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

    /// <summary>
    /// Wipes per-test app data and any WireMock stub registered via
    /// <see cref="StubAnthropicResponseAsync"/>, so the shared container starts each test from a
    /// clean slate without needing to be recreated.
    /// </summary>
    public async Task ResetStateAsync()
    {
        using HttpClient wireMockAdminClient = _wireMockContainer.CreateClient();
        using HttpResponseMessage clearMappingsResponse =
            await wireMockAdminClient.DeleteAsync("__admin/mappings");
        clearMappingsResponse.EnsureSuccessStatusCode();

        using HttpClient appClient = new() { BaseAddress = new Uri($"{ServerAddress}/") };
        appClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{TestUsername}:{TestPassword}")));

        using HttpResponseMessage resetResponse = await appClient.PostAsync("__test__/reset", content: null);
        resetResponse.EnsureSuccessStatusCode();
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
