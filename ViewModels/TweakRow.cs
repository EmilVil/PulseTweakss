using Pulse.Models;

namespace Pulse.ViewModels;

public sealed class TweakRow : Bindable
{
    private TweakOption? target;
    private bool staged;

    public TweakRow(ITweak tweak, Detection state)
    {
        Tweak = tweak;
        State = state;
        Choices = state.Options.Where(option => option.Value != state.Current?.Value).ToArray();
        target = Choices.FirstOrDefault();
    }

    public ITweak Tweak { get; }
    public TweakInfo Info => Tweak.Info;
    public Detection State { get; }
    public IReadOnlyList<TweakOption> Choices { get; }
    public bool HasChoices => Choices.Count > 1;
    public bool HasFixedTarget => CanStage && !HasChoices;
    public bool CanStage => State.Current is not null && Choices.Count > 0;
    public string CurrentLabel => State.Current?.Label ?? "Unavailable";
    public bool IsModerate => Info.Risk == Risk.Moderate;
    public bool IsUnavailable => State.Current is null;
    public string? UnavailableReason => State.UnavailableReason;
    public string RiskLabel => !Info.Reversible ? "Moderate risk · No PULSE undo" : IsModerate ? "Moderate risk" : "Low risk";

    public TweakOption? Target
    {
        get => target;
        set
        {
            if (value is null || !Choices.Contains(value) || !Set(ref target, value))
                return;
            if (Staged)
                Changed?.Invoke(this);
        }
    }

    public bool Staged
    {
        get => staged;
        set
        {
            if (value && !CanStage)
                return;
            if (Set(ref staged, value))
                Changed?.Invoke(this);
        }
    }

    public event Action<TweakRow>? Changed;

    public PlannedChange CreatePlan() => new(Tweak, State.Current!, Target!);

    public bool StageValue(string? value)
    {
        var option = Choices.FirstOrDefault(choice => choice.Value == value);
        if (option is null)
            return false;
        Target = option;
        Staged = true;
        return true;
    }
}

public sealed class NavRow(Page page, string icon) : Bindable
{
    private bool active;
    public Page Page { get; } = page;
    public string Label => Page.ToString();
    public string Icon { get; } = icon;
    public bool Active { get => active; set => Set(ref active, value); }
}

public sealed record ProfileRow(Profile Profile, string Name, string Description);
public sealed record PreviewRow(string Name, string Transition, string Impact, string RiskLabel);
public sealed record HistoryRow(ChangeRecord Change, bool CanRevert)
{
    public string Name => Change.Info.Name;
    public string Transition => $"{Change.Before.Label}  →  {Change.After.Label}";
    public string Date => Change.At.ToLocalTime().ToString("g");
    public string Status => Change.Status switch
    {
        ChangeStatus.ReviewRequired or ChangeStatus.Pending or ChangeStatus.Reverting => "Review required",
        _ => Change.Status.ToString()
    };
    public string? Detail => Change.Detail ??
        (Change.Status is ChangeStatus.Pending or ChangeStatus.Reverting ? "The operation was interrupted. Verify the current setting before retrying." : null);
}

public interface IDialogs
{
    bool Confirm(string title, string description, string confirm, IReadOnlyList<PreviewRow> rows);
    string? PickExportPath();
}
