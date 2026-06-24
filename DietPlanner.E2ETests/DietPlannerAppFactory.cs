using DietPlanner.Endpoints.Slots;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DietPlanner.E2ETests;

/// <summary>
/// Hosts the real app on a real Kestrel socket (rather than the in-memory TestServer that
/// <see cref="WebApplicationFactory{TEntryPoint}"/> uses by default) so a Playwright-driven
/// browser can navigate to it like any other site, backed by a throwaway SQLite database so
/// e2e runs never touch the git-tracked DietPlannerDatabase.db.
/// </summary>
public sealed class DietPlannerAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    public string ServerAddress { get; private set; } = string.Empty;

    /// <summary>
    /// Forces <see cref="WebApplicationFactory{TEntryPoint}"/> to build and start the host via
    /// <see cref="CreateHost"/> (which boots the real Kestrel server and captures
    /// <see cref="ServerAddress"/>). The base class's own post-build step then tries to cast the
    /// registered <see cref="IServer"/> to <c>TestServer</c> and throws, since this factory
    /// deliberately hosts on Kestrel instead - that failure is irrelevant here since the app is
    /// already up by that point, so it's swallowed.
    /// </summary>
    public void Start()
    {
        try
        {
            CreateClient().Dispose();
        }
        catch (InvalidCastException)
        {
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        _connection.Open();

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(_connection));
        });

        builder.ConfigureWebHost(webHostBuilder => webHostBuilder.UseKestrel().UseUrls("http://127.0.0.1:0"));

        IHost host = builder.Build();
        host.Start();

        using AppDbContext db = host.Services.GetRequiredService<IServiceScopeFactory>()
            .CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        db.Slots.AddRange(
            new Slot(SlotKey.Breakfast, "Breakfast", 1),
            new Slot(SlotKey.Lunch, "Lunch", 2),
            new Slot(SlotKey.Dinner, "Dinner", 3),
            new Slot(SlotKey.BeforeBed, "Before bed", 4));
        db.SaveChanges();

        IServerAddressesFeature addressesFeature = host.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!;
        ServerAddress = addressesFeature.Addresses.First();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
