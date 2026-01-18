using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.Slots;

public class SlotsService : ISlotsService
{
    private readonly AppDbContext _appDbContext;

    public SlotsService(AppDbContext appDbContext)
    {
        _appDbContext = appDbContext;
    }

    public async Task<SlotDto?> CreateSlotAsync(CreateSlotRequest request, CancellationToken cancellationToken)
    {
        bool exists = await _appDbContext.Slots.AnyAsync(s => s.Key == request.Key);
        if (exists)
        {
            return null;
        }

        Slot slot = new(request.Key, request.DisplayName.Trim(), request.SortOrder);

        _appDbContext.Slots.Add(slot);
        await _appDbContext.SaveChangesAsync();

        return new SlotDto(slot.DisplayName, slot.SortOrder, slot.Key);
    }

    public async Task<DeleteResult> DeleteSlotAsync(SlotKey key, CancellationToken cancellationToken)
    {
        Slot? slot = await _appDbContext.Slots.SingleOrDefaultAsync(s => s.Key == key);
        if (slot is null)
        {
            return DeleteResult.NotFound;
        }

        _appDbContext.Slots.Remove(slot);
        await _appDbContext.SaveChangesAsync();
        return DeleteResult.Success;
    }

    public async Task<IEnumerable<SlotDto>> GetSlotsAsync(CancellationToken cancellationToken)
    {
        List<SlotDto> items = await _appDbContext.Slots
                .OrderBy(s => s.SortOrder)
                .Select(s => new SlotDto(s.DisplayName, s.SortOrder, s.Key))
                .ToListAsync();

        return items;
    }
}
