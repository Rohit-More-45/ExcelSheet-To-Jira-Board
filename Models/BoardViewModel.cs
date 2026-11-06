namespace JiraSheetBoard.Models;

public class BoardViewModel
{
    public List<SheetIssue> Issues { get; set; } = new();
    public List<string> Statuses { get; set; } = new();
    public List<string> Assignees { get; set; } = new();
    public List<string> Priorities { get; set; } = new();
    public List<string> IssueTypes { get; set; } = new();
    public List<string> AllColumnNames { get; set; } = new();

    // Option counts across the full dataset for user-friendly filter badges
    public Dictionary<string, int> StatusCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> AssigneeCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> PriorityCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> TypeCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentSearch { get; set; } = "";
    public List<string> CurrentStatusFilters { get; set; } = new();
    public List<string> CurrentAssigneeFilters { get; set; } = new();
    public List<string> CurrentPriorityFilters { get; set; } = new();
    public List<string> CurrentTypeFilters { get; set; } = new();

    // Backwards compatibility properties
    public string CurrentStatusFilter => CurrentStatusFilters.FirstOrDefault() ?? "";
    public string CurrentAssigneeFilter => CurrentAssigneeFilters.FirstOrDefault() ?? "";
    public string CurrentPriorityFilter => CurrentPriorityFilters.FirstOrDefault() ?? "";
    public string CurrentTypeFilter => CurrentTypeFilters.FirstOrDefault() ?? "";

    public bool OnlyWithPhotos { get; set; }
    public string CurrentSortBy { get; set; } = "priority-desc"; // priority-desc, priority-asc, key-asc, key-desc, title-asc, title-desc, date-asc, date-desc, created-desc
    public string ActiveView { get; set; } = "board"; // board, table, gallery

    public string SheetSourceTitle { get; set; } = "Default Sheet";
    public List<string> AvailableSheets { get; set; } = new();
    public string? CurrentSheetName { get; set; }
    public Dictionary<string, int> SheetCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ConfiguredUrl { get; set; }
    public bool HasConfiguredUrl => !string.IsNullOrWhiteSpace(ConfiguredUrl);
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public int TotalIssuesCount => Issues.Count;
    public int TotalPhotosCount => Issues.Count(i => i.HasPhoto);
    public int TotalUnfilteredCount { get; set; }

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(CurrentSearch) ||
        CurrentStatusFilters.Any() ||
        CurrentAssigneeFilters.Any() ||
        CurrentPriorityFilters.Any() ||
        CurrentTypeFilters.Any() ||
        OnlyWithPhotos;

    public int ActiveFilterCount =>
        (!string.IsNullOrWhiteSpace(CurrentSearch) ? 1 : 0) +
        CurrentStatusFilters.Count +
        CurrentAssigneeFilters.Count +
        CurrentPriorityFilters.Count +
        CurrentTypeFilters.Count +
        (OnlyWithPhotos ? 1 : 0);

    // Dynamic columns to display in Kanban board (all columns present so client filtering can toggle them)
    public List<string> DisplayStatuses => Statuses;

    // Grouping by status for board view
    public Dictionary<string, List<SheetIssue>> IssuesByStatus =>
        DisplayStatuses.ToDictionary(
            status => status,
            status => Issues.Where(i =>
                string.Equals(i.Status, status, StringComparison.OrdinalIgnoreCase) ||
                (string.IsNullOrWhiteSpace(i.Status) && string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase))
            ).ToList(),
            StringComparer.OrdinalIgnoreCase
        );
}

public class ImportSheetRequest
{
    public string? GoogleSheetUrl { get; set; }
    public IFormFile? UploadFile { get; set; }
    public string? RawData { get; set; }
}

public class ParsedSheetRow
{
    public int ExcelRowNum { get; set; }
    public List<string> Cells { get; set; } = new();
}

