namespace DietPlanner.Endpoints.Settings;

public record SettingsDto(
    int DailyKcalTarget,
    int DailyProteinTargetG,
    int DailyCarbTargetG,
    int DailyFibreTargetG,
    int DailyPlantsTarget
);
