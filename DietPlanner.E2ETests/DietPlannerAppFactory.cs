using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;

namespace DietPlanner.E2ETests;

/// <summary>
/// Runs the app as the actual Docker image that ships to production - built once per test run via
/// Testcontainers - rather than an in-process TestServer, so e2e runs exercise the same artifact
/// that gets deployed. Each instance starts a fresh, disposable container; since the image bundles
/// its own seeded SQLite db, every container gets an independent copy via Docker's writable layer,
/// so test runs never interfere with each other or with the git-tracked DietPlannerDatabase.db.
/// </summary>
public sealed class DietPlannerAppFactory : IAsyncDisposable
{
    private const ushort ContainerPort = 8080;

    private static readonly IFutureDockerImage Image = new ImageFromDockerfileBuilder()
        .WithDockerfileDirectory(CommonDirectoryPath.GetSolutionDirectory(), string.Empty)
        .WithDockerfile("Dockerfile")
        .Build();

    private static readonly SemaphoreSlim ImageBuildLock = new(1, 1);
    private static bool s_imageBuilt;

    private IContainer _container = null!;
    public string ServerAddress { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        await EnsureImageBuiltAsync();

        _container = new ContainerBuilder()
            .WithImage(Image)
            .WithPortBinding(ContainerPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(ContainerPort)))
            .Build();

        await _container.StartAsync();

        ServerAddress = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(ContainerPort)}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
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
