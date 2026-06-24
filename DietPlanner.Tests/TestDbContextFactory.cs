using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Tests;

/// <summary>
/// Creates an <see cref="AppDbContext"/> backed by a SQLite in-memory database. The connection
/// must stay open for the lifetime of the context (SQLite's `:memory:` database is destroyed the
/// moment its only connection closes), so callers should dispose both together via
/// <see cref="TestDatabase"/>.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext context = CreateContext();
        context.Database.EnsureCreated();
    }

    public AppDbContext CreateContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
