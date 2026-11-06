using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
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
    private readonly string _dataDirectory;
    private readonly string _mediaDirectory;

    private List<SheetIssue> _cachedIssues = new();
    private Dictionary<string, List<SheetIssue>> _workbookSheets = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _availableSheetNames = new();
    private string? _currentSheetName;
    private string _sourceName = "No Sheet Connected";
    private string? _configuredUrl;
    private DateTime _lastUpdated = DateTime.Now;
    private readonly object _lock = new();

    public SheetDataService(
        IWebHostEnvironment environment,
        ILogger<SheetDataService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _environment = environment;
        _logger = logger;
        _httpClientFactory = httpClientFactory;

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

    public Task<List<string>> GetAvailableSheetsAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(new List<string>(_availableSheetNames));
        }
    }

    public Task<string?> GetCurrentSheetNameAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(_currentSheetName);
        }
    }

    public Task<Dictionary<string, int>> GetSheetCountsAsync()
    {
        lock (_lock)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in _availableSheetNames)
            {
                if (_workbookSheets.TryGetValue(s, out var issues))
                {
                    dict[s] = issues.Count;
                }
                else
                {
                    dict[s] = 0;
                }
            }
            return Task.FromResult(dict);
        }
    }

    public Task<bool> SelectSheetAsync(string sheetName)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(sheetName) && _workbookSheets.TryGetValue(sheetName, out var issues))
            {
                _currentSheetName = sheetName;
                _cachedIssues = issues;
                var baseSource = _sourceName.Contains('(') ? _sourceName.Substring(0, _sourceName.IndexOf('(')).Trim() : _sourceName;
                _sourceName = $"{baseSource} ({sheetName})";
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
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
    public string? GetConfiguredUrl() => _configuredUrl;

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
                _workbookSheets = new Dictionary<string, List<SheetIssue>>(StringComparer.OrdinalIgnoreCase)
                {
                    [sourceName] = parsed
                };
                _availableSheetNames = new List<string> { sourceName };
                _currentSheetName = sourceName;
                _sourceName = sourceName;
                _lastUpdated = DateTime.Now;
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
            var parsedSheets = ParseAllXlsxSheets(stream, out var orderedSheetNames);
            if (!parsedSheets.Any())
            {
                return Task.FromResult((false, "No valid records could be parsed from the Excel file.", 0));
            }

            // Default to 1st sheet as requested: "by default open 1st and add dropdown for selecting another sheets"
            var firstSheet = orderedSheetNames.FirstOrDefault(name => parsedSheets.ContainsKey(name)) 
                             ?? parsedSheets.Keys.First();
            var activeIssues = parsedSheets[firstSheet];

            lock (_lock)
            {
                _workbookSheets = parsedSheets;
                _availableSheetNames = orderedSheetNames.Where(n => parsedSheets.ContainsKey(n)).ToList();
                _currentSheetName = firstSheet;
                _cachedIssues = activeIssues;
                _sourceName = $"{sourceName} ({firstSheet})";
                _lastUpdated = DateTime.Now;
            }

            return Task.FromResult((true, $"Successfully loaded '{firstSheet}' with {activeIssues.Count} issues and {activeIssues.Count(p => p.HasPhoto)} photos ({parsedSheets.Count} subsheets available).", activeIssues.Count));
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

        string? downloadedFilePath = null;
        string? usedUrl = null;
        string? lastError = null;

        try
        {
            var candidateUrls = GetCandidateDownloadUrls(url.Trim());

            // 1. For SharePoint / OneDrive or on Windows: Use native curl with cookie jar FIRST
            // This natively handles SharePoint FedAuth cookies on relative redirects and large files
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (var exportUrl in candidateUrls)
                {
                    _logger.LogInformation("Attempting download via curl: {ExportUrl}", exportUrl);
                    var tempFile = TryDownloadWithCurlToFile(exportUrl, timeoutSeconds: 120);
                    if (tempFile != null)
                    {
                        if (IsValidSpreadsheetFile(tempFile, out var isHtml))
                        {
                            downloadedFilePath = tempFile;
                            usedUrl = exportUrl;
                            break;
                        }
                        else
                        {
                            if (isHtml) lastError = "HTML login page returned instead of raw data.";
                            try { File.Delete(tempFile); } catch { }
                        }
                    }
                }
            }

            // 2. Fallback to .NET HttpClient if curl not available or failed
            if (downloadedFilePath == null)
            {
                var cookieContainer = new CookieContainer();
                using var handler = new HttpClientHandler
                {
                    CookieContainer = cookieContainer,
                    AllowAutoRedirect = true,
                    MaxAutomaticRedirections = 10,
                    AutomaticDecompression = DecompressionMethods.All
                };

                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromMinutes(3);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,text/csv,*/*;q=0.8");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

                foreach (var exportUrl in candidateUrls)
                {
                    try
                    {
                        _logger.LogInformation("Attempting download via HttpClient: {ExportUrl}", exportUrl);
                        using var response = await client.GetAsync(exportUrl, HttpCompletionOption.ResponseHeadersRead);
                        if (response.IsSuccessStatusCode)
                        {
                            var tempFile = Path.Combine(Path.GetTempPath(), $"jira_sheet_{Guid.NewGuid():N}.tmp");
                            using (var fileStream = File.Create(tempFile))
                            {
                                await response.Content.CopyToAsync(fileStream);
                            }

                            if (IsValidSpreadsheetFile(tempFile, out var isHtml))
                            {
                                downloadedFilePath = tempFile;
                                usedUrl = exportUrl;
                                break;
                            }
                            else
                            {
                                if (isHtml) lastError = "HTML login page returned instead of raw data.";
                                try { File.Delete(tempFile); } catch { }
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
            }

            if (downloadedFilePath == null)
            {
                // If user pasted a browser viewer link (/:x:/r/ or /Documents/), explain clearly why it can't be fetched anonymously
                if (url.Contains("/:x:/r/", StringComparison.OrdinalIgnoreCase) ||
                    url.Contains("/:w:/r/", StringComparison.OrdinalIgnoreCase) ||
                    (url.Contains("/personal/", StringComparison.OrdinalIgnoreCase) && url.Contains("/Documents/", StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, "This is an internal browser viewer URL (copied from your browser address bar), which is restricted to your private browser login session. To open it: paste the original Share link (e.g. with '/:x:/g/'), or click the 'Upload File' tab and drag & drop the downloaded .xlsx file directly.", 0);
                }

                var isAuthGate = lastError != null && (
                    lastError.Contains("HTML login page", StringComparison.OrdinalIgnoreCase) ||
                    lastError.Contains("401", StringComparison.OrdinalIgnoreCase) ||
                    lastError.Contains("403", StringComparison.OrdinalIgnoreCase)
                );

                if (isAuthGate)
                {
                    return (false, "This sheet is locked behind corporate Single Sign-On (Microsoft / Google login). To open it right now: Download the file from your browser (File > Save As > Download a Copy) and upload the .xlsx file here, or change the link sharing to 'Anyone with the link can view'.", 0);
                }

                return (false, $"Unable to fetch sheet data ({lastError ?? "Access restricted"}). Please ensure the link is publicly accessible or upload your .xlsx file directly.", 0);
            }

            // Inspect downloaded file and import
            (bool Success, string Message, int Count) result;
            var isXlsx = false;

            using (var fs = File.OpenRead(downloadedFilePath))
            {
                byte[] header = new byte[4];
                int read = fs.Read(header, 0, 4);
                if (read == 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
                {
                    isXlsx = true;
                }
            }

            var sheetName = ExtractSheetNameFromUrl(url) ?? "Excel Online Sheet";

            if (isXlsx)
            {
                using var fs = File.OpenRead(downloadedFilePath);
                result = await ImportXlsxAsync(fs, sheetName);
            }
            else
            {
                var text = await File.ReadAllTextAsync(downloadedFilePath, Encoding.UTF8);
                result = await ImportCsvAsync(text, sheetName);
            }

            try { File.Delete(downloadedFilePath); } catch { }

            if (result.Success)
            {
                lock (_lock)
                {
                    _configuredUrl = url.Trim();
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import from URL {Url}", url);
            try { if (downloadedFilePath != null && File.Exists(downloadedFilePath)) File.Delete(downloadedFilePath); } catch { }
            return (false, $"Error fetching URL: {ex.Message}", 0);
        }
    }

    private static bool IsValidSpreadsheetFile(string filePath, out bool isHtml)
    {
        isHtml = false;
        if (!File.Exists(filePath)) return false;

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length < 10) return false;

        using var fs = File.OpenRead(filePath);
        byte[] header = new byte[Math.Min(fs.Length, 2048)];
        int read = fs.Read(header, 0, header.Length);
        if (read < 4) return false;

        // Binary XLSX/ZIP check (PK\x03\x04 or PK..)
        if (header[0] == 0x50 && header[1] == 0x4B)
        {
            return true;
        }

        // Text check for HTML login or viewer pages
        var snippet = Encoding.UTF8.GetString(header, 0, read);
        if (snippet.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("<head", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("Federated Redirect", StringComparison.OrdinalIgnoreCase))
        {
            isHtml = true;
            return false;
        }

        // Must NOT be an HTTP error response
        if (snippet.StartsWith("404", StringComparison.OrdinalIgnoreCase) ||
            snippet.StartsWith("403", StringComparison.OrdinalIgnoreCase) ||
            snippet.StartsWith("401", StringComparison.OrdinalIgnoreCase) ||
            snippet.StartsWith("500", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("FILE NOT FOUND", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("Access denied", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // CSV should contain at least one newline, comma, tab, or semicolon
        if (!snippet.Contains('\n') && !snippet.Contains('\r') &&
            !snippet.Contains(',') && !snippet.Contains('\t') && !snippet.Contains(';'))
        {
            return false;
        }

        return true;
    }

    private string? TryDownloadWithCurlToFile(string exportUrl, int timeoutSeconds = 120)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"jira_sheet_{Guid.NewGuid():N}.tmp");
        var cookieFile = Path.Combine(Path.GetTempPath(), $"jira_cookie_{Guid.NewGuid():N}.txt");

        try
        {
            try { File.WriteAllText(cookieFile, ""); } catch { }

            var startInfo = new ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = $"-L -s -k -f --max-time {timeoutSeconds} -c \"{cookieFile}\" -b \"{cookieFile}\" -A \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36\" -o \"{tempFile}\" \"{exportUrl}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };

            using var proc = Process.Start(startInfo);
            if (proc != null)
            {
                var stderrTask = proc.StandardError.ReadToEndAsync();
                bool exited = proc.WaitForExit((timeoutSeconds + 5) * 1000);
                if (!exited)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                }

                try { if (File.Exists(cookieFile)) File.Delete(cookieFile); } catch { }

                if (proc.HasExited && proc.ExitCode == 0 && File.Exists(tempFile))
                {
                    var fileInfo = new FileInfo(tempFile);
                    if (fileInfo.Length > 0)
                    {
                        return tempFile;
                    }
                }
                else
                {
                    var stderr = stderrTask.IsCompleted ? stderrTask.Result : "";
                    _logger.LogWarning("Curl download returned code {ExitCode} for {Url}. Stderr: {Stderr}",
                        proc.HasExited ? proc.ExitCode : -1, exportUrl, stderr);
                }
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Curl download failed for {Url}", exportUrl);
            try { if (File.Exists(cookieFile)) File.Delete(cookieFile); } catch { }
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
        return null;
    }

    public Task ResetToSampleDataAsync()
    {
        lock (_lock)
        {
            _cachedIssues = new List<SheetIssue>();
            _workbookSheets = new Dictionary<string, List<SheetIssue>>(StringComparer.OrdinalIgnoreCase);
            _availableSheetNames = new List<string>();
            _currentSheetName = null;
            _sourceName = "No Sheet Connected";
            _configuredUrl = null;
            _lastUpdated = DateTime.Now;
        }
        return Task.CompletedTask;
    }

    private void InitializeData()
    {
        lock (_lock)
        {
            _cachedIssues = new List<SheetIssue>();
            _workbookSheets = new Dictionary<string, List<SheetIssue>>(StringComparer.OrdinalIgnoreCase);
            _availableSheetNames = new List<string>();
            _currentSheetName = null;
            _sourceName = "No Sheet Connected";
            _configuredUrl = null;
            _lastUpdated = DateTime.Now;
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
        var sheets = ParseAllXlsxSheets(stream, out var orderedNames);
        if (orderedNames.Any() && sheets.TryGetValue(orderedNames[0], out var firstIssues))
        {
            return firstIssues;
        }
        return sheets.Values.FirstOrDefault() ?? new List<SheetIssue>();
    }

    public Dictionary<string, List<SheetIssue>> ParseAllXlsxSheets(Stream stream, out List<string> orderedSheetNames)
    {
        var result = new Dictionary<string, List<SheetIssue>>(StringComparer.OrdinalIgnoreCase);
        orderedSheetNames = new List<string>();

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

        // 2. Read shared strings
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

        // 3. Map workbook rId to target worksheet file from xl/_rels/workbook.xml.rels
        var wbRels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var wbRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (wbRelsEntry != null)
        {
            using var s = wbRelsEntry.Open();
            var xdoc = XDocument.Load(s);
            XNamespace ns = xdoc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            foreach (var rel in xdoc.Descendants(ns + "Relationship"))
            {
                var id = (string?)rel.Attribute("Id");
                var target = (string?)rel.Attribute("Target");
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                {
                    wbRels[id] = target;
                }
            }
        }

        // 4. Read sheets from xl/workbook.xml in their exact tab display order
        var sheetsInfo = new List<(string Name, string Target)>();
        var wbEntry = archive.GetEntry("xl/workbook.xml");
        if (wbEntry != null)
        {
            using var s = wbEntry.Open();
            var xdoc = XDocument.Load(s);
            XNamespace ns = xdoc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            XNamespace rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            foreach (var sheetEl in xdoc.Descendants(ns + "sheet"))
            {
                var name = (string?)sheetEl.Attribute("name");
                var rId = (string?)sheetEl.Attribute(rNs + "id");
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(rId) && wbRels.TryGetValue(rId, out var target))
                {
                    var fullTarget = target.StartsWith("xl/") ? target : "xl/" + target.TrimStart('/');
                    sheetsInfo.Add((name, fullTarget));
                    orderedSheetNames.Add(name);
                }
            }
        }

        // Fallback if workbook.xml had no recognized sheets
        if (sheetsInfo.Count == 0)
        {
            foreach (var e in archive.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                var simpleName = Path.GetFileNameWithoutExtension(e.FullName);
                sheetsInfo.Add((simpleName, e.FullName));
                orderedSheetNames.Add(simpleName);
            }
        }

        // 5. Parse each worksheet and its own dedicated drawings
        foreach (var sheet in sheetsInfo)
        {
            var rowToImages = new Dictionary<int, List<string>>();

            // Find drawing for this specific worksheet
            var wsRelPath = GetRelationshipPath(sheet.Target);
            var wsRelEntry = archive.GetEntry(wsRelPath);
            string? drawingPath = null;

            if (wsRelEntry != null)
            {
                using var s = wsRelEntry.Open();
                var xdoc = XDocument.Load(s);
                XNamespace ns = xdoc.Root?.GetDefaultNamespace() ?? XNamespace.None;
                var drawingRel = xdoc.Descendants(ns + "Relationship")
                    .FirstOrDefault(r => ((string?)r.Attribute("Type"))?.EndsWith("/drawing", StringComparison.OrdinalIgnoreCase) == true);

                if (drawingRel != null)
                {
                    var rawTarget = (string?)drawingRel.Attribute("Target");
                    if (!string.IsNullOrEmpty(rawTarget))
                    {
                        drawingPath = ResolveRelativePath(sheet.Target, rawTarget);
                    }
                }
            }

            // If this sheet has a drawing file, parse its rels and anchors
            if (!string.IsNullOrEmpty(drawingPath))
            {
                var drRelPath = GetRelationshipPath(drawingPath);
                var drRelEntry = archive.GetEntry(drRelPath);
                var drRels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (drRelEntry != null)
                {
                    using var s = drRelEntry.Open();
                    var xdoc = XDocument.Load(s);
                    XNamespace ns = xdoc.Root?.GetDefaultNamespace() ?? XNamespace.None;
                    foreach (var rel in xdoc.Descendants(ns + "Relationship"))
                    {
                        var id = (string?)rel.Attribute("Id");
                        var target = (string?)rel.Attribute("Target");
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                        {
                            drRels[id] = Path.GetFileName(target);
                        }
                    }
                }

                var drEntry = archive.GetEntry(drawingPath);
                if (drEntry != null)
                {
                    using var s = drEntry.Open();
                    var xdoc = XDocument.Load(s);
                    XNamespace xdrNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
                    XNamespace aNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
                    XNamespace rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

                    // Support both twoCellAnchor and oneCellAnchor
                    var anchors = xdoc.Descendants(xdrNs + "twoCellAnchor")
                        .Concat(xdoc.Descendants(xdrNs + "oneCellAnchor"));

                    foreach (var anchor in anchors)
                    {
                        var fromRowEl = anchor.Element(xdrNs + "from")?.Element(xdrNs + "row");
                        var blipEl = anchor.Descendants(aNs + "blip").FirstOrDefault();

                        if (fromRowEl != null && int.TryParse(fromRowEl.Value, out var fromRowIdx) && blipEl != null)
                        {
                            // OpenXML drawing row is 0-based. Excel row is 1-based (fromRowIdx + 1).
                            int excelRowNum = fromRowIdx + 1;
                            var embedId = (string?)blipEl.Attribute(rNs + "embed");
                            if (!string.IsNullOrEmpty(embedId) && drRels.TryGetValue(embedId, out var imgFileName))
                            {
                                if (!rowToImages.ContainsKey(excelRowNum))
                                {
                                    rowToImages[excelRowNum] = new List<string>();
                                }
                                var mediaUrl = $"/media/{imgFileName}";
                                if (!rowToImages[excelRowNum].Contains(mediaUrl))
                                {
                                    rowToImages[excelRowNum].Add(mediaUrl);
                                }
                            }
                        }
                    }
                }
            }

            // Parse worksheet rows with exact ExcelRowNum
            var sheetEntry = archive.GetEntry(sheet.Target);
            if (sheetEntry == null) continue;

            var parsedRows = ParseWorksheetRows(sheetEntry, sharedStrings);
            var issues = ParseRowsToIssuesWithExcelRowNumbers(parsedRows, rowToImages, sheet.Name);
            if (issues.Count > 0)
            {
                result[sheet.Name] = issues;
            }
        }

        return result;
    }

    private List<ParsedSheetRow> ParseWorksheetRows(ZipArchiveEntry sheetEntry, List<string> sharedStrings)
    {
        var rows = new List<ParsedSheetRow>();
        using var sheetStream = sheetEntry.Open();
        var xdoc = XDocument.Load(sheetStream);
        XNamespace ns = xdoc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        var sheetData = xdoc.Descendants(ns + "sheetData").FirstOrDefault();
        if (sheetData == null) return rows;

        int defaultRowNum = 1;
        foreach (var rowEl in sheetData.Elements(ns + "row"))
        {
            int excelRowNum = int.TryParse((string?)rowEl.Attribute("r"), out var parsedR) ? parsedR : defaultRowNum;
            defaultRowNum = excelRowNum + 1;

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
                rows.Add(new ParsedSheetRow
                {
                    ExcelRowNum = excelRowNum,
                    Cells = rowCells
                });
            }
        }

        return rows;
    }

    private static string GetRelationshipPath(string partPath)
    {
        var dir = Path.GetDirectoryName(partPath)?.Replace('\\', '/') ?? "";
        var fileName = Path.GetFileName(partPath);
        return string.IsNullOrEmpty(dir) ? $"_rels/{fileName}.rels" : $"{dir}/_rels/{fileName}.rels";
    }

    private static string ResolveRelativePath(string basePath, string relativeTarget)
    {
        var baseDir = Path.GetDirectoryName(basePath)?.Replace('\\', '/') ?? "";
        var combined = Path.Combine(baseDir, relativeTarget).Replace('\\', '/');
        var parts = combined.Split('/');
        var stack = new List<string>();
        foreach (var p in parts)
        {
            if (p == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
            else if (p != "." && !string.IsNullOrEmpty(p)) { stack.Add(p); }
        }
        return string.Join('/', stack);
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
        var parsedRows = new List<ParsedSheetRow>();
        for (int i = 0; i < rows.Count; i++)
        {
            parsedRows.Add(new ParsedSheetRow
            {
                ExcelRowNum = i + 1,
                Cells = rows[i]
            });
        }
        return ParseRowsToIssuesWithExcelRowNumbers(parsedRows, rowToImages, "Sheet 1");
    }

    public List<SheetIssue> ParseRowsToIssuesWithExcelRowNumbers(
        List<ParsedSheetRow> parsedRows, 
        Dictionary<int, List<string>> rowToImages,
        string sheetName)
    {
        if (parsedRows == null || parsedRows.Count == 0)
        {
            return new List<SheetIssue>();
        }

        // Dynamically find the header row (usually row 0, but can be row 1 or 2 if there's a title block)
        int headerRowIdx = 0;
        int maxHeaderMatches = 0;

        for (int i = 0; i < Math.Min(parsedRows.Count, 5); i++)
        {
            int matches = 0;
            foreach (var cell in parsedRows[i].Cells)
            {
                var lower = cell?.Trim().ToLowerInvariant() ?? "";
                if (lower == "key" || lower == "title" || lower == "status" || lower == "priority" ||
                    lower == "assignee" || lower == "bug" || lower == "task" || lower == "id" ||
                    lower == "summary" || lower == "description" || lower == "module" || lower.Contains("status"))
                {
                    matches++;
                }
            }
            if (matches > maxHeaderMatches)
            {
                maxHeaderMatches = matches;
                headerRowIdx = i;
            }
        }

        var headers = parsedRows[headerRowIdx].Cells.Select(h => h?.Trim() ?? "").ToList();
        var issues = new List<SheetIssue>();

        bool isBugSheet = sheetName.Contains("bug", StringComparison.OrdinalIgnoreCase) ||
                          headers.Any(h => h.Contains("bug", StringComparison.OrdinalIgnoreCase));
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

        int startRow = headerRowIdx + 1;
        // If there's only 1 row total and it has no header matches, treat it as a single data row
        if (parsedRows.Count == 1 && maxHeaderMatches == 0)
        {
            startRow = 0;
        }

        int rowNum = 1;
        for (int r = startRow; r < parsedRows.Count; r++)
        {
            var rowObj = parsedRows[r];
            var row = rowObj.Cells;
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

            // Attached Photos from Excel Drawings:
            // MATCHED BY EXACT EXCEL ROW NUMBER (no shifting, no off-by-one errors)
            var photosList = new List<string>();
            if (rowToImages.TryGetValue(rowObj.ExcelRowNum, out var drawingPhotos))
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
            var variants = new List<string> { url };
            if (url.Contains("esluk_com", StringComparison.OrdinalIgnoreCase))
            {
                variants.Add(url.Replace("esluk_com", "esl-uk_com", StringComparison.OrdinalIgnoreCase));
            }

            foreach (var v in variants)
            {
                // A. Primary: Clean direct URL with ?download=1 (removes tracking rtime, cid, wdLOR, etc.)
                var cleanBase = Regex.Replace(v, @"\?.*$", "");
                urls.Add(cleanBase + "?download=1");

                // B. Direct URL with &download=1
                if (v.Contains("?"))
                {
                    urls.Add(v + "&download=1");
                }

                // C. If browser viewer /:[xwpb]:/r/ was pasted, strip reader to doc library
                if (Regex.IsMatch(v, @"/:[xwpb]:/r/", RegexOptions.IgnoreCase))
                {
                    var noReader = Regex.Replace(v, @"/:[xwpb]:/r/", "/", RegexOptions.IgnoreCase);
                    var cleanNoReader = Regex.Replace(noReader, @"\?.*$", "");
                    urls.Add(cleanNoReader + "?download=1");
                }
            }

            // Tertiary candidate: onedrive.live.com
            if (url.Contains("onedrive.live.com", StringComparison.OrdinalIgnoreCase))
            {
                urls.Add(url.Replace("/view.aspx", "/download").Replace("/edit.aspx", "/download"));
            }

            // Quaternary candidate: 1drv.ms short link
            if (url.Contains("1drv.ms", StringComparison.OrdinalIgnoreCase))
            {
                urls.Add(url.Contains("?") ? url + "&download=1" : url + "?download=1");
            }
        }

        // 2. Google Sheets
        if (url.Contains("docs.google.com/spreadsheets", StringComparison.OrdinalIgnoreCase))
        {
            var matchGid = Regex.Match(url, @"[#&?]gid=([0-9]+)");
            var gidParam = matchGid.Success ? $"&gid={matchGid.Groups[1].Value}" : "&gid=0";
            var gidValue = matchGid.Success ? matchGid.Groups[1].Value : "0";

            // Check if published to web link: /d/e/2PACX.../
            var matchPublished = Regex.Match(url, @"docs\.google\.com/spreadsheets/(?:u/[0-9]+/)?d/e/([a-zA-Z0-9-_]+)");
            if (matchPublished.Success)
            {
                var pubToken = matchPublished.Groups[1].Value;
                urls.Add($"https://docs.google.com/spreadsheets/d/e/{pubToken}/pub?output=xlsx{gidParam}");
                urls.Add($"https://docs.google.com/spreadsheets/d/e/{pubToken}/pub?output=csv{gidParam}");
                urls.Add($"https://docs.google.com/spreadsheets/d/e/{pubToken}/pub?gid={gidValue}&single=true&output=csv");
            }

            // Standard or multi-user Google Sheet link: /spreadsheets/d/{id}/ or /spreadsheets/u/0/d/{id}/
            var matchId = Regex.Match(url, @"docs\.google\.com/spreadsheets/(?:u/[0-9]+/)?d/([a-zA-Z0-9-_]+)");
            if (matchId.Success && !url.Contains("/d/e/"))
            {
                var sheetId = matchId.Groups[1].Value;
                // 1. XLSX export candidate
                urls.Add($"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=xlsx");
                // 2. CSV export candidate with gid
                urls.Add($"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=csv{gidParam}");
                // 3. Visualization API CSV candidate
                urls.Add($"https://docs.google.com/spreadsheets/d/{sheetId}/gviz/tq?tqx=out:csv{gidParam}");
                // 4. Published formats
                urls.Add($"https://docs.google.com/spreadsheets/d/{sheetId}/pub?output=xlsx{gidParam}");
                urls.Add($"https://docs.google.com/spreadsheets/d/{sheetId}/pub?output=csv{gidParam}");
            }

            if (url.Contains("/pubhtml", StringComparison.OrdinalIgnoreCase))
            {
                urls.Add(url.Replace("/pubhtml", "/pub?output=xlsx"));
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
                urls.Add($"https://drive.google.com/uc?export=download&confirm=t&id={fileId}");
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

        // Only include raw url if it is not a known interactive web viewer page
        bool isWebReader = url.Contains("/edit", StringComparison.OrdinalIgnoreCase) ||
                           url.Contains("/view", StringComparison.OrdinalIgnoreCase) ||
                           url.Contains("/:x:/r/", StringComparison.OrdinalIgnoreCase) ||
                           url.Contains("/:w:/r/", StringComparison.OrdinalIgnoreCase) ||
                           url.Contains("/pubhtml", StringComparison.OrdinalIgnoreCase);

        if (!isWebReader || urls.Count == 0)
        {
            urls.Add(url);
        }

        return urls.Distinct().ToList();
    }

    private static string? ExtractSheetNameFromUrl(string url)
    {
        var matchFilename = Regex.Match(url, @"/([^/?#]+\.(?:xlsx|xlsm|csv|tsv))(?=[?#]|$)", RegexOptions.IgnoreCase);
        if (matchFilename.Success)
        {
            return Uri.UnescapeDataString(matchFilename.Groups[1].Value);
        }

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
