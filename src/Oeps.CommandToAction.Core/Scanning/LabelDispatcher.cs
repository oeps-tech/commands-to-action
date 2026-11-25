using System.Text.Json;
using Oeps.CommandToAction.Core.Commands;
using Oeps.CommandToAction.Core.Configuration;
using Scanner.Contracts;

namespace Oeps.CommandToAction.Core.Scanning;

public enum DispatchResult { CommandSelected, Queued, Ignored, Invalid }

/// <summary>Routes scanner labels in receive order using the settings in effect at receipt.</summary>
public sealed class LabelDispatcher
{
    private readonly ActionQueue _queue;
    private readonly object _gate = new();
    private ActionCommand _selectedCommand = ActionCommand.Disable;
    private ActionTimings _timings = new();
    private bool _openSalesOrders;
    private bool _openDeliveries;

    public LabelDispatcher(ActionQueue queue, UserSettings? settings = null)
    {
        _queue = queue;
        ApplySettings(settings ?? new UserSettings());
    }

    public void ApplySettings(UserSettings settings)
    {
        settings.Timings.Validate();
        lock (_gate)
        {
            _timings = settings.Timings;
            _openSalesOrders = settings.OpenSalesOrders;
            _openDeliveries = settings.OpenDeliveries;
        }
    }

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
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("label_version", out var version))
                return DispatchResult.Invalid;
            bool supported = version.ValueKind switch
            {
                JsonValueKind.String => version.GetString() is "C" or "X",
                JsonValueKind.Number => version.TryGetInt32(out var number) && number is 4 or 5,
                _ => false
            };
            return supported ? HandleLabel(PayloadValidator.ReadClientJson(json)) : DispatchResult.Ignored;
        }
        catch (JsonException)
        {
            Message?.Invoke("Invalid scanner message ignored.");
            return DispatchResult.Invalid;
        }
    }

    public DispatchResult HandleLabel(LabelData label)
    {
        lock (_gate)
        {
            switch (label.LabelVersion)
            {
                case 4 when _openSalesOrders:
                    if (label.SalesOrderId is not int orderId || orderId <= 0) return DispatchResult.Invalid;
                    return Enqueue(new ActionJob("Odoo sales order", _timings, [OpenOdooRecordStep.SalesOrder(orderId)]));
                case 5 when _openDeliveries:
                    if (label.OutId is not int outId || outId <= 0) return DispatchResult.Invalid;
                    return Enqueue(new ActionJob("Odoo outgoing delivery", _timings, [OpenOdooRecordStep.OutgoingDelivery(outId)]));
                case LabelData.CommandVersion:
                    if (!CommandCatalog.TryParse(label.Command, out var selected))
                    {
                        Message?.Invoke("Unknown command ignored. Current command unchanged.");
                        return DispatchResult.Ignored;
                    }
                    SelectCommand(selected);
                    Message?.Invoke("Command selected by scanner: " + label.Command);
                    return DispatchResult.CommandSelected;
                case LabelData.RawVersion when _selectedCommand != ActionCommand.Disable:
                    if (label.Raw is null || !ActionPlanner.TryCreate(_selectedCommand, label.Raw, _timings, out var job))
                    {
                        Message?.Invoke("Label ignored: the format does not match this command.");
                        return DispatchResult.Invalid;
                    }
                    return Enqueue(job!);
                default:
                    return DispatchResult.Ignored;
            }
        }
    }

    private DispatchResult Enqueue(ActionJob job) => _queue.Enqueue(job) ? DispatchResult.Queued : DispatchResult.Ignored;
}
