using DietPlanner.Endpoints.Slots;
using FluentAssertions;
using Xunit;

namespace DietPlanner.Tests;

public sealed class SlotsServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    [Fact]
    public async Task CreateSlotAsync_WithNewKey_CreatesSlot()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);

        SlotDto? result = await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Breakfast, "Breakfast", 1), CancellationToken.None);

        result.Should().Be(new SlotDto("Breakfast", 1, SlotKey.Breakfast));
    }

    [Fact]
    public async Task CreateSlotAsync_TrimsDisplayName()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);

        SlotDto? result = await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Lunch, "  Lunch  ", 2), CancellationToken.None);

        result!.DisplayName.Should().Be("Lunch");
    }

    [Fact]
    public async Task CreateSlotAsync_WithExistingKey_ReturnsNull()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);
        await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Dinner, "Dinner", 3), CancellationToken.None);

        SlotDto? result = await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Dinner, "Dinner Take 2", 99), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSlotsAsync_ReturnsSlotsOrderedBySortOrder()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);
        await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Dinner, "Dinner", 3), CancellationToken.None);
        await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Breakfast, "Breakfast", 1), CancellationToken.None);
        await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Lunch, "Lunch", 2), CancellationToken.None);

        List<SlotDto> result = (await service.GetSlotsAsync(CancellationToken.None)).ToList();

        result.Select(s => s.Key).Should().ContainInOrder(SlotKey.Breakfast, SlotKey.Lunch, SlotKey.Dinner);
    }

    [Fact]
    public async Task DeleteSlotAsync_WithExistingKey_RemovesSlot()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);
        await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.BeforeBed, "Before Bed", 4), CancellationToken.None);

        DeleteResult result = await service.DeleteSlotAsync(SlotKey.BeforeBed, CancellationToken.None);

        result.Should().Be(DeleteResult.Success);
        (await service.GetSlotsAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSlotAsync_WithUnknownKey_ReturnsNotFound()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);

        DeleteResult result = await service.DeleteSlotAsync(SlotKey.Breakfast, CancellationToken.None);

        result.Should().Be(DeleteResult.NotFound);
    }

    public void Dispose() => _database.Dispose();
}
