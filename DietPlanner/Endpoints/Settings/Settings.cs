using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.Settings;

public record Settings(string Key, string Value);

public static class ConfigureSettings
{
    public static ModelBuilder AddSettings(this ModelBuilder b)
    {
        _ = b.Entity<Settings>()
              .HasKey(s => s.Key);
        return b;
    }
}