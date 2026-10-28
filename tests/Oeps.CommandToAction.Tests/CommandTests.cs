using Oeps.CommandToAction.Core.Commands;

namespace Oeps.CommandToAction.Tests;

public static class CommandTests
{
    [Test]
    public static void CatalogOrderAndExactCommandNames()
    {
        Assert.Equal("Odoo IN|Odoo IN Moves|Odoo OUT Moves|Add Enter and Up|Add Right|Direct|Disable",
            string.Join("|", CommandCatalog.All.Select(command => command.Name)));
        foreach (var command in CommandCatalog.All)
        {
            Assert.True(CommandCatalog.TryParse(command.Name, out var parsed));
            Assert.Equal(command.Command, parsed);
            Assert.True(command.Description.Length > 20);
        }
        Assert.False(CommandCatalog.TryParse("odoo in", out _));
        Assert.False(CommandCatalog.TryParse(" Direct", out _));
        Assert.False(CommandCatalog.TryParse("For Odoo OUT", out _));
        Assert.False(CommandCatalog.TryParse(null, out _));
    }

    [Test]
    public static void OdooValidationRequiresExactAsciiFormats()
    {
        foreach (var raw in new[] { "OEPS-Ab09", "OEPS-1234", "ABcd12345678", "aB09123456", "123412345678", "1234123456" })
            Assert.True(ActionPlanner.IsValidOdooLabel(raw), $"Rejected valid label: {raw}");
        foreach (var raw in new[] { "", "OEPS-ABC", "OEPS-ABCDE", "OEPS-ÄBCD", "oeps-ABCD", "OEPS-ABCD\n", "OEPS-ABCD\r\n", " OEPS-ABCD", "OEPS-ABCD ", "ABCD12345", "ABCD1234567", "ABCD123456789", "ABCD12345X", "ABCD１２３４５６", "AB_D123456", "prefixOEPS-ABCD", "ABCD123456\n" })
            Assert.False(ActionPlanner.IsValidOdooLabel(raw), $"Accepted invalid label: {raw}");
        Assert.False(ActionPlanner.IsValidOdooLabel(null));
    }

    [Test]
    public static void OdooInOnlyTypesProductThenTab()
    {
        Assert.Equal("text:OEPS-Ab09|wait:1000|Tab", Describe(ActionCommand.OdooIn, "OEPS-Ab09"));
        Assert.Equal("text:Ab09|wait:1000|Tab", Describe(ActionCommand.OdooIn, "Ab09123456"));
        Assert.Equal("text:Ab09|wait:1000|Tab", Describe(ActionCommand.OdooIn, "Ab0912345678"));
    }

    [Test]
    public static void IncomingMovesHaveFourTabsAndSerialInSecondField()
    {
        Assert.Equal("text:OEPS-Ab09|wait:1000|Tab|wait:200|Tab|wait:200|Tab|wait:200|Tab|wait:200|text:1|wait:100|Enter",
            Describe(ActionCommand.OdooInMoves, "OEPS-Ab09"));
        Assert.Equal("text:Ab09|wait:1000|Tab|wait:200|text:Ab09123456|wait:100|Tab|wait:200|Tab|wait:200|Tab|wait:200|text:1|wait:100|Enter",
            Describe(ActionCommand.OdooInMoves, "Ab09123456"));
    }

    [Test]
    public static void OutgoingMovesHaveThreeTabsAndSerialInThirdField()
    {
        Assert.Equal("text:OEPS-Ab09|wait:1000|Tab|wait:200|Tab|wait:200|Tab|wait:200|text:1|wait:100|Enter",
            Describe(ActionCommand.OdooOutMoves, "OEPS-Ab09"));
        Assert.Equal("text:Ab09|wait:1000|Tab|wait:200|Tab|wait:200|text:Ab0912345678|wait:100|Tab|wait:200|text:1|wait:100|Enter",
            Describe(ActionCommand.OdooOutMoves, "Ab0912345678"));
    }

    [Test]
    public static void GenericCommandsPreserveLiteralContentAndAppendOnlyRequestedKeys()
    {
        const string raw = "+^%{F4}  é漢🙂\t\r\n";
        Assert.Equal($"text:{raw}", Describe(ActionCommand.Direct, raw));
        Assert.Equal($"text:{raw}|Right", Describe(ActionCommand.AddRight, raw));
        Assert.Equal($"text:{raw}|Enter|Up", Describe(ActionCommand.AddEnterAndUp, raw));
        Assert.False(ActionPlanner.TryCreate(ActionCommand.Disable, raw, new(), out var disabled));
        Assert.True(disabled is null);
        Assert.False(ActionPlanner.TryCreate(ActionCommand.OdooIn, raw, new(), out var invalid));
        Assert.True(invalid is null);
    }

    [Test]
    public static void CustomTimingValuesAreCapturedAndValidated()
    {
        var timings = new ActionTimings { InitMs = 111, SnMs = 222, TabMs = 333, CharMs = 444, BetweenActionMs = 555 };
        Assert.True(ActionPlanner.TryCreate(ActionCommand.OdooInMoves, "Ab09123456", timings, out var job));
        Assert.Equal("111,333,222,333,333,333,444", string.Join(",", job!.Steps.OfType<DelayStep>().Select(step => step.Milliseconds)));
        Assert.Equal(555, job.Timings.BetweenActionMs);
        Assert.False(ReferenceEquals(timings, job.Timings));
        foreach (var badTimings in new[]
                 {
                     timings with { InitMs = -1 }, timings with { SnMs = 60001 }, timings with { TabMs = -1 },
                     timings with { CharMs = -1 }, timings with { BetweenActionMs = -1 }
                 })
            Assert.Throws<ArgumentOutOfRangeException>(badTimings.Validate);
        new ActionTimings { InitMs = 0, SnMs = 0, TabMs = 0, CharMs = 0, BetweenActionMs = 0 }.Validate();
    }

    private static string Describe(ActionCommand command, string raw)
    {
        Assert.True(ActionPlanner.TryCreate(command, raw, new(), out var job));
        return string.Join("|", job!.Steps.Select(step => step switch
        {
            TextStep text => "text:" + text.Text,
            KeyStep key => key.Key.ToString(),
            DelayStep delay => "wait:" + delay.Milliseconds,
            _ => throw new Exception("Unknown step")
        }));
    }
}
