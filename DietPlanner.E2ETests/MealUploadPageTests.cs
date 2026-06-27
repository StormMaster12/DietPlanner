using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DietPlanner.E2ETests;

[TestFixture]
public sealed class MealUploadPageTests : PageTestBase
{
    [Test]
    public async Task MealUploadPage_UploadingValidCsv_ShowsInsertedCount()
    {
        await GoToAsync("meals/upload");

        string csv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                     "Oatmeal,Breakfast,300,10,40,5,2,Oatmeal MFP,Good fibre,\n";
        byte[] csvBytes = System.Text.Encoding.UTF8.GetBytes(csv);

        await Page.SetInputFilesAsync("#csv-file-input", new FilePayload
        {
            Name = "meals.csv",
            MimeType = "text/csv",
            Buffer = csvBytes
        });
        await Page.ClickAsync("text=Upload CSV");

        await Expect(Page.Locator(".alert")).ToContainTextAsync("Inserted 1 new meal(s)", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
    }

    [Test]
    public async Task MealUploadPage_UploadingCsvWithBadRow_ShowsRowError()
    {
        await GoToAsync("meals/upload");

        string csv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                     "Oatmeal,Brunch,300,10,40,5,2,Oatmeal MFP,,\n";
        byte[] csvBytes = System.Text.Encoding.UTF8.GetBytes(csv);

        await Page.SetInputFilesAsync("#csv-file-input", new FilePayload
        {
            Name = "meals.csv",
            MimeType = "text/csv",
            Buffer = csvBytes
        });
        await Page.ClickAsync("text=Upload CSV");

        await Expect(Page.Locator(".alert--error")).ToContainTextAsync("could not be imported", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
    }

    [Test]
    public async Task MealUploadPage_UploadingPdf_ShowsInsertedCount()
    {
        string extractedJsonArray = """
            [{"name":"Oatmeal","slotKey":"Breakfast","kcal":300,"proteinG":10,"carbsG":40,"fibreG":5,"plants":2,"mfName":"Oatmeal MFP","zoeNotes":"Good fibre","notes":null}]
            """;
        string anthropicResponseBody = JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "text", text = extractedJsonArray } }
        });
        await StubAnthropicResponseAsync(anthropicResponseBody);

        await GoToAsync("meals/upload");

        await Page.SetInputFilesAsync("#pdf-file-input", new FilePayload
        {
            Name = "meal-plan.pdf",
            MimeType = "application/pdf",
            Buffer = BuildPdf("Breakfast: oatmeal with berries.")
        });
        await Page.ClickAsync("text=Upload PDF");

        await Expect(Page.Locator(".alert")).ToContainTextAsync("Inserted 1 new meal(s)", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
    }

    private static byte[] BuildPdf(string text)
    {
        PdfDocumentBuilder builder = new();
        PdfPageBuilder page = builder.AddPage(PageSize.A4);
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(text, 12, new PdfPoint(25, 700), font);
        return builder.Build();
    }
}
