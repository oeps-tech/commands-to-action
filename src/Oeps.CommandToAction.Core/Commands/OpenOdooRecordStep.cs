using System.Globalization;

namespace Oeps.CommandToAction.Core.Commands;

public sealed record OpenOdooRecordStep : ActionStep
{
    public Uri Url { get; }

    private OpenOdooRecordStep(string model, int recordId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordId);
        var id = recordId.ToString(CultureInfo.InvariantCulture);
        Url = new Uri($"https://oeps.thinkopen.solutions/odoo/{model}/{id}");
    }

    public static OpenOdooRecordStep SalesOrder(int id) => new("sale.order", id);
    public static OpenOdooRecordStep OutgoingDelivery(int id) => new("stock.picking", id);
}
