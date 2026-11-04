using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using JiraSheetBoard.Models;

namespace JiraSheetBoard.Services;

public class SheetDataService : ISheetDataService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SheetDataService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly string _dataDirectory;
    private readonly string _activeDataFilePath;
    private readonly string _metadataFilePath;
    private readonly string _mediaDirectory;

    private List<SheetIssue> _cachedIssues = new();
    private string _sourceName = "Live Sheet Data";
    private string? _configuredUrl;
    private DateTime _lastUpdated = DateTime.Now;
    private readonly object _lock = new();

    public SheetDataService(
        IWebHostEnvironment environment,
        ILogger<SheetDataService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _environment = environment;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;

        _dataDirectory = Path.Combine(_environment.ContentRootPath, "Data");
        if (!Directory.Exists(_dataDirectory))
        {
            Directory.CreateDirectory(_dataDirectory);
        }

        _mediaDirectory = Path.Combine(_environment.WebRootPath, "media");
        if (!Directory.Exists(_mediaDirectory))
        {
            Directory.CreateDirectory(_mediaDirectory);
        }

        _activeDataFilePath = Path.Combine(_dataDirectory, "active-issues.json");
        _metadataFilePath = Path.Combine(_dataDirectory, "metadata.json");

        InitializeData();
    }

    public Task<List<SheetIssue>> GetAllIssuesAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(new List<SheetIssue>(_cachedIssues));
        }
    }

    public Task<SheetIssue?> GetIssueByIdAsync(string id)
    {
        lock (_lock)
        {
            var issue = _cachedIssues.FirstOrDefault(i =>
                string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.Key, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(issue);
        }
    }

    public Task<List<string>> GetAllStatusesAsync()
    {
        lock (_lock)
        {
            // Standard order for modern workflows (Jira Scrum/Kanban, QA, and task boards)
            var preferredOrder = new[] { 
                "Backlog", "To Do", "Open", "Selected for Development", 
                "In Progress", "In Review", "Testing", "Ready for QA", 
                "Merged", "Done", "Tested and Closed", "Closed", "Archived" 
            };

            var presentStatuses = _cachedIssues
                .Select(i => string.IsNullOrWhiteSpace(i.Status) ? "Open" : i.Status.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!presentStatuses.Any())
            {
                presentStatuses = new List<string> { "Open", "Ready for QA", "Tested and Closed" };
            }

            var ordered = preferredOrder
                .Where(s => presentStatuses.Any(p => string.Equals(p, s, StringComparison.OrdinalIgnoreCase)))
                .Select(s => presentStatuses.First(p => string.Equals(p, s, StringComparison.OrdinalIgnoreCase)))
                .Concat(presentStatuses.Where(p => !preferredOrder.Any(s => string.Equals(s, p, StringComparison.OrdinalIgnoreCase))))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult(ordered);
        }
    }

    public Task<List<string>> GetAllAssigneesAsync()
    {
        lock (_lock)
        {
            var assignees = _cachedIssues
                .Select(i => string.IsNullOrWhiteSpace(i.Assignee) ? "Unassigned" : i.Assignee.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a == "Unassigned" ? "zzzz" : a)
                .ToList();

            return Task.FromResult(assignees);
        }
    }

    public Task<List<string>> GetAllPrioritiesAsync()
    {
        lock (_lock)
        {
            var preferred = new List<string> { "Critical", "Major", "Minor", "Highest", "High", "Medium", "Low", "Lowest" };
            var present = _cachedIssues
                .Select(i => i.Priority)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var ordered = preferred.Where(p => present.Contains(p, StringComparer.OrdinalIgnoreCase))
                .Concat(present.Where(p => !preferred.Contains(p, StringComparer.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!ordered.Any())
            {
                ordered = new List<string> { "Critical", "Major", "Minor" };
            }

            return Task.FromResult(ordered);
        }
    }

    public Task<List<string>> GetAllIssueTypesAsync()
    {
        lock (_lock)
        {
            var types = _cachedIssues
                .Select(i => string.IsNullOrWhiteSpace(i.IssueType) ? "Bug" : i.IssueType.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t)
                .ToList();

            if (!types.Any())
            {
                types = new List<string> { "Bug", "Task", "Story", "Epic" };
            }

            return Task.FromResult(types);
        }
    }

    public Task<List<string>> GetAllColumnNamesAsync()
    {
        lock (_lock)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Key", "Title", "Status", "Priority", "Assignee", "IssueType", "Photos", "CreatedDate"
            };

            foreach (var issue in _cachedIssues)
            {
                foreach (var k in issue.CustomFields.Keys)
                {
                    columns.Add(k);
                }
            }

            return Task.FromResult(columns.ToList());
        }
    }

    public string GetCurrentSourceName() => _sourceName;
    public DateTime GetLastUpdated() => _lastUpdated;
    public string? GetConfiguredUrl() => _configuredUrl ?? _configuration["GoogleSheet:SheetUrl"];

    public Task<(bool Success, string Message, int Count)> ImportCsvAsync(string csvContent, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return Task.FromResult((false, "CSV content is empty.", 0));
        }

        try
        {
            var parsed = ParseCsvToIssues(csvContent);
            if (!parsed.Any())
            {
                return Task.FromResult((false, "No valid records could be parsed from the provided data.", 0));
            }

            lock (_lock)
            {
                _cachedIssues = parsed;
                _sourceName = sourceName;
                _lastUpdated = DateTime.Now;
                SaveToDisk();
            }

            return Task.FromResult((true, $"Successfully loaded {parsed.Count} live issues with {parsed.Count(p => p.HasPhoto)} photos.", parsed.Count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse CSV data");
            return Task.FromResult((false, $"Parsing error: {ex.Message}", 0));
        }
    }

    public Task<(bool Success, string Message, int Count)> ImportXlsxAsync(Stream stream, string sourceName)
    {
        try
        {
            var parsed = ParseXlsxToIssues(stream);
            if (!parsed.Any())
            {
                return Task.FromResult((false, "No valid records could be parsed from the Excel file.", 0));
            }

            lock (_lock)
            {
                _cachedIssues = parsed;
                _sourceName = sourceName;
                _lastUpdated = DateTime.Now;
                SaveToDisk();
            }

            return Task.FromResult((true, $"Successfully loaded {parsed.Count} live issues with {parsed.Count(p => p.HasPhoto)} photos from Excel.", parsed.Count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Excel file");
            return Task.FromResult((false, $"Excel parsing error: {ex.Message}", 0));
        }
    }

    public async Task<(bool Success, string Message, int Count)> ImportFromUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return (false, "URL is required.", 0);
        }

        try
        {
            var candidateUrls = GetCandidateDownloadUrls(url.Trim());
            var cookieContainer = new CookieContainer();
            using var handler = new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 JiraSheetViewer/1.0");

            byte[]? downloadedBytes = null;
            string? usedUrl = null;
            string? lastError = null;

            foreach (var exportUrl in candidateUrls)
            {
                try
                {
                    _logger.LogInformation("Attempting to fetch sheet from: {ExportUrl}", exportUrl);
                    var response = await client.GetAsync(exportUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        if (bytes.Length > 0)
                        {
                            // Check if returned HTML error page
                            var headerSnippet = Encoding.UTF8.GetString(bytes.Take(Math.Min(bytes.Length, 300)).ToArray());
                            if (!headerSnippet.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) &&
                                !headerSnippet.Contains("<html", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadedBytes = bytes;
                                usedUrl = exportUrl;
                                break;
                            }
                            else
                            {
                                lastError = "HTML login page returned instead of raw data.";
                            }
                        }
                    }
                    else
                    {
                        lastError = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            if (downloadedBytes == null)
            {
                return (false, $"Unable to fetch sheet data ({lastError ?? "Access restricted"}). Please ensure the file or Google Sheet has public view access.", 0);
            }

            // Detect binary XLSX (PK..) or CSV text
            if (downloadedBytes.Length >= 4 &&
                downloadedBytes[0] == 0x50 && downloadedBytes[1] == 0x4B &&
                downloadedBytes[2] == 0x03 && downloadedBytes[3] == 0x04)
            {
                // Save locally to Data/sheet.xlsx
                var localXlsx = Path.Combine(_dataDirectory, "sheet.xlsx");
                await File.WriteAllBytesAsync(localXlsx, downloadedBytes);

                using var ms = new MemoryStream(downloadedBytes);
                var sheetName = ExtractSheetNameFromUrl(url) ?? "Live Excel Sheet";
                var result = await ImportXlsxAsync(ms, sheetName);
                if (result.Success)
                {
                    _configuredUrl = url.Trim();
                    SaveToDisk();
                }
                return result;
            }
            else
            {
                var text = Encoding.UTF8.GetString(downloadedBytes);
                var sheetName = ExtractSheetNameFromUrl(url) ?? "Live CSV Sheet";
                var result = await ImportCsvAsync(text, sheetName);
                if (result.Success)
                {
                    _configuredUrl = url.Trim();
                    SaveToDisk();
                }
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import from URL {Url}", url);
            return (false, $"Error fetching URL: {ex.Message}", 0);
        }
    }

    public Task ResetToSampleDataAsync()
    {
        // Re-parse Data/sheet.xlsx if available, otherwise clear
        var userXlsxPath = Path.Combine(_dataDirectory, "sheet.xlsx");
        if (File.Exists(userXlsxPath))
        {
            using var stream = File.OpenRead(userXlsxPath);
            var parsed = ParseXlsxToIssues(stream);
            if (parsed.Any())
            {
                lock (_lock)
                {
                    _cachedIssues = parsed;
                    _sourceName = "Data/sheet.xlsx";
                    _lastUpdated = File.GetLastWriteTime(userXlsxPath);
                    SaveToDisk();
                }
                return Task.CompletedTask;
            }
        }

        lock (_lock)
        {
            _cachedIssues = new List<SheetIssue>();
            _sourceName = "Empty (Awaiting Sheet Connection)";
            _lastUpdated = DateTime.Now;
            SaveToDisk();
        }
        return Task.CompletedTask;
    }

    private void InitializeData()
    {
        lock (_lock)
        {
            try
            {
                // Check metadata
                if (File.Exists(_metadataFilePath))
                {
                    var meta = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_metadataFilePath));
                    if (meta != null)
                    {
                        if (meta.TryGetValue("sourceName", out var sName)) _sourceName = sName;
                        if (meta.TryGetValue("configuredUrl", out var cUrl)) _configuredUrl = cUrl;
                    }
                }

                // If not in metadata, check appsettings.json
                var configUrl = _configuration["GoogleSheet:SheetUrl"];
                if (string.IsNullOrWhiteSpace(_configuredUrl) && !string.IsNullOrWhiteSpace(configUrl))
                {
                    _configuredUrl = configUrl.Trim();
                }

                // Priority 1: Check if user has sheet.xlsx in Data/ (Real live data)
                var userXlsxPath = Path.Combine(_dataDirectory, "sheet.xlsx");
                if (File.Exists(userXlsxPath))
                {
                    using var stream = File.OpenRead(userXlsxPath);
                    var parsed = ParseXlsxToIssues(stream);
                    if (parsed.Any())
                    {
                        _cachedIssues = parsed;
                        _sourceName = "Live Bugs Sheet (sheet.xlsx)";
                        _lastUpdated = File.GetLastWriteTime(userXlsxPath);
                        SaveToDisk();
                        return;
                    }
                }

                // Priority 2: Check if user placed sheet.csv in Data/
                var userCsvPath = Path.Combine(_dataDirectory, "sheet.csv");
                if (File.Exists(userCsvPath))
                {
                    var text = File.ReadAllText(userCsvPath);
                    var parsed = ParseCsvToIssues(text);
                    if (parsed.Any())
                    {
                        _cachedIssues = parsed;
                        _sourceName = "Data/sheet.csv";
                        _lastUpdated = File.GetLastWriteTime(userCsvPath);
                        SaveToDisk();
                        return;
                    }
                }

                // Priority 3: Check active-issues.json
                if (File.Exists(_activeDataFilePath))
                {
                    var json = File.ReadAllText(_activeDataFilePath);
                    var list = JsonSerializer.Deserialize<List<SheetIssue>>(json);
                    if (list != null && list.Any())
                    {
                        _cachedIssues = list;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load live issues from disk");
            }

            _cachedIssues = new List<SheetIssue>();
            _sourceName = "Live Sheet (No Data Loaded)";
            _lastUpdated = DateTime.Now;
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(_cachedIssues, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_activeDataFilePath, json, Encoding.UTF8);

            var meta = new Dictionary<string, string>
            {
                ["sourceName"] = _sourceName,
                ["lastUpdated"] = _lastUpdated.ToString("o"),
                ["configuredUrl"] = _configuredUrl ?? ""
            };
            File.WriteAllText(_metadataFilePath, JsonSerializer.Serialize(meta), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save active issues to disk");
        }
    }

    public List<SheetIssue> ParseCsvToIssues(string text)
    {
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text.Substring(1);
        }

        char delimiter = ',';
        var firstLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (firstLine.Count(c => c == '\t') > firstLine.Count(c => c == ','))
        {
            delimiter = '\t';
        }

        var rows = ParseCsvRows(text, delimiter);
        return ParseRowsToIssues(rows, new Dictionary<int, List<string>>());
    }

    public List<SheetIssue> ParseXlsxToIssues(Stream stream)
    {
        var rowToImages = new Dictionary<int, List<string>>();
        var rows = ParseXlsxRowsAndExtractMedia(stream, rowToImages);
        return ParseRowsToIssues(rows, rowToImages);
    }

    private List<List<string>> ParseXlsxRowsAndExtractMedia(Stream stream, Dictionary<int, List<string>> rowToImages)
    {
        var rows = new List<List<string>>();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        // 1. Extract embedded media files (screenshots) to wwwroot/media/
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(entry.FullName);
                var destPath = Path.Combine(_mediaDirectory, fileName);
                if (!File.Exists(destPath) || new FileInfo(destPath).Length == 0)
                {
                    try
                    {
                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                    catch { }
                }
            }
        }

        // 2. Parse drawings relationship to map rId to image filename
        var rIdToFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relsEntry = archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels");
        if (relsEntry != null)
        {
            using var relsStream = relsEntry.Open();
            var xdocRels = XDocument.Load(relsStream);
            XNamespace nsRels = xdocRels.Root?.GetDefaultNamespace() ?? XNamespace.None;
            foreach (var rel in xdocRels.Descendants(nsRels + "Relationship"))
            {
                var id = (string?)rel.Attribute("Id");
                var target = (string?)rel.Attribute("Target");
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                {
                    rIdToFileName[id] = Path.GetFileName(target);
                }
            }
        }

        // 3. Parse drawing1.xml to map row index to image filenames
        var drawEntry = archive.GetEntry("xl/drawings/drawing1.xml");
        if (drawEntry != null)
        {
            using var drawStream = drawEntry.Open();
            var xdocDraw = XDocument.Load(drawStream);
            XNamespace xdrNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
            XNamespace aNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            foreach (var anchor in xdocDraw.Descendants(xdrNs + "twoCellAnchor"))
            {
                var fromRowEl = anchor.Element(xdrNs + "from")?.Element(xdrNs + "row");
                var blipEl = anchor.Descendants(aNs + "blip").FirstOrDefault();

                if (fromRowEl != null && int.TryParse(fromRowEl.Value, out var rowIdx) && blipEl != null)
                {
                    var embedId = (string?)blipEl.Attribute(rNs + "embed");
                    if (!string.IsNullOrEmpty(embedId) && rIdToFileName.TryGetValue(embedId, out var imgFileName))
                    {
                        if (!rowToImages.ContainsKey(rowIdx))
                        {
                            rowToImages[rowIdx] = new List<string>();
                        }
                        rowToImages[rowIdx].Add($"/media/{imgFileName}");
                    }
                }
            }
        }

        // 4. Read shared strings
        var sharedStrings = new List<string>();
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry != null)
        {
            using var entryStream = sharedStringsEntry.Open();
            var xdoc = XDocument.Load(entryStream);
            XNamespace ns = xdoc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            foreach (var si in xdoc.Descendants(ns + "si"))
            {
                var text = string.Concat(si.Descendants(ns + "t").Select(t => t.Value));
                sharedStrings.Add(text);
            }
        }

        // 5. Read first worksheet (e.g. Bugs_List)
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml") ??
                         archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));

        if (sheetEntry == null) return rows;

        using (var sheetStream = sheetEntry.Open())
        {
            var xdoc = XDocument.Load(sheetStream);
            XNamespace ns = xdoc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var sheetData = xdoc.Descendants(ns + "sheetData").FirstOrDefault();
            if (sheetData == null) return rows;

            foreach (var rowEl in sheetData.Elements(ns + "row"))
            {
                var rowCells = new List<string>();
                int currentColIdx = 0;

                foreach (var c in rowEl.Elements(ns + "c"))
                {
                    var cellRef = (string?)c.Attribute("r") ?? "";
                    var cellColIdx = GetColumnIndexFromRef(cellRef);

                    while (currentColIdx < cellColIdx)
                    {
                        rowCells.Add("");
                        currentColIdx++;
                    }

                    var tAttr = (string?)c.Attribute("t");
                    var vEl = c.Element(ns + "v");
                    string cellValue = "";

                    if (tAttr == "s" && vEl != null && int.TryParse(vEl.Value, out var sIdx) && sIdx >= 0 && sIdx < sharedStrings.Count)
                    {
                        cellValue = sharedStrings[sIdx];
                    }
                    else if (tAttr == "inlineStr")
                    {
                        var isEl = c.Element(ns + "is");
                        cellValue = isEl?.Element(ns + "t")?.Value ?? "";
                    }
                    else if (vEl != null)
                    {
                        cellValue = vEl.Value;
                    }

                    rowCells.Add(cellValue);
                    currentColIdx++;
                }

                if (rowCells.Any(val => !string.IsNullOrWhiteSpace(val)))
                {
                    rows.Add(rowCells);
                }
            }
        }

        return rows;
    }

    private static int GetColumnIndexFromRef(string cellRef)
    {
        int col = 0;
        foreach (char ch in cellRef)
        {
            if (char.IsLetter(ch))
            {
                col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            else
            {
                break;
            }
        }
        return Math.Max(0, col - 1);
    }

    public List<SheetIssue> ParseRowsToIssues(List<List<string>> rows, Dictionary<int, List<string>> rowToImages)
    {
        if (rows.Count < 2)
        {
            return new List<SheetIssue>();
        }

        var headers = rows[0].Select(h => h.Trim()).ToList();
        var issues = new List<SheetIssue>();

        bool isBugSheet = headers.Any(h => h.Contains("bug", StringComparison.OrdinalIgnoreCase));
        string defaultPrefix = isBugSheet ? "BUG" : "TASK";

        // Heuristics for standard fields across any generic spreadsheet
        int keyIndex = FindColumnIndex(headers, "key", "issue key", "ticket", "id", "task id", "item id", "bug no.", "bug no", "no.", "no", "#", "code", "number");
        int titleIndex = FindColumnIndex(headers, "title", "summary", "task", "task name", "bug - title", "bug title", "name", "item", "subject", "feature", "topic", "headline");
        int descIndex = FindColumnIndex(headers, "description", "details", "notes", "remarks", "content", "desc", "body");
        int statusIndex = FindColumnIndex(headers, "status", "qa status", "developer status", "dev status", "state", "stage", "column", "progress", "phase", "condition");
        int priorityIndex = FindColumnIndex(headers, "priority", "critical", "severity", "urgency", "importance", "level", "rank", "impact");
        int assigneeIndex = FindColumnIndex(headers, "assignee", "assigned to", "developer", "owner", "lead", "person", "member", "resource", "assigned", "who");
        int typeIndex = FindColumnIndex(headers, "type", "issue type", "work item type", "kind", "category");
        int moduleIndex = FindColumnIndex(headers, "module", "feature/module", "feature", "category", "component", "tags", "tag", "label", "labels", "epic");
        int stepsIndex = FindColumnIndex(headers, "steps to reproduce", "steps", "reproduction");
        int actualIndex = FindColumnIndex(headers, "actual result", "actual");
        int expectedIndex = FindColumnIndex(headers, "expected result", "expected");
        int commentsIndex = FindColumnIndex(headers, "comments", "comment");
        int dueDateIndex = FindColumnIndex(headers, "due date", "due", "deadline", "target date", "end date");
        int createdDateIndex = FindColumnIndex(headers, "created date", "created", "date created", "timestamp", "date");

        // Photo columns
        var photoIndices = new List<int>();
        for (int i = 0; i < headers.Count; i++)
        {
            var h = headers[i].ToLowerInvariant();
            if (h.Contains("photo") || h.Contains("image") || h.Contains("picture") ||
                h.Contains("screenshot") || h.Contains("attachment"))
            {
                photoIndices.Add(i);
            }
        }

        int rowNum = 1;
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var issue = new SheetIssue
            {
                Id = $"ISSUE-{rowNum}",
                IssueType = isBugSheet ? "Bug" : "Task"
            };

            // Key
            if (keyIndex >= 0 && keyIndex < row.Count && !string.IsNullOrWhiteSpace(row[keyIndex]))
            {
                var rawKey = row[keyIndex].Trim();
                issue.Key = rawKey.Contains('-') ? rawKey : $"{defaultPrefix}-{rawKey}";
            }
            else
            {
                issue.Key = $"{defaultPrefix}-{rowNum}";
            }

            // Issue Type
            if (typeIndex >= 0 && typeIndex < row.Count && !string.IsNullOrWhiteSpace(row[typeIndex]))
            {
                issue.IssueType = NormalizeIssueType(row[typeIndex].Trim());
            }

            // Title
            if (titleIndex >= 0 && titleIndex < row.Count && !string.IsNullOrWhiteSpace(row[titleIndex]))
            {
                issue.Title = row[titleIndex].Trim();
            }
            else
            {
                issue.Title = row.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && !c.StartsWith("http")) ?? $"Item #{rowNum}";
            }

            // Description
            if (descIndex >= 0 && descIndex < row.Count)
            {
                issue.Description = row[descIndex].Trim();
            }

            // Status
            if (statusIndex >= 0 && statusIndex < row.Count && !string.IsNullOrWhiteSpace(row[statusIndex]))
            {
                issue.Status = NormalizeStatus(row[statusIndex].Trim());
            }
            else
            {
                issue.Status = "To Do";
            }

            // Priority
            if (priorityIndex >= 0 && priorityIndex < row.Count && !string.IsNullOrWhiteSpace(row[priorityIndex]))
            {
                issue.Priority = NormalizePriority(row[priorityIndex].Trim());
            }
            else
            {
                issue.Priority = "Medium";
            }

            // Assignee
            if (assigneeIndex >= 0 && assigneeIndex < row.Count && !string.IsNullOrWhiteSpace(row[assigneeIndex]))
            {
                issue.Assignee = row[assigneeIndex].Trim();
            }
            else
            {
                issue.Assignee = "Unassigned";
            }

            // Tags (Module)
            if (moduleIndex >= 0 && moduleIndex < row.Count && !string.IsNullOrWhiteSpace(row[moduleIndex]))
            {
                var mod = row[moduleIndex].Trim();
                issue.Tags.Add(mod);
            }

            // Due Date
            if (dueDateIndex >= 0 && dueDateIndex < row.Count && DateTime.TryParse(row[dueDateIndex].Trim(), out var parsedDue))
            {
                issue.DueDate = parsedDue;
            }

            // Created Date
            if (createdDateIndex >= 0 && createdDateIndex < row.Count && DateTime.TryParse(row[createdDateIndex].Trim(), out var parsedCreated))
            {
                issue.CreatedDate = parsedCreated;
            }

            // Attached Photos from Excel Drawings
            var photosList = new List<string>();
            if (rowToImages.TryGetValue(r, out var drawingPhotos))
            {
                photosList.AddRange(drawingPhotos);
            }

            // Also check photo columns for URLs
            foreach (var pIdx in photoIndices)
            {
                if (pIdx < row.Count && !string.IsNullOrWhiteSpace(row[pIdx]))
                {
                    ExtractUrlsFromText(row[pIdx], photosList);
                }
            }

            issue.Photos = photosList.Distinct().ToList();

            // Populate CustomFields for ALL columns so none of the sheet's data is lost
            for (int col = 0; col < headers.Count && col < row.Count; col++)
            {
                var hName = headers[col];
                var val = row[col];
                if (!string.IsNullOrWhiteSpace(hName) && !string.IsNullOrWhiteSpace(val))
                {
                    issue.CustomFields[hName] = val.Trim();
                }
            }

            issues.Add(issue);
            rowNum++;
        }

        return issues;
    }

    private static List<List<string>> ParseCsvRows(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        currentCell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentCell.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                }
                else if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                }
                else if (c == '\n')
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                }
                else
                {
                    currentCell.Append(c);
                }
            }
        }

        if (currentCell.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentCell.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    private static int FindColumnIndex(List<string> headers, params string[] candidates)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            var headerClean = headers[i].Trim().ToLowerInvariant();
            foreach (var cand in candidates)
            {
                if (headerClean == cand || headerClean.Replace("_", " ").Replace("-", " ") == cand)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private static void ExtractUrlsFromText(string text, List<string> output)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var parts = text.Split(new[] { '\r', '\n', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var trimmed = p.Trim('"', '\'', ' ', '<', '>');
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("/media/", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("/images/", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(trimmed, @"\.(jpg|jpeg|png|webp|gif|svg)(\?.*)?$", RegexOptions.IgnoreCase))
            {
                output.Add(trimmed);
            }
        }
    }

    private static string NormalizeStatus(string status)
    {
        var trimmed = status?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed)) return "To Do";
        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("tested") && lower.Contains("closed")) return "Tested and Closed";
        if (lower.Contains("ready") && lower.Contains("qa")) return "Ready for QA";
        if (lower == "reopen" || lower.Contains("re-open")) return "Reopen";
        if (lower == "merged") return "Merged";
        if (lower == "open") return "Open";
        if (lower == "to do" || lower == "todo" || lower == "backlog") return "To Do";
        if (lower.Contains("progress") || lower == "wip" || lower.Contains("doing") || lower.Contains("develop")) return "In Progress";
        if (lower.Contains("review") || lower.Contains("test")) return "In Review";
        if (lower == "done" || lower == "completed" || lower == "finished" || lower == "resolved") return "Done";
        if (lower == "closed") return "Closed";
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant());
    }

    private static string NormalizePriority(string priority)
    {
        var trimmed = priority?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed)) return "Medium";
        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("critical") || lower.Contains("highest") || lower == "blocker" || lower == "p1" || lower == "urgent") return "Critical";
        if (lower.Contains("major") || lower.Contains("high") || lower == "p2") return "High";
        if (lower.Contains("medium") || lower == "normal" || lower == "p3") return "Medium";
        if (lower.Contains("minor") || lower.Contains("low") || lower == "p4") return "Low";
        if (lower.Contains("trivial") || lower.Contains("lowest") || lower == "p5") return "Lowest";
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower);
    }

    private static string NormalizeIssueType(string type)
    {
        var trimmed = type?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed)) return "Task";
        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("bug") || lower.Contains("defect") || lower.Contains("error")) return "Bug";
        if (lower.Contains("story") || lower.Contains("user story")) return "Story";
        if (lower.Contains("epic") || lower.Contains("initiative")) return "Epic";
        return "Task";
    }

    public static List<string> GetCandidateDownloadUrls(string url)
    {
        var urls = new List<string>();
        url = url.Trim();

        // 1. SharePoint / OneDrive / Office 365
        if (url.Contains("sharepoint.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("1drv.ms", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("onedrive.live.com", StringComparison.OrdinalIgnoreCase))
        {
            if (url.Contains("?"))
            {
                urls.Add(Regex.Replace(url, @"\?.*$", "?download=1"));
                urls.Add(url + "&download=1");
            }
            else
            {
                urls.Add(url + "?download=1");
            }
        }

        // 2. Google Sheets
        if (url.Contains("docs.google.com/spreadsheets", StringComparison.OrdinalIgnoreCase))
        {
            var matchGid = Regex.Match(url, @"[#&?]gid=([0-9]+)");
            var gidParam = matchGid.Success ? $"&gid={matchGid.Groups[1].Value}" : "&gid=0";

            // Check if published to web link: /d/e/2PACX.../
            var matchPublished = Regex.Match(url, @"docs\.google\.com/spreadsheets/d/e/([a-zA-Z0-9-_]+)");
            if (matchPublished.Success)
            {
                var pubToken = matchPublished.Groups[1].Value;
                urls.Add($"https://docs.google.com/spreadsheets/d/e/{pubToken}/pub?output=csv{gidParam}");
                urls.Add($"https://docs.google.com/spreadsheets/d/e/{pubToken}/pub?gid=0&single=true&output=csv");
            }

            // Standard Google Sheet link: /d/{sheetId}/
            var matchId = Regex.Match(url, @"docs\.google\.com/spreadsheets/d/([a-zA-Z0-9-_]+)");
            if (matchId.Success && !url.Contains("/d/e/"))
            {
                var sheetId = matchId.Groups[1].Value;
                urls.Add($"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=csv{gidParam}");
                urls.Add($"https://docs.google.com/spreadsheets/d/{sheetId}/gviz/tq?tqx=out:csv{gidParam}");
                urls.Add($"https://docs.google.com/spreadsheets/d/{sheetId}/pub?output=csv{gidParam}");
            }

            if (url.Contains("/pubhtml", StringComparison.OrdinalIgnoreCase))
            {
                urls.Add(url.Replace("/pubhtml", "/pub?output=csv"));
            }
        }

        // 3. Google Drive file link
        if (url.Contains("drive.google.com/file/d/", StringComparison.OrdinalIgnoreCase))
        {
            var matchDrive = Regex.Match(url, @"drive\.google\.com/file/d/([a-zA-Z0-9-_]+)");
            if (matchDrive.Success)
            {
                var fileId = matchDrive.Groups[1].Value;
                urls.Add($"https://drive.google.com/uc?export=download&id={fileId}");
            }
        }

        // 4. Dropbox link
        if (url.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase))
        {
            if (url.Contains("dl=0"))
            {
                urls.Add(url.Replace("dl=0", "dl=1"));
            }
            else if (!url.Contains("dl=1"))
            {
                urls.Add(url.Contains("?") ? url + "&dl=1" : url + "?dl=1");
            }
        }

        urls.Add(url);
        return urls.Distinct().ToList();
    }

    private static string? ExtractSheetNameFromUrl(string url)
    {
        if (url.Contains("docs.google.com/spreadsheets", StringComparison.OrdinalIgnoreCase))
        {
            return "Connected Google Sheet";
        }
        if (url.Contains("sharepoint.com", StringComparison.OrdinalIgnoreCase) || url.Contains("onedrive", StringComparison.OrdinalIgnoreCase))
        {
            return "Connected Excel Online Sheet";
        }
        if (url.Contains(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return "Connected CSV Sheet";
        }
        if (url.Contains(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return "Connected Excel Sheet";
        }
        return "Connected Cloud Sheet";
    }
}
