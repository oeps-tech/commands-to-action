using System.Text.RegularExpressions;

namespace Oeps.CommandToAction.Core.Commands;

public enum ActionKey { Tab, Enter, Up, Right }
public abstract record ActionStep;
public sealed record TextStep(string Text) : ActionStep;
public sealed record KeyStep(ActionKey Key) : ActionStep;
public sealed record DelayStep(int Milliseconds) : ActionStep;

public sealed record ActionJob(
    ActionCommand Command,
    string Raw,
    ActionTimings Timings,
    IReadOnlyList<ActionStep> Steps);

public static partial class ActionPlanner
{
    // \z is intentional: '$' would incorrectly accept a trailing newline.
    [GeneratedRegex(@"\A(?:OEPS-[A-Za-z0-9]{4}|[A-Za-z0-9]{4}(?:[0-9]{8}|[0-9]{6}))\z", RegexOptions.CultureInvariant)]
    private static partial Regex OdooLabelPattern();

    public static bool IsValidOdooLabel(string? raw) => raw is not null && OdooLabelPattern().IsMatch(raw);

    public static bool TryCreate(ActionCommand command, string raw, ActionTimings timings, out ActionJob? job)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(timings);
        timings.Validate();
        job = null;
        if (command == ActionCommand.Disable) return false;

        var steps = new List<ActionStep>();
        void Text(string text) => steps.Add(new TextStep(text));
        void Key(ActionKey key) => steps.Add(new KeyStep(key));
        void Delay(int milliseconds) => steps.Add(new DelayStep(milliseconds));
        void TabThenWait() { Key(ActionKey.Tab); Delay(timings.TabMs); }

        switch (command)
        {
            case ActionCommand.OdooIn:
            case ActionCommand.OdooInMoves:
            case ActionCommand.OdooOutMoves:
                if (!IsValidOdooLabel(raw)) return false;
                bool productOnly = raw.StartsWith("OEPS-", StringComparison.Ordinal);
                Text(productOnly ? raw : raw[..4]);
                Delay(timings.InitMs);
                if (command == ActionCommand.OdooIn)
                {
                    Key(ActionKey.Tab);
                    break;
                }
                TabThenWait();
                if (command == ActionCommand.OdooOutMoves && !productOnly)
                    TabThenWait();
                if (!productOnly)
                {
                    Text(raw);
                    Delay(timings.SnMs);
                }
                int remainingTabs = command == ActionCommand.OdooInMoves ? 3 : productOnly ? 2 : 1;
                for (int i = 0; i < remainingTabs; i++) TabThenWait();
                Text("1");
                Delay(timings.CharMs);
                Key(ActionKey.Enter);
                break;
            case ActionCommand.AddEnterAndUp:
                Text(raw);
                Key(ActionKey.Enter);
                Key(ActionKey.Up);
                break;
            case ActionCommand.AddRight:
                Text(raw);
                Key(ActionKey.Right);
                break;
            case ActionCommand.Direct:
                Text(raw);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }

        job = new ActionJob(command, raw, timings with { }, steps.AsReadOnly());
        return true;
    }
}
