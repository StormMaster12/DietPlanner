using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
        - ingredients (array, required): every distinct ingredient line used in the recipe, each an
          object with:
          - name (string, required): the ingredient's name, e.g. "chicken breast".
          - quantity (number, required): the amount; 0 if no amount is stated.
          - unit (string or null): unit of measure, e.g. "g", "tbsp", "cup"; null for countable items
            (e.g. "2 eggs").
          Use an empty array if the text doesn't break the meal down into individual ingredients.

        The text may contain "### Chapter: <name>" marker lines inserted by the importer to show
        which section of the source document the following meals came from (e.g. "### Chapter:
        Breakfast", "### Chapter: Dinner (continued)"). Treat the chapter name as a strong signal
        for slotKey - "Breakfast"/"Lunch"/"Dinner" map directly, and "Snack(s)"/"Supper"/"Before Bed"
        map to "BeforeBed" - but let a meal's own description override the chapter when it clearly
        belongs to a different slot. A "(continued)" suffix just means the chapter's text was split
        across multiple requests; it does not change the slotKey. Never emit the marker lines
        themselves as meal data.

        Respond with ONLY a raw JSON array of these objects - no markdown fences, no commentary. If
        the text contains no identifiable meals, respond with an empty array [].
        """;

    private const int MaxChunkChars = 8000;

    private static readonly Regex ChapterKeywordRegex = new(
        @"\b(breakfast|lunch|dinner|before\s*bed|supper|snacks?|dessert)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    /// <summary>
    /// Recognises short, standalone section headings (e.g. "Breakfast", "Snacks &amp; Before Bed",
    /// "Dinner:") without matching ordinary sentences that merely mention a meal in passing (e.g.
    /// "Breakfast: oatmeal with berries.") - those have content after the keyword and are left as body
    /// text. Word-boundary matching also keeps "brunch" from being mistaken for "lunch".
    /// </summary>
    private static bool TryGetChapterHeading(string line, out string heading)
    {
        heading = "";
        string trimmed = line.Trim();

        if (trimmed.Length == 0 || trimmed.Length > 40 || trimmed.Any(char.IsDigit))
        {
            return false;
        }

        if (trimmed.EndsWith('.') || trimmed.EndsWith(','))
        {
            return false;
        }

        string candidate = trimmed;
        int colonIndex = candidate.IndexOf(':');
        if (colonIndex >= 0)
        {
            if (colonIndex != candidate.Length - 1)
            {
                return false;
            }

            candidate = candidate[..colonIndex].TrimEnd();
        }

        if (candidate.Length == 0 || candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 4)
        {
            return false;
        }

        if (!ChapterKeywordRegex.IsMatch(candidate))
        {
            return false;
        }

        heading = candidate;
        return true;
    }

    private static List<TextChapter> SplitIntoChapters(string text)
    {
        var chapters = new List<TextChapter>();
        StringBuilder currentBody = new();
        string currentHeading = "Unspecified";

        foreach (string line in text.Split('\n'))
        {
            if (TryGetChapterHeading(line, out string heading))
            {
                if (currentBody.Length > 0)
                {
                    chapters.Add(new TextChapter(currentHeading, currentBody.ToString()));
                    currentBody.Clear();
                }

                currentHeading = heading;
                continue;
            }

            currentBody.AppendLine(line);
        }

        if (currentBody.Length > 0)
        {
            chapters.Add(new TextChapter(currentHeading, currentBody.ToString()));
        }

        return chapters;
    }

    /// <summary>
    /// Groups chapters into request-sized chunks, each prefixed with "### Chapter: ..." markers so a
    /// chunk that doesn't start at a chapter boundary (because the chapter itself was too long, or it
    /// was bundled with neighbouring chapters) still carries its slot-key context for the model.
    /// </summary>
    private static List<string> BuildChunks(List<TextChapter> chapters)
    {
        var chunks = new List<string>();
        StringBuilder currentChunk = new();

        void FlushChunk()
        {
            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
            }
        }

        foreach (TextChapter chapter in chapters)
        {
            string remainingBody = chapter.Body;
            bool isFirstPiece = true;

            while (remainingBody.Length > 0)
            {
                if (currentChunk.Length > 0 && currentChunk.Length + remainingBody.Length > MaxChunkChars)
                {
                    FlushChunk();
                }

                int capacity = MaxChunkChars - currentChunk.Length;
                int take = Math.Min(capacity, remainingBody.Length);
                if (take < remainingBody.Length)
                {
                    int lastNewline = remainingBody.LastIndexOf('\n', Math.Max(take - 1, 0));
                    if (lastNewline > 0)
                    {
                        take = lastNewline + 1;
                    }
                }

                string piece = remainingBody[..take];
                remainingBody = remainingBody[take..];

                string heading = isFirstPiece ? chapter.Heading : $"{chapter.Heading} (continued)";
                currentChunk.AppendLine($"### Chapter: {heading}");
                currentChunk.Append(piece);
                currentChunk.AppendLine();
                isFirstPiece = false;

                if (currentChunk.Length >= MaxChunkChars)
                {
                    FlushChunk();
                }
            }
        }

        FlushChunk();
        return chunks;
    }

    private sealed record TextChapter(string Heading, string Body);

    private async Task<(List<MealImportRow> ParsedRows, List<string> RowErrors)> ExtractRowsAsync(
        string extractedText, CancellationToken cancellationToken)
    {
        var rowErrors = new List<string>();

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            rowErrors.Add("PDF import is not configured: missing Anthropic API key.");
            return ([], rowErrors);
        }

        var extractedRows = new List<ExtractedMealRow>();
        List<string> chunks = BuildChunks(SplitIntoChapters(extractedText));
        for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
        {
            try
            {
                extractedRows.AddRange(await CallAnthropicAsync(chunks[chunkIndex], cancellationToken));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                LogAnthropicCallFailed(ex);
                string part = chunks.Count > 1 ? $"part {chunkIndex + 1} of {chunks.Count} of " : "";
                rowErrors.Add($"Could not extract meals from {part}the PDF: {ex.Message}");
            }
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

                List<MealImportIngredientRow> ingredients = (extractedRow.Ingredients ?? [])
                    .Select(i => string.IsNullOrWhiteSpace(i.Name)
                        ? throw new FormatException("An ingredient is missing a name")
                        : new MealImportIngredientRow(i.Name, i.Quantity, i.Unit))
                    .ToList();

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
                    extractedRow.Notes,
                    ingredients));
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
                "The Anthropic response was truncated because it described too many meals to fit in a " +
                "single reply, even after splitting the PDF by chapter. Try splitting the source PDF into " +
                "smaller files and importing them separately.");
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
        [property: JsonPropertyName("notes")] string? Notes,
        [property: JsonPropertyName("ingredients")] List<ExtractedIngredientRow>? Ingredients);

    private sealed record ExtractedIngredientRow(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("quantity")] decimal Quantity,
        [property: JsonPropertyName("unit")] string? Unit);
}
