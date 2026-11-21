using Pulse.Models;

namespace Pulse.Services;

public sealed class TweakEngine(JsonStore store)
{
    public async Task<ChangeRecord> ApplyAsync(PlannedChange plan)
    {
        using var journal = store.LockHistory();
        var history = store.ReadHistory();
        var live = await RequireCurrentAsync(plan.Tweak);
        if (live.Current!.Value != plan.Before.Value)
            throw new InvalidOperationException("Windows changed after preview. Refresh and review this change again.");

        var option = live.Options.FirstOrDefault(option => option.Value == plan.After.Value)
            ?? throw new InvalidOperationException("The requested setting is no longer available.");
        if (live.Current.Value == option.Value)
            throw new InvalidOperationException("The setting is already at the requested value.");

        var entry = new ChangeRecord(Guid.NewGuid(), plan.Tweak.Info, live.Current, option, DateTimeOffset.UtcNow, ChangeStatus.Pending);
        history.Add(entry);
        store.SaveHistory(history);
        return await CompleteAsync(history, history.Count - 1, plan.Tweak, reverting: false);
    }

    public async Task<ChangeRecord> RevertAsync(Guid id, ITweak tweak)
    {
        using var journal = store.LockHistory();
        var history = store.ReadHistory();
        var index = history.FindIndex(entry => entry.Id == id && entry.Info.Id == tweak.Info.Id);
        if (index < 0 || !tweak.Info.Reversible || !CanRevert(history, history[index]))
            throw new InvalidOperationException("This change is no longer eligible for revert.");

        var entry = history[index];
        var current = (await RequireCurrentAsync(tweak)).Current!;
        if (current.Value == entry.Before.Value)
        {
            var restored = entry with { Status = ChangeStatus.Reverted, Detail = "The original value was already present.", RevertedAt = DateTimeOffset.UtcNow };
            history[index] = restored;
            store.SaveHistory(history);
            return restored;
        }
        if (current.Value != entry.After.Value)
            throw new InvalidOperationException("The setting changed outside PULSE. Its current value was preserved.");

        history[index] = entry with { Status = ChangeStatus.Reverting, Detail = null };
        store.SaveHistory(history);
        return await CompleteAsync(history, index, tweak, reverting: true);
    }

    public static bool CanRevert(IReadOnlyList<ChangeRecord> history, ChangeRecord entry) =>
        entry.Info.Reversible && IsOpen(entry.Status) &&
        history.LastOrDefault(candidate => candidate.Info.Id == entry.Info.Id && IsOpen(candidate.Status))?.Id == entry.Id;

    private static bool IsOpen(ChangeStatus status) =>
        status is ChangeStatus.Applied or ChangeStatus.Pending or ChangeStatus.ReviewRequired or ChangeStatus.Reverting;

    private async Task<ChangeRecord> CompleteAsync(List<ChangeRecord> history, int index, ITweak tweak, bool reverting)
    {
        var entry = history[index];
        var target = reverting ? entry.Before : entry.After;
        var starting = reverting ? entry.After : entry.Before;
        string? detail = null;
        try { await tweak.WriteAsync(target, starting); }
        catch (Exception ex) { detail = ex.Message; }

        var result = ChangeStatus.ReviewRequired;
        try
        {
            var live = (await RequireCurrentAsync(tweak)).Current!;
            if (live.Value == target.Value)
                result = reverting ? ChangeStatus.Reverted : ChangeStatus.Applied;
            else if (live.Value == starting.Value)
                result = reverting ? ChangeStatus.Applied : ChangeStatus.Failed;

            if (live.Value != target.Value)
                detail ??= $"Windows reported {live.Label} after the operation.";
        }
        catch (Exception ex) { detail ??= ex.Message; }

        var updated = entry with
        {
            Status = result,
            Detail = detail,
            RevertedAt = result == ChangeStatus.Reverted ? DateTimeOffset.UtcNow : null
        };
        history[index] = updated;
        store.SaveHistory(history);
        return updated;
    }

    private static async Task<Detection> RequireCurrentAsync(ITweak tweak)
    {
        var state = await tweak.DetectAsync();
        return state.Current is not null
            ? state
            : throw new InvalidOperationException(state.UnavailableReason ?? "Windows did not report a current value.");
    }
}
