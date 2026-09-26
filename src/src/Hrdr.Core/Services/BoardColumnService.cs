using Hrdr.Core.Data;
using Hrdr.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hrdr.Core.Services;

public record ReorderBoardColumnsRequest(IReadOnlyList<string> OrderedStates);

public class BoardColumnService(HrdrDbContext db)
{
    public const string ColumnOrderKey = "BoardColumnOrder";

    public async Task<IReadOnlyList<WorkItemState>> GetOrderAsync(CancellationToken ct = default)
    {
        var setting = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == ColumnOrderKey, ct);

        if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
            return WorkItemStateHelpers.BoardOrder;

        if (!TryParseOrder(setting.Value, out var order))
            return WorkItemStateHelpers.BoardOrder;

        return order;
    }

    public async Task<IReadOnlyList<WorkItemState>> ReorderAsync(
        ReorderBoardColumnsRequest request,
        CancellationToken ct = default)
    {
        var orderedStates = ParseAndValidate(request.OrderedStates);

        var value = string.Join(',', orderedStates.Select(s => s.ToString()));
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == ColumnOrderKey, ct);
        if (setting is null)
        {
            db.AppSettings.Add(new AppSetting { Key = ColumnOrderKey, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(ct);
        return orderedStates;
    }

    private static IReadOnlyList<WorkItemState> ParseAndValidate(IReadOnlyList<string> raw)
    {
        if (raw.Count == 0)
            throw new ArgumentException("OrderedStates must not be empty.", nameof(raw));

        var parsed = new List<WorkItemState>(raw.Count);
        foreach (var value in raw)
        {
            if (!WorkItemStateHelpers.TryParse(value, out var state))
                throw new ArgumentException($"Unknown state '{value}'.", nameof(raw));
            parsed.Add(state);
        }

        if (parsed.Distinct().Count() != parsed.Count)
            throw new ArgumentException("OrderedStates must be unique.", nameof(raw));

        var expected = WorkItemStateHelpers.BoardOrder;
        if (parsed.Count != expected.Count || expected.Any(s => !parsed.Contains(s)))
            throw new ArgumentException(
                "OrderedStates must include every board state exactly once.",
                nameof(raw));

        return parsed;
    }

    private static bool TryParseOrder(string value, out IReadOnlyList<WorkItemState> order)
    {
        order = [];
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            order = ParseAndValidate(parts);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
