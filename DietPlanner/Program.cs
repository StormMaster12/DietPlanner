using DietPlanner;
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

services.AddScoped<ISettingsService, SettingsService>()
    .AddScoped<IDayPlanService, DayPlanService>()
    .AddScoped<IWeekPlanService, WeekPlanService>()
    .AddScoped<IMealsService, MealsService>()
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

app.MapSettingsEndpoints();
app.MapDayEndpoints();
app.MapMealsEndpoints();
app.MapSlotsEndpoints();
app.MapWeekEndpoints();

app.Run();