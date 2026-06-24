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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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

public partial class Program { }