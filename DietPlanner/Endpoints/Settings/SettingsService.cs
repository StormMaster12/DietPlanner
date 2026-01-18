using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.Settings;

public sealed class SettingsService : ISettingsService
{
    private readonly AppDbContext _db;

    private const int DefaultKcal = 2300;
    private const int DefaultProtein = 165;
    private const int DefaultFibre = 30;
    private const int DefaultPlants = 30;

    public SettingsService(AppDbContext db) => _db = db;

    public async Task<SettingsDto> GetAsync(CancellationToken ct)
    {
        var map = await _db.Settings
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new SettingsDto(
            GetInt(map, "daily_kcal_target", DefaultKcal),
            GetInt(map, "daily_protein_target_g", DefaultProtein),
            GetInt(map, "daily_fibre_target_g", DefaultFibre),
            GetInt(map, "daily_plants_target", DefaultPlants)
        );
    }

    public async Task<SettingsDto> UpdateAsync(UpdateSettingsRequest req, CancellationToken ct)
    {
        await UpsertKeyAsync("daily_kcal_target", req.DailyKcalTarget.ToString(), ct);
        await UpsertKeyAsync("daily_protein_target_g", req.DailyProteinTargetG.ToString(), ct);
        await UpsertKeyAsync("daily_fibre_target_g", req.DailyFibreTargetG.ToString(), ct);
        await UpsertKeyAsync("daily_plants_target", req.DailyPlantsTarget.ToString(), ct);

        await _db.SaveChangesAsync(ct);

        return new SettingsDto(
            req.DailyKcalTarget,
            req.DailyProteinTargetG,
            req.DailyFibreTargetG,
            req.DailyPlantsTarget
        );
    }

    private static int GetInt(Dictionary<string, string> map, string key, int fallback)
        => map.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;

    private async Task UpsertKeyAsync(string key, string value, CancellationToken ct)
    {
        var row = await _db.Settings.SingleOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            _db.Settings.Add(new Settings(key, value));
        }
        else
        {
            row = row with { Value = value };
        }
    }
}
