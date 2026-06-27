using DietPlanner.Endpoints.Meal;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace DietPlanner.Components.Pages;

public partial class MealUploadPage
{
    [Inject]
    private ILogger<MealUploadPage> Logger { get; set; } = null!;

    [LoggerMessage(Level = LogLevel.Error, Message = "CSV meal import failed for file '{FileName}'")]
    private partial void LogCsvImportFailed(Exception exception, string fileName);

    [LoggerMessage(Level = LogLevel.Error, Message = "PDF meal import failed for file '{FileName}'")]
    private partial void LogPdfImportFailed(Exception exception, string fileName);
}
