namespace DietPlanner.Endpoints.Settings;

public interface ISettingsService
{
    Task<SettingsDto> GetAsync(CancellationToken ct);
    Task<SettingsDto> UpdateAsync(UpdateSettingsRequest req, CancellationToken ct);
}