using DietPlanner.Endpoints.Settings;
using FluentAssertions;
using Xunit;

namespace DietPlanner.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    [Fact]
    public async Task GetAsync_WithNoSettingsSaved_ReturnsDefaults()
    {
        using AppDbContext db = _database.CreateContext();
        SettingsService service = new(db);

        SettingsDto result = await service.GetAsync(CancellationToken.None);

        result.Should().Be(new SettingsDto(2300, 165, 220, 30, 30));
    }

    [Fact]
    public async Task UpdateAsync_ThenGetAsync_RoundTripsValues()
    {
        using AppDbContext db = _database.CreateContext();
        SettingsService service = new(db);

        UpdateSettingsRequest request = new(2000, 150, 200, 25, 20);
        await service.UpdateAsync(request, CancellationToken.None);

        SettingsDto result = await service.GetAsync(CancellationToken.None);

        result.Should().Be(new SettingsDto(2000, 150, 200, 25, 20));
    }

    [Fact]
    public async Task UpdateAsync_CalledTwice_PersistsTheSecondUpdate()
    {
        // Regression test: UpsertKeyAsync used to reassign a `record with` expression to a local
        // variable instead of attaching it to the change tracker, so updates to already-existing
        // settings rows were silently dropped after the first save.
        using AppDbContext db = _database.CreateContext();
        SettingsService service = new(db);

        await service.UpdateAsync(new UpdateSettingsRequest(2000, 150, 200, 25, 20), CancellationToken.None);
        await service.UpdateAsync(new UpdateSettingsRequest(2500, 180, 250, 35, 40), CancellationToken.None);

        SettingsDto result = await service.GetAsync(CancellationToken.None);

        result.Should().Be(new SettingsDto(2500, 180, 250, 35, 40));
    }

    [Fact]
    public async Task UpdateAsync_CalledTwiceWithFreshContexts_PersistsTheSecondUpdate()
    {
        UpdateSettingsRequest first = new(2000, 150, 200, 25, 20);
        UpdateSettingsRequest second = new(2500, 180, 250, 35, 40);

        using (AppDbContext db1 = _database.CreateContext())
        {
            await new SettingsService(db1).UpdateAsync(first, CancellationToken.None);
        }

        using (AppDbContext db2 = _database.CreateContext())
        {
            await new SettingsService(db2).UpdateAsync(second, CancellationToken.None);
        }

        using AppDbContext db3 = _database.CreateContext();
        SettingsDto result = await new SettingsService(db3).GetAsync(CancellationToken.None);

        result.Should().Be(new SettingsDto(2500, 180, 250, 35, 40));
    }

    public void Dispose() => _database.Dispose();
}
