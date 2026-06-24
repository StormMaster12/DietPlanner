using Microsoft.Playwright;
using NUnit.Framework;

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

        await Page.SetInputFilesAsync("input[type=file]", new FilePayload
        {
            Name = "meals.csv",
            MimeType = "text/csv",
            Buffer = csvBytes
        });

        await Expect(Page.Locator(".alert")).ToContainTextAsync("Inserted 1 new meal(s)", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
    }

    [Test]
    public async Task MealUploadPage_UploadingCsvWithBadRow_ShowsRowError()
    {
        await GoToAsync("meals/upload");

        string csv = "Name,SlotKey,Kcal,ProteinG,CarbsG,FibreG,Plants,MfName,ZoeNotes,Notes\n" +
                     "Oatmeal,Brunch,300,10,40,5,2,Oatmeal MFP,,\n";
        byte[] csvBytes = System.Text.Encoding.UTF8.GetBytes(csv);

        await Page.SetInputFilesAsync("input[type=file]", new FilePayload
        {
            Name = "meals.csv",
            MimeType = "text/csv",
            Buffer = csvBytes
        });

        await Expect(Page.Locator(".alert--error")).ToContainTextAsync("could not be imported", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
    }
}
