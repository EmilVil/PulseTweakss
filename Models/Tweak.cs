namespace Pulse.Models;

public enum Page { Overview, Optimization, Debloat, AppInstaller, Gaming, Privacy, Customization, Advanced, Restore, Settings }
public enum Profile { Custom, Safe, Balanced, Gaming }
public enum Risk { Low, Moderate }
public enum ChangeStatus { Pending, Applied, Failed, ReviewRequired, Reverting, Reverted }

public sealed record TweakOption(string? Value, string Label);
public sealed record TweakInfo(
    string Id, Page Page, string Name, string Description, Risk Risk, string Compatibility,
    string Impact, bool Reversible = true);

public sealed record Detection(TweakOption? Current, IReadOnlyList<TweakOption> Options, string? UnavailableReason)
{
    public static Detection Available(TweakOption current, params TweakOption[] options) => new(current, options, null);
    public static Detection Unavailable(string reason) => new(null, [], reason);
}

public interface ITweak
{
    TweakInfo Info { get; }
    Task<Detection> DetectAsync();
    Task WriteAsync(TweakOption target, TweakOption expected);
}

public sealed record PlannedChange(ITweak Tweak, TweakOption Before, TweakOption After);
public sealed record ChangeRecord(
    Guid Id, TweakInfo Info, TweakOption Before, TweakOption After, DateTimeOffset At,
    ChangeStatus Status, string? Detail = null, DateTimeOffset? RevertedAt = null);

public sealed record Preferences(Profile Profile = Profile.Custom, bool LiveMonitoring = true, bool AnimatePages = true);
public sealed record SettingLink(string Name, string Description, string Uri, string Action = "Open settings");
