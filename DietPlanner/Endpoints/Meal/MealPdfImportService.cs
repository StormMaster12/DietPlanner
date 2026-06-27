using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DietPlanner.Endpoints.Slots;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace DietPlanner.Endpoints.Meal;

public sealed partial class MealPdfImportService : IMealPdfImportService
{
    private const string AnthropicVersion = "2023-06-01";

    private const string SystemPrompt = """
        You extract structured meal data from free-form text (recipe sheets, meal plans, nutrition
        labels). Read the text and identify every distinct meal/recipe it describes. For each one,
        output an object with these exact fields:
        - name (string, required): the meal's name.
        - slotKey (string, required): one of "Breakfast", "Lunch", "Dinner", "BeforeBed". Infer this
          from context (e.g. a snack or supper-time item is "BeforeBed") if it isn't stated outright.
        - kcal, proteinG, carbsG, fibreG (integers, required): nutrition per serving. Give your best
          estimate from the ingredients/quantities described if no explicit figure is given.
        - plants (integer, required): count of distinct plant-based ingredients (the "30 plants a
          week" metric), 0 if none.
        - mfName (string, required): a short name suitable for looking the meal up in MyFitnessPal;
          reuse "name" if nothing better is available.
        - zoeNotes (string or null): any notes related to gut health / the ZOE program, else null.
        - notes (string or null): any other free-text notes worth keeping, else null.

        Respond with ONLY a raw JSON array of these objects - no markdown fences, no commentary. If
        the text contains no identifiable meals, respond with an empty array [].
        """;

    private readonly AppDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly AnthropicOptions _options;
    private readonly ILogger<MealPdfImportService> _logger;

    public MealPdfImportService(
        AppDbContext db, HttpClient httpClient, IOptions<AnthropicOptions> options, ILogger<MealPdfImportService> logger)
    {
        _db = db;
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Anthropic API call failed while extracting meals from PDF text")]
    private partial void LogAnthropicCallFailed(Exception exception);

    public async Task<MealImportResult> ImportFromPdfAsync(Stream pdfFileContent, CancellationToken cancellationToken)
    {
        using MemoryStream bufferedContent = new();
        await pdfFileContent.CopyToAsync(bufferedContent, cancellationToken);
        bufferedContent.Position = 0;

        string extractedText = ExtractText(bufferedContent);

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return new MealImportResult(0, 0, ["The PDF contained no extractable text."]);
        }

        (List<MealImportRow> parsedRows, List<string> rowErrors) = await ExtractRowsAsync(extractedText, cancellationToken);

        return await MealImportUpsert.UpsertRowsAsync(_db, parsedRows, rowErrors, cancellationToken);
    }

    private static string ExtractText(Stream pdfFileContent)
    {
        using PdfDocument document = PdfDocument.Open(pdfFileContent);
        StringBuilder textBuilder = new();

        foreach (var page in document.GetPages())
        {
            textBuilder.AppendLine(page.Text);
        }

        return textBuilder.ToString();
    }

    private async Task<(List<MealImportRow> ParsedRows, List<string> RowErrors)> ExtractRowsAsync(
        string extractedText, CancellationToken cancellationToken)
    {
        var rowErrors = new List<string>();

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            rowErrors.Add("PDF import is not configured: missing Anthropic API key.");
            return ([], rowErrors);
        }

        List<ExtractedMealRow> extractedRows;
        try
        {
            extractedRows = await CallAnthropicAsync(extractedText, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            LogAnthropicCallFailed(ex);
            rowErrors.Add($"Could not extract meals from the PDF: {ex.Message}");
            return ([], rowErrors);
        }

        var parsedRows = new List<MealImportRow>();
        for (int i = 0; i < extractedRows.Count; i++)
        {
            ExtractedMealRow extractedRow = extractedRows[i];
            int itemNumber = i + 1;

            try
            {
                if (string.IsNullOrWhiteSpace(extractedRow.Name))
                {
                    throw new FormatException("name is required");
                }

                if (string.IsNullOrWhiteSpace(extractedRow.MfName))
                {
                    throw new FormatException("mfName is required");
                }

                if (!Enum.TryParse(extractedRow.SlotKey, ignoreCase: true, out SlotKey slotKey))
                {
                    throw new FormatException($"Unknown slotKey '{extractedRow.SlotKey}'. Expected one of: {string.Join(", ", Enum.GetNames<SlotKey>())}");
                }

                parsedRows.Add(new MealImportRow(
                    extractedRow.Name,
                    slotKey,
                    extractedRow.Kcal,
                    extractedRow.ProteinG,
                    extractedRow.CarbsG,
                    extractedRow.FibreG,
                    extractedRow.Plants,
                    extractedRow.MfName,
                    extractedRow.ZoeNotes,
                    extractedRow.Notes));
            }
            catch (FormatException ex)
            {
                rowErrors.Add($"Item {itemNumber}: {ex.Message}");
            }
        }

        return (parsedRows, rowErrors);
    }

    private async Task<List<ExtractedMealRow>> CallAnthropicAsync(string extractedText, CancellationToken cancellationToken)
    {
        var requestBody = new AnthropicRequest(
            _options.Model,
            8192,
            SystemPrompt,
            [new AnthropicMessage("user", extractedText)]);

        using HttpRequestMessage request = new(HttpMethod.Post, _options.BaseUrl);
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Anthropic API returned {(int)response.StatusCode}: {responseBody}");
        }

        AnthropicResponse? anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody);
        string? extractedJson = anthropicResponse?.Content.FirstOrDefault(c => c.Type == "text")?.Text;

        if (string.IsNullOrWhiteSpace(extractedJson))
        {
            throw new JsonException("Anthropic API returned no text content.");
        }

        if (anthropicResponse?.StopReason == "max_tokens")
        {
            throw new JsonException(
                "The Anthropic response was truncated because it contained too many meals for a single PDF. " +
                "Try splitting the PDF into smaller files and importing them separately.");
        }

        return JsonSerializer.Deserialize<List<ExtractedMealRow>>(StripMarkdownFences(extractedJson)) ?? [];
    }

    private static string StripMarkdownFences(string text)
    {
        string trimmed = text.Trim();
        if (!trimmed.StartsWith("```"))
        {
            return trimmed;
        }

        int firstNewLine = trimmed.IndexOf('\n');
        int fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine < 0 || fenceEnd <= firstNewLine
            ? trimmed
            : trimmed[(firstNewLine + 1)..fenceEnd].Trim();
    }

    private sealed record AnthropicRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] List<AnthropicMessage> Messages);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record AnthropicResponse(
        [property: JsonPropertyName("content")] List<AnthropicContentBlock> Content,
        [property: JsonPropertyName("stop_reason")] string? StopReason);

    private sealed record AnthropicContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);

    private sealed record ExtractedMealRow(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("slotKey")] string SlotKey,
        [property: JsonPropertyName("kcal")] int Kcal,
        [property: JsonPropertyName("proteinG")] int ProteinG,
        [property: JsonPropertyName("carbsG")] int CarbsG,
        [property: JsonPropertyName("fibreG")] int FibreG,
        [property: JsonPropertyName("plants")] int Plants,
        [property: JsonPropertyName("mfName")] string MfName,
        [property: JsonPropertyName("zoeNotes")] string? ZoeNotes,
        [property: JsonPropertyName("notes")] string? Notes);
}
