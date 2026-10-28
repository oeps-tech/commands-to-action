using System.Text.Json;
using Oeps.CommandToAction.Core.Commands;

namespace Oeps.CommandToAction.Core.Scanning;

public enum DispatchResult { CommandSelected, Queued, Ignored, Invalid }

/// <summary>Routes SDK JSON in receive order; command and timing changes affect future readings only.</summary>
public sealed class LabelDispatcher(ActionQueue queue)
{
    private readonly object _gate = new();
    private ActionCommand _selectedCommand = ActionCommand.Disable;
    private ActionTimings _timings = new();
    public event Action<ActionCommand>? CommandChanged;
    public event Action<string>? Message;
    public ActionCommand SelectedCommand { get { lock (_gate) return _selectedCommand; } }
    public ActionTimings Timings
    {
        get { lock (_gate) return _timings; }
        set { value.Validate(); lock (_gate) _timings = value; }
    }

    public void SelectCommand(ActionCommand command)
    {
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        lock (_gate)
        {
            _selectedCommand = command;
            CommandChanged?.Invoke(command);
        }
    }

    public DispatchResult HandleJson(string json)
    {
        lock (_gate)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("label_version", out var version)) return DispatchResult.Invalid;
                if (version.ValueKind != JsonValueKind.String) return DispatchResult.Ignored;
                if (version.GetString() == "C")
                {
                    if (!root.TryGetProperty("command", out var command) || command.ValueKind != JsonValueKind.String) return DispatchResult.Invalid;
                    if (!CommandCatalog.TryParse(command.GetString()!, out var selected))
                    {
                        Message?.Invoke("Unknown command ignored. Current command unchanged.");
                        return DispatchResult.Ignored;
                    }
                    SelectCommand(selected);
                    Message?.Invoke("Command selected by scanner: " + command.GetString());
                    return DispatchResult.CommandSelected;
                }
                if (version.GetString() != "X") return DispatchResult.Ignored;
                if (!root.TryGetProperty("raw", out var raw) || raw.ValueKind != JsonValueKind.String) return DispatchResult.Invalid;
                if (_selectedCommand == ActionCommand.Disable) return DispatchResult.Ignored;
                if (!ActionPlanner.TryCreate(_selectedCommand, raw.GetString()!, _timings, out var job))
                {
                    Message?.Invoke("Label ignored: the format does not match this command.");
                    return DispatchResult.Invalid;
                }
                return queue.Enqueue(job!) ? DispatchResult.Queued : DispatchResult.Ignored;
            }
            catch (JsonException)
            {
                Message?.Invoke("Invalid scanner message ignored.");
                return DispatchResult.Invalid;
            }
        }
    }
}
