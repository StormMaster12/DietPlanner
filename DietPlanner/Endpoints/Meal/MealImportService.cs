using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using DietPlanner.Endpoints.Slots;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.Meal;

public sealed class MealImportService : IMealImportService
{
    private readonly AppDbContext _db;

    public MealImportService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<MealImportResult> ImportFromCsvAsync(Stream csvFileContent, CancellationToken cancellationToken)
    {
        (List<MealImportRow> parsedRows, List<string> rowErrors) = ParseCsv(csvFileContent);

        int insertedCount = 0;
        int updatedCount = 0;

        foreach (MealImportRow row in parsedRows)
        {
            MealEntry? existingMeal = await _db.Meals.SingleOrDefaultAsync(
                m => m.SlotKey == row.SlotKey && m.Name.ToLower() == row.Name.ToLower(),
                cancellationToken);

            if (existingMeal is null)
            {
                _db.Meals.Add(new MealEntry
                {
                    Id = Guid.NewGuid(),
                    Name = row.Name,
                    SlotKey = row.SlotKey,
                    Kcal = row.Kcal,
                    ProteinG = row.ProteinG,
                    CarbsG = row.CarbsG,
                    FibreG = row.FibreG,
                    Plants = row.Plants,
                    MfName = row.MfName,
                    ZoeNotes = row.ZoeNotes,
                    Notes = row.Notes
                });
                insertedCount++;
            }
            else
            {
                existingMeal.Kcal = row.Kcal;
                existingMeal.ProteinG = row.ProteinG;
                existingMeal.CarbsG = row.CarbsG;
                existingMeal.FibreG = row.FibreG;
                existingMeal.Plants = row.Plants;
                existingMeal.MfName = row.MfName;
                existingMeal.ZoeNotes = row.ZoeNotes;
                existingMeal.Notes = row.Notes;
                updatedCount++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new MealImportResult(insertedCount, updatedCount, rowErrors);
    }

    /// <summary>
    /// Reads every data row of the CSV individually so a single malformed row (bad number, unknown
    /// slot name, etc.) is reported and skipped rather than aborting the whole upload.
    /// </summary>
    private static (List<MealImportRow> ParsedRows, List<string> RowErrors) ParseCsv(Stream csvFileContent)
    {
        var parsedRows = new List<MealImportRow>();
        var rowErrors = new List<string>();

        CsvConfiguration csvConfiguration = new(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim
        };

        using StreamReader streamReader = new(csvFileContent);
        using CsvReader csvReader = new(streamReader, csvConfiguration);

        csvReader.Read();
        csvReader.ReadHeader();

        // CSV row 1 is the header, so the first data row is "row 2" from the user's point of view.
        int currentCsvRowNumber = 1;

        while (csvReader.Read())
        {
            currentCsvRowNumber++;

            try
            {
                string name = csvReader.GetField("Name") ?? throw new FormatException("Name is required");
                string slotKeyText = csvReader.GetField("SlotKey") ?? throw new FormatException("SlotKey is required");
                string mfName = csvReader.GetField("MfName") ?? throw new FormatException("MfName is required");

                if (!Enum.TryParse(slotKeyText, ignoreCase: true, out SlotKey slotKey))
                {
                    throw new FormatException($"Unknown SlotKey '{slotKeyText}'. Expected one of: {string.Join(", ", Enum.GetNames<SlotKey>())}");
                }

                int kcal = ParseRequiredInt(csvReader, "Kcal");
                int proteinG = ParseRequiredInt(csvReader, "ProteinG");
                int carbsG = ParseRequiredInt(csvReader, "CarbsG");
                int fibreG = ParseRequiredInt(csvReader, "FibreG");
                int plants = ParseRequiredInt(csvReader, "Plants");

                string? zoeNotes = csvReader.GetField("ZoeNotes");
                string? notes = csvReader.GetField("Notes");

                parsedRows.Add(new MealImportRow(name, slotKey, kcal, proteinG, carbsG, fibreG, plants, mfName, zoeNotes, notes));
            }
            catch (Exception ex) when (ex is FormatException or CsvHelperException)
            {
                rowErrors.Add($"Row {currentCsvRowNumber}: {ex.Message}");
            }
        }

        return (parsedRows, rowErrors);
    }

    private static int ParseRequiredInt(CsvReader csvReader, string columnName)
    {
        string? rawValue = csvReader.GetField(columnName);
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue))
        {
            throw new FormatException($"{columnName} '{rawValue}' is not a whole number");
        }

        return parsedValue;
    }
}
