namespace DietPlanner.Endpoints.Settings;

public record SettingsDto(
    int DailyKcalTarget,
    int DailyProteinTargetG,
    int DailyFibreTargetG,
    int DailyPlantsTarget
);
