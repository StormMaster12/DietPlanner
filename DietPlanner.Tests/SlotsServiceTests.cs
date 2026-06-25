using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class SlotsServiceTests
{
    private TestDatabase _database = null!;

    [SetUp]
    public void SetUp() => _database = new TestDatabase();

    [TearDown]
    public void TearDown() => _database.Dispose();

    [Test]
    public async Task CreateSlotAsync_WithNewKey_CreatesSlot()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);

        SlotDto? result = await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Breakfast, "Breakfast", 1), CancellationToken.None);

        Assert.That(result, Is.EqualTo(new SlotDto("Breakfast", 1, SlotKey.Breakfast)));
    }

    [Test]
    public async Task CreateSlotAsync_TrimsDisplayName()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);

        SlotDto? result = await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Lunch, "  Lunch  ", 2), CancellationToken.None);

        Assert.That(result!.DisplayName, Is.EqualTo("Lunch"));
    }

    [Test]
    public async Task CreateSlotAsync_WithExistingKey_ReturnsNull()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);
        await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Dinner, "Dinner", 3), CancellationToken.None);

        SlotDto? result = await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Dinner, "Dinner Take 2", 99), CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetSlotsAsync_ReturnsSlotsOrderedBySortOrder()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);
        await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Dinner, "Dinner", 3), CancellationToken.None);
        await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Breakfast, "Breakfast", 1), CancellationToken.None);
        await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.Lunch, "Lunch", 2), CancellationToken.None);

        List<SlotDto> result = (await service.GetSlotsAsync(CancellationToken.None)).ToList();

        Assert.That(result.Select(s => s.Key), Is.EqualTo(new[] { SlotKey.Breakfast, SlotKey.Lunch, SlotKey.Dinner }));
    }

    [Test]
    public async Task DeleteSlotAsync_WithExistingKey_RemovesSlot()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);
        await service.CreateSlotAsync(new CreateSlotRequest(SlotKey.BeforeBed, "Before Bed", 4), CancellationToken.None);

        DeleteResult result = await service.DeleteSlotAsync(SlotKey.BeforeBed, CancellationToken.None);

        Assert.That(result, Is.EqualTo(DeleteResult.Success));
        Assert.That(await service.GetSlotsAsync(CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task DeleteSlotAsync_WithUnknownKey_ReturnsNotFound()
    {
        using AppDbContext db = _database.CreateContext();
        SlotsService service = new(db);

        DeleteResult result = await service.DeleteSlotAsync(SlotKey.Breakfast, CancellationToken.None);

        Assert.That(result, Is.EqualTo(DeleteResult.NotFound));
    }

}
