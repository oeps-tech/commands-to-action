namespace Oeps.CommandToAction.Core.Commands;

public enum ActionCommand
{
    OdooIn,
    OdooInMoves,
    OdooOutMoves,
    AddEnterAndUp,
    AddRight,
    Direct,
    Disable
}

public sealed record CommandDescriptor(ActionCommand Command, string Name, string Description);

public static class CommandCatalog
{
    public static IReadOnlyList<CommandDescriptor> All { get; } = Array.AsReadOnly(new[]
    {
        new CommandDescriptor(ActionCommand.OdooIn, "Odoo IN",
            "Accepts OEPS-yyyy, yyyyxxxxxxxx or yyyyxxxxxx (y is an ASCII letter or number; x is a digit). Types the OEPS product code or four-character product prefix, waits INIT, then presses Tab."),
        new CommandDescriptor(ActionCommand.OdooInMoves, "Odoo IN Moves",
            "Accepts the same three Odoo formats. Types the product and, for serial/batch labels, the full label in the serial/batch field. Advances through the incoming-move fields, enters quantity 1, then presses Enter. Uses INIT, SN, TAB and CHAR delays."),
        new CommandDescriptor(ActionCommand.OdooOutMoves, "Odoo OUT Moves",
            "Accepts the same three Odoo formats. Types the product and, for serial/batch labels, the full label in the outgoing serial/batch field. Advances through the outgoing-move fields, enters quantity 1, then presses Enter. Uses INIT, SN, TAB and CHAR delays."),
        new CommandDescriptor(ActionCommand.AddEnterAndUp, "Add Enter and Up",
            "Types the raw label exactly, presses Enter, then presses the Up arrow."),
        new CommandDescriptor(ActionCommand.AddRight, "Add Right",
            "Types the raw label exactly, then presses the Right arrow."),
        new CommandDescriptor(ActionCommand.Direct, "Direct",
            "Types the raw label exactly, without adding any keys."),
        new CommandDescriptor(ActionCommand.Disable, "Disable",
            "Ignores incoming raw labels. Command labels can still select another command. Jobs already queued keep the command selected when they arrived.")
    });

    public static bool TryParse(string? name, out ActionCommand command)
    {
        foreach (var descriptor in All)
        {
            if (string.Equals(descriptor.Name, name, StringComparison.Ordinal))
            {
                command = descriptor.Command;
                return true;
            }
        }
        command = ActionCommand.Disable;
        return false;
    }

    public static CommandDescriptor Get(ActionCommand command) =>
        All.FirstOrDefault(item => item.Command == command)
        ?? throw new ArgumentOutOfRangeException(nameof(command));

    public static string GetHelpText(ActionCommand command)
    {
        var sequence = command switch
        {
            ActionCommand.OdooIn => """
                OEPS-yyyy:
                <label>  [tab]

                Serial / batch (yyyyxxxxxxxx or yyyyxxxxxx):
                <prefix>  [tab]

                """,
            ActionCommand.OdooInMoves => """
                OEPS-yyyy:
                <label>  [tab]  [tab]  [tab]  [tab]  '1'  <enter>

                Serial / batch (yyyyxxxxxxxx or yyyyxxxxxx):
                <prefix>  [tab]  <label>  [tab]  [tab]  [tab]  '1'  <enter>

                """,
            ActionCommand.OdooOutMoves => """
                OEPS-yyyy:
                <label>  [tab]  [tab]  [tab]  '1'  <enter>

                Serial / batch (yyyyxxxxxxxx or yyyyxxxxxx):
                <prefix>  [tab]  [tab]  <label>  [tab]  '1'  <enter>

                BETWEEN_ACTION: between completed actions.
                """,
            _ => ""
        };
        return Get(command).Description + (sequence.Length == 0 ? "" :
            "\n\n" + sequence.TrimEnd() + "\n\n<label> = full content; <prefix> = first four characters.")
            + "\n\nLabels already queued keep their original command and timings.";
    }
}
