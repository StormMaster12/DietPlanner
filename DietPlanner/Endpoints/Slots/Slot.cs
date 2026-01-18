namespace DietPlanner.Endpoints.Slots;

public record Slot(
    SlotKey Key,
    string DisplayName,
    int SortOrder);
