using System.Diagnostics;

namespace DietPlanner.E2ETests;

/// <summary>
/// Runs the app as the actual Docker image that ships to production - built once per test run -
/// rather than an in-process TestServer, so e2e runs exercise the same artifact that gets
/// deployed. Each instance starts a fresh, disposable container; since the image bundles its own
/// seeded SQLite db, every container gets an independent copy via Docker's writable layer, so test
/// runs never interfere with each other or with the git-tracked DietPlannerDatabase.db.
/// </summary>
public sealed class DietPlannerAppFactory : IDisposable
{
    private const string ImageTag = "dietplanner-e2e:test";
    private static readonly object BuildLock = new();
    private static bool s_imageBuilt;

    private string _containerId = string.Empty;
    public string ServerAddress { get; private set; } = string.Empty;

    public void Start()
    {
        EnsureImageBuilt();

        _containerId = RunDocker("run", "-d", "-P", ImageTag).Trim();

        string portMapping = RunDocker("port", _containerId, "8080/tcp").Trim();
        string hostPort = portMapping.Split(':').Last();
        ServerAddress = $"http://127.0.0.1:{hostPort}";

        WaitUntilReady();
    }

    public void Dispose()
    {
        if (_containerId.Length > 0)
        {
            RunDocker("rm", "-f", _containerId);
        }
    }

    private static void EnsureImageBuilt()
    {
        if (s_imageBuilt)
        {
            return;
        }

        lock (BuildLock)
        {
            if (s_imageBuilt)
            {
                return;
            }

            if (!ImageExists())
            {
                RunDocker("build", "-t", ImageTag, FindRepoRoot());
            }

            s_imageBuilt = true;
        }
    }

    private static bool ImageExists()
    {
        try
        {
            RunDocker("image", "inspect", ImageTag);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DietPlanner.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                $"Could not locate repo root (DietPlanner.sln) starting from {AppContext.BaseDirectory}.");
    }

    private void WaitUntilReady()
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };
        Exception? lastError = null;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                client.GetAsync(ServerAddress).GetAwaiter().GetResult();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(500);
            }
        }

        throw new TimeoutException($"Container at {ServerAddress} did not become ready in time.", lastError);
    }

    private static string RunDocker(params string[] arguments)
    {
        ProcessStartInfo startInfo = new("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"docker {string.Join(' ', arguments)} failed: {stderr}");
        }

        return stdout;
    }
}
