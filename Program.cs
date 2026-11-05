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
