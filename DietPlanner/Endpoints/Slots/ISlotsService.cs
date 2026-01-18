namespace DietPlanner.Endpoints.Slots;

public interface ISlotsService
{
    Task<IEnumerable<SlotDto>> GetSlotsAsync(CancellationToken cancellationToken);
    Task<SlotDto?> CreateSlotAsync(CreateSlotRequest request, CancellationToken cancellationToken);
    Task<DeleteResult> DeleteSlotAsync(SlotKey key, CancellationToken cancellationToken);
}
