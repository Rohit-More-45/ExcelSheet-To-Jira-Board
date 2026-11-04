using JiraSheetBoard.Models;

namespace JiraSheetBoard.Services;

public interface ISheetDataService
{
    Task<List<SheetIssue>> GetAllIssuesAsync();
    Task<SheetIssue?> GetIssueByIdAsync(string id);
    Task<List<string>> GetAllStatusesAsync();
    Task<List<string>> GetAllAssigneesAsync();
    Task<List<string>> GetAllPrioritiesAsync();
    Task<List<string>> GetAllIssueTypesAsync();
    Task<List<string>> GetAllColumnNamesAsync();
    Task<(bool Success, string Message, int Count)> ImportCsvAsync(string csvContent, string sourceName);
    Task<(bool Success, string Message, int Count)> ImportXlsxAsync(Stream stream, string sourceName);
    Task<(bool Success, string Message, int Count)> ImportFromUrlAsync(string url);
    Task ResetToSampleDataAsync();
    string GetCurrentSourceName();
    DateTime GetLastUpdated();
    string? GetConfiguredUrl();
}
