using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using JiraSheetBoard.Models;
using JiraSheetBoard.Services;

namespace JiraSheetBoard.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ISheetDataService _sheetService;

    public HomeController(ILogger<HomeController> logger, ISheetDataService sheetService)
    {
        _logger = logger;
        _sheetService = sheetService;
    }

    public async Task<IActionResult> Index(
        string? search,
        [FromQuery(Name = "status")] string[]? status,
        [FromQuery(Name = "assignee")] string[]? assignee,
        [FromQuery(Name = "priority")] string[]? priority,
        [FromQuery(Name = "type")] string[]? type,
        bool withPhotos = false,
        string sortBy = "priority-desc",
        string view = "board")
    {
        var allIssues = await _sheetService.GetAllIssuesAsync();
        var statuses = await _sheetService.GetAllStatusesAsync();
        var assignees = await _sheetService.GetAllAssigneesAsync();
        var priorities = await _sheetService.GetAllPrioritiesAsync();
        var types = await _sheetService.GetAllIssueTypesAsync();
        var columns = await _sheetService.GetAllColumnNamesAsync();

        var statusList = CleanFilterArray(status);
        var assigneeList = CleanFilterArray(assignee);
        var priorityList = CleanFilterArray(priority);
        var typeList = CleanFilterArray(type);

        // Precompute option counts across the dataset for user-friendly UI badges
        var statusCounts = allIssues
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Status) ? "Open" : i.Status.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var assigneeCounts = allIssues
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Assignee) ? "Unassigned" : i.Assignee.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var priorityCounts = allIssues
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Priority) ? "Major" : i.Priority.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var typeCounts = allIssues
            .GroupBy(i => string.IsNullOrWhiteSpace(i.IssueType) ? "Bug" : i.IssueType.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Sort full dataset according to sortBy
        var sortedIssues = (sortBy switch
        {
            "priority-desc" => allIssues.OrderByDescending(i => GetPriorityWeight(i.Priority)),
            "priority-asc" => allIssues.OrderBy(i => GetPriorityWeight(i.Priority)),
            "key-asc" => allIssues.OrderBy(i => i.Key),
            "key-desc" => allIssues.OrderByDescending(i => i.Key),
            "title-asc" => allIssues.OrderBy(i => i.Title),
            "title-desc" => allIssues.OrderByDescending(i => i.Title),
            "date-asc" => allIssues.OrderBy(i => i.DueDate ?? DateTime.MaxValue),
            "date-desc" => allIssues.OrderByDescending(i => i.DueDate ?? DateTime.MinValue),
            "created-desc" => allIssues.OrderByDescending(i => i.CreatedDate ?? DateTime.MinValue),
            _ => allIssues.OrderByDescending(i => GetPriorityWeight(i.Priority))
        }).ToList();

        var model = new BoardViewModel
        {
            Issues = sortedIssues,
            Statuses = statuses,
            Assignees = assignees,
            Priorities = priorities,
            IssueTypes = types,
            AllColumnNames = columns,
            StatusCounts = statusCounts,
            AssigneeCounts = assigneeCounts,
            PriorityCounts = priorityCounts,
            TypeCounts = typeCounts,
            TotalUnfilteredCount = allIssues.Count,
            CurrentSearch = search ?? "",
            CurrentStatusFilters = statusList,
            CurrentAssigneeFilters = assigneeList,
            CurrentPriorityFilters = priorityList,
            CurrentTypeFilters = typeList,
            OnlyWithPhotos = withPhotos,
            CurrentSortBy = sortBy,
            ActiveView = string.IsNullOrWhiteSpace(view) ? "board" : view.ToLowerInvariant(),
            SheetSourceTitle = _sheetService.GetCurrentSourceName(),
            ConfiguredUrl = _sheetService.GetConfiguredUrl(),
            LastUpdated = _sheetService.GetLastUpdated()
        };

        return View(model);
    }

    private static List<string> CleanFilterArray(string[]? input)
    {
        if (input == null || input.Length == 0) return new List<string>();
        return input
            .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.Equals("all", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync()
    {
        var url = _sheetService.GetConfiguredUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            TempData["ErrorMessage"] = "No Google Sheet link configured yet. Click 'Connect Sheet' to provide your link.";
            return RedirectToAction(nameof(Index));
        }

        var result = await _sheetService.ImportFromUrlAsync(url);
        if (result.Success)
        {
            TempData["SuccessMessage"] = $"Synced live data from Google Sheet! ({result.Count} cards loaded)";
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> GetIssue(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "Issue ID is required." });
        }

        var issue = await _sheetService.GetIssueByIdAsync(id);
        if (issue == null)
        {
            return NotFound(new { error = "Issue not found." });
        }

        return Json(issue);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(150_000_000)]
    public async Task<IActionResult> Import(ImportSheetRequest request)
    {
        try
        {
            // Case 1: Uploaded file (.xlsx, .csv)
            if (request.UploadFile != null && request.UploadFile.Length > 0)
            {
                var ext = Path.GetExtension(request.UploadFile.FileName).ToLowerInvariant();
                (bool Success, string Message, int Count) result;

                if (ext == ".xlsx" || ext == ".xlsm")
                {
                    using var stream = request.UploadFile.OpenReadStream();
                    result = await _sheetService.ImportXlsxAsync(stream, request.UploadFile.FileName);
                }
                else
                {
                    using var reader = new StreamReader(request.UploadFile.OpenReadStream(), Encoding.UTF8);
                    var content = await reader.ReadToEndAsync();
                    result = await _sheetService.ImportCsvAsync(content, request.UploadFile.FileName);
                }

                if (result.Success)
                {
                    TempData["SuccessMessage"] = result.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                }
                return RedirectToAction(nameof(Index));
            }

            // Case 2: Google Sheet URL
            if (!string.IsNullOrWhiteSpace(request.GoogleSheetUrl))
            {
                var result = await _sheetService.ImportFromUrlAsync(request.GoogleSheetUrl);
                if (result.Success)
                {
                    TempData["SuccessMessage"] = result.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                }
                return RedirectToAction(nameof(Index));
            }

            // Case 3: Raw pasted text
            if (!string.IsNullOrWhiteSpace(request.RawData))
            {
                var result = await _sheetService.ImportCsvAsync(request.RawData, "Pasted Sheet Data");
                if (result.Success)
                {
                    TempData["SuccessMessage"] = result.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                }
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = "Please supply a Google Sheet URL, upload a CSV file, or paste raw sheet rows.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing import");
            TempData["ErrorMessage"] = $"Import failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect()
    {
        await _sheetService.ResetToSampleDataAsync();
        TempData["SuccessMessage"] = "Sheet disconnected. Enter any sheet link to load a new board.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetSample()
    {
        await _sheetService.ResetToSampleDataAsync();
        TempData["SuccessMessage"] = "Live sheet data reloaded successfully!";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ExportCsv()
    {
        var issues = await _sheetService.GetAllIssuesAsync();
        var allCustom = await _sheetService.GetAllColumnNamesAsync();

        var sb = new StringBuilder();
        // Header
        sb.AppendLine("Key,Title,Status,Priority,IssueType,Assignee,Reporter,DueDate,Photos,Tags,Description");

        foreach (var i in issues)
        {
            var photosJoined = string.Join(";", i.Photos);
            var tagsJoined = string.Join(";", i.Tags);
            var dueStr = i.DueDate?.ToString("yyyy-MM-dd") ?? "";
            
            sb.AppendLine($"\"{EscapeCsv(i.Key)}\",\"{EscapeCsv(i.Title)}\",\"{EscapeCsv(i.Status)}\",\"{EscapeCsv(i.Priority)}\",\"{EscapeCsv(i.IssueType)}\",\"{EscapeCsv(i.Assignee)}\",\"{EscapeCsv(i.Reporter)}\",\"{EscapeCsv(dueStr)}\",\"{EscapeCsv(photosJoined)}\",\"{EscapeCsv(tagsJoined)}\",\"{EscapeCsv(i.Description)}\"");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"JiraSheet_Export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private static int GetPriorityWeight(string? p) => (p?.ToLowerInvariant()) switch
    {
        "critical" => 6,
        "highest" => 5,
        "major" => 4,
        "high" => 4,
        "medium" => 3,
        "minor" => 2,
        "low" => 2,
        "lowest" => 1,
        _ => 3
    };

    private static string EscapeCsv(string val)
    {
        if (string.IsNullOrEmpty(val)) return "";
        return val.Replace("\"", "\"\"");
    }
}
