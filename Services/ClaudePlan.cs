using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DynamicIsland.Services;

/// <summary>
/// Claude Code's own task list, rebuilt from its PostToolUse hooks: TaskCreate / TaskUpdate (current Claude Code)
/// one task at a time, or TodoWrite (older, and with CLAUDE_CODE_ENABLE_TASKS=0) the whole list at once. The
/// newest session wins. Field names are read loosely: the hook sees what the model sent (taskId / id / task_id,
/// activeForm / active_form). Called from the bridge's thread only.
/// </summary>
public sealed class ClaudePlan
{
    public enum Status { Pending, InProgress, Completed }
    public sealed record Task(string Id, string Subject, string? ActiveForm, Status Status);
    public sealed record Snapshot(string Project, IReadOnlyList<Task> Tasks, DateTime At);

    private readonly List<Task> _tasks = [];
    private string _session = "", _project = "";

    /// <summary>A PostToolUse hook body; the plan after it, or null when it was not about the task list.</summary>
    public Snapshot? Apply(JsonObject json, string project)
    {
        var tool = Str(json["tool_name"]);
        if (tool is not ("TaskCreate" or "TaskUpdate" or "TodoWrite")) return null;
        var input = json["tool_input"] as JsonObject ?? new JsonObject();
        var session = Str(json["session_id"]) ?? "";
        if (session != _session) { _session = session; _tasks.Clear(); }
        _project = project;

        switch (tool)
        {
            case "TodoWrite":
                _tasks.Clear();
                int n = 0;
                foreach (var t in input["todos"] as JsonArray ?? [])
                {
                    if (t is not JsonObject o) continue;
                    var text = Str(o["content"]) ?? Str(o["subject"]);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    _tasks.Add(new((++n).ToString(), text, Str(o["activeForm"]) ?? Str(o["active_form"]), Parse(Str(o["status"]))));
                }
                break;

            case "TaskCreate":
            {
                var subject = Str(input["subject"]) ?? Str(input["content"]) ?? Str(input["description"]);
                if (string.IsNullOrWhiteSpace(subject)) return null;
                var id = NewId(json["tool_response"]) ?? (_tasks.Select(t => int.TryParse(t.Id, out var i) ? i : 0).DefaultIfEmpty(0).Max() + 1).ToString();
                _tasks.RemoveAll(t => t.Id == id);
                _tasks.Add(new(id, subject, Str(input["activeForm"]) ?? Str(input["active_form"]), Status.Pending));
                break;
            }

            case "TaskUpdate":
            {
                var id = Str(input["taskId"]) ?? Str(input["id"]) ?? Str(input["task_id"]);
                int at = _tasks.FindIndex(t => t.Id == id);
                if (at < 0) return null;
                var status = Str(input["status"]);
                if (status == "deleted") { _tasks.RemoveAt(at); break; }
                var t = _tasks[at];
                _tasks[at] = t with
                {
                    Subject = Str(input["subject"]) ?? t.Subject,
                    ActiveForm = Str(input["activeForm"]) ?? Str(input["active_form"]) ?? t.ActiveForm,
                    Status = status == null ? t.Status : Parse(status),
                };
                break;
            }
        }
        return new(_project, _tasks.ToList(), DateTime.Now);
    }

    private static Status Parse(string? s) => s switch
    {
        "in_progress" => Status.InProgress,
        "completed" => Status.Completed,
        _ => Status.Pending,
    };

    // the SDK documents { task: { id } }; a plain "Task #3 created" text is read too
    private static string? NewId(JsonNode? response)
    {
        if (response is JsonObject o)
            return Str((o["task"] as JsonObject)?["id"]) ?? Str(o["id"]) ?? Str(o["taskId"]) ?? NewId(o["content"] ?? o["text"]);
        var text = response is JsonValue ? Str(response) : response?.ToJsonString();
        var m = text == null ? null : Regex.Match(text, @"#(\d+)");
        return m is { Success: true } ? m.Groups[1].Value : null;
    }

    private static string? Str(JsonNode? n) => n is JsonValue v
        ? v.TryGetValue<string>(out var s) ? s : v.TryGetValue<long>(out var l) ? l.ToString() : null
        : null;
}
