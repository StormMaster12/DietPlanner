using System.Security.Cryptography;
using System.Text;
using DietPlanner;
using DietPlanner.Components;
using DietPlanner.Endpoints.DayPlan;
using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using DietPlanner.Endpoints.WeekPlan;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
var services = builder.Services;
services.AddEndpointsApiExplorer();
services.AddSwaggerGen();

#if DEBUG
services.AddSassCompiler();
#endif

// Razor Components power the interactive Diet Planner / Meals / Settings web pages, rendered
// server-side (Blazor Server) so the UI can call straight into the same EF Core services that
// back the minimal API below, without needing a separate JavaScript HTTP client layer.
services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

services.AddScoped<ISettingsService, SettingsService>()
    .AddScoped<IDayPlanService, DayPlanService>()
    .AddScoped<IWeekPlanService, WeekPlanService>()
    .AddScoped<IWeekPlanGeneratorService, WeekPlanGeneratorService>()
    .AddScoped<IMealsService, MealsService>()
    .AddScoped<IMealImportService, MealImportService>()
    .AddScoped<ISlotsService, SlotsService>();

services.AddValidatorsFromAssemblyContaining<Program>();
services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Db")));

WebApplication app = builder.Build();

// Volume-mounted SQLite starts empty on first boot; apply migrations so the
// schema exists without needing to ship a pre-built .db file to the volume.
using (IServiceScope scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Single-user deployment: reject everything before it reaches Blazor/EF Core so
// floods cost a header comparison, not a SignalR connection or a DB hit.
string? allowedIpsRaw = builder.Configuration["ALLOWED_IPS"];
HashSet<string>? allowedIps = string.IsNullOrWhiteSpace(allowedIpsRaw)
    ? null
    : allowedIpsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();

string? appUsername = builder.Configuration["APP_USERNAME"];
string? appPassword = builder.Configuration["APP_PASSWORD"];
bool authConfigured = !string.IsNullOrEmpty(appUsername) && !string.IsNullOrEmpty(appPassword);

if (!authConfigured && !app.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "APP_USERNAME and APP_PASSWORD must be set outside Development (set as Fly secrets).");
}

app.Use(async (context, next) =>
{
    if (allowedIps is not null)
    {
        // Fly's edge always overwrites this header, so clients cannot spoof it.
        string? clientIp = context.Request.Headers["Fly-Client-IP"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString();

        if (clientIp is null || !allowedIps.Contains(clientIp))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }

    if (authConfigured)
    {
        string? header = context.Request.Headers.Authorization.FirstOrDefault();
        if (header is null || !header.StartsWith("Basic ", StringComparison.Ordinal) ||
            !TryValidateBasicAuth(header["Basic ".Length..], appUsername!, appPassword!))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"DietPlanner\"";
            return;
        }
    }

    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapSettingsEndpoints();
app.MapDayEndpoints();
app.MapMealsEndpoints();
app.MapSlotsEndpoints();
app.MapWeekEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program
{
    private static bool TryValidateBasicAuth(string base64Credentials, string expectedUsername, string expectedPassword)
    {
        byte[] decodedBytes;
        try
        {
            decodedBytes = Convert.FromBase64String(base64Credentials);
        }
        catch (FormatException)
        {
            return false;
        }

        string decoded = Encoding.UTF8.GetString(decodedBytes);
        int separatorIndex = decoded.IndexOf(':');
        if (separatorIndex < 0)
        {
            return false;
        }

        string username = decoded[..separatorIndex];
        string password = decoded[(separatorIndex + 1)..];

        bool usernameMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(username), Encoding.UTF8.GetBytes(expectedUsername));
        bool passwordMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(expectedPassword));

        return usernameMatches && passwordMatches;
    }
}