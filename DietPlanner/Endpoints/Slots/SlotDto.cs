namespace DietPlanner.Endpoints.Slots;

public record SlotDto(
    string DisplayName,
    int SortOrder,
    SlotKey Key);
