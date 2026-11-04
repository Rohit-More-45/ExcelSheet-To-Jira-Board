using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JiraSheetBoard.Models;

public class SheetIssue
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Key { get; set; } = "KAN-001";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "To Do";
    public string Priority { get; set; } = "Medium"; // Highest, High, Medium, Low, Lowest
    public string Assignee { get; set; } = "Unassigned";
    public string AssigneeAvatar { get; set; } = "";
    public string Reporter { get; set; } = "";
    public string IssueType { get; set; } = "Task"; // Story, Bug, Task, Epic
    public List<string> Photos { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    [JsonConverter(typeof(TolerantDateTimeConverter))]
    public DateTime? DueDate { get; set; }

    [JsonConverter(typeof(TolerantDateTimeConverter))]
    public DateTime? CreatedDate { get; set; }

    public string StoryPoints { get; set; } = "";

    /// <summary>
    /// Holds all arbitrary other columns from the Google Sheet
    /// </summary>
    public Dictionary<string, string> CustomFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string PrimaryPhoto => Photos.FirstOrDefault() ?? "";
    public int PhotoCount => Photos.Count;
    public bool HasPhoto => Photos.Count > 0;
}

public class TolerantDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            var num = reader.GetDouble();
            if (num > 0 && num < 100000)
            {
                try { return DateTime.FromOADate(num); } catch { }
            }
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str)) return null;

            if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
                DateTime.TryParse(str, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
            {
                return dt;
            }

            if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var oaDate) && oaDate > 0 && oaDate < 100000)
            {
                try { return DateTime.FromOADate(oaDate); } catch { }
            }

            var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "yyyy/MM/dd" };
            if (DateTime.TryParseExact(str, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                return dt;
            }
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString("o"));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

