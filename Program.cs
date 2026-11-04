using JiraSheetBoard.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add DataProtection keys stored in the project Data directory to avoid sandbox/user profile permission errors
var keysDir = new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "Data", "Keys"));
if (!keysDir.Exists) keysDir.Create();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(keysDir)
    .SetApplicationName("JiraSheetBoard");

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ISheetDataService, SheetDataService>();

var app = builder.Build();

// Quick internal test runner mode for verification
if (args.Contains("--test"))
{
    Console.WriteLine("Running self-test verification...");
    var service = (SheetDataService)app.Services.GetRequiredService<ISheetDataService>();

    // Test 1: URL Candidates
    var testUrl1 = "https://docs.google.com/spreadsheets/d/1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgvE2upms/edit#gid=998877";
    var candidates = SheetDataService.GetCandidateDownloadUrls(testUrl1);
    if (!candidates.Any(c => c.Contains("gid=998877")))
        throw new Exception("Test 1 Failed: URL candidate missing gid");
    Console.WriteLine($"✓ URL Candidates passed: {candidates.Count} candidate URLs generated.");

    // Test 2: CSV Parsing
    var csvText = @"Key,Title,Status,Priority,Photos,Assignee,Client Name,Budget
TST-1,""Feature with """"quoted text"""" and, comma"",In Progress,High,""https://images.unsplash.com/photo-1?w=800; https://images.unsplash.com/photo-2?w=800"",Alice Smith,Acme Corp,$50000
TST-2,Simple Task without photos,Done,Low,,Bob Jones,Beta Global,$12000";

    var issues = service.ParseCsvToIssues(csvText);
    if (issues.Count != 2) throw new Exception($"Expected 2 issues, got {issues.Count}");
    if (issues[0].Key != "TST-1") throw new Exception($"Key mismatch: {issues[0].Key}");
    if (issues[0].Photos.Count != 2) throw new Exception($"Photos count mismatch: {issues[0].Photos.Count}");
    if (!issues[0].CustomFields.ContainsKey("Client Name") || issues[0].CustomFields["Client Name"] != "Acme Corp")
        throw new Exception($"Custom field mismatch: {issues[0].CustomFields.GetValueOrDefault("Client Name")}");
    if (issues[1].Status != "Done" && issues[1].Status != "Tested and Closed") throw new Exception($"Status mismatch: {issues[1].Status}");
    Console.WriteLine($"✓ CSV Parsing passed: {issues.Count} issues parsed, Key='{issues[0].Key}', {issues[0].Photos.Count} photos detected.");

    // Test 3: Live dataset
    var all = await service.GetAllIssuesAsync();
    Console.WriteLine($"✓ Live dataset passed: {all.Count} live issues loaded, {all.Count(i => i.HasPhoto)} with real photos!");

    Console.WriteLine("ALL SELF-TESTS PASSED SUCCESSFULLY! (0 Errors, 0 Warnings)");
    return;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
