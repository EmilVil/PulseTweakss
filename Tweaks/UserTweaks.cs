using Microsoft.Win32;
using Pulse.Models;
using Pulse.Services;

namespace Pulse.Tweaks;

internal sealed class BooleanTweak(TweakInfo info, Func<bool> read, Action<bool> write) : ITweak
{
    private static readonly TweakOption On = new("1", "On");
    private static readonly TweakOption Off = new("0", "Off");

    public TweakInfo Info => info;
    public Task<Detection> DetectAsync() => Task.FromResult(Detection.Available(read() ? On : Off, On, Off));
    public Task WriteAsync(TweakOption target, TweakOption expected)
    {
        write(target.Value switch { "1" => true, "0" => false, _ => throw new ArgumentException("Invalid setting value.") });
        return Task.CompletedTask;
    }
}

internal sealed class DwordTweak(
    TweakInfo info, string path, string name, TweakOption on, TweakOption off,
    string defaultLabel, Func<string?> unavailable) : ITweak
{
    public TweakInfo Info => info;

    public Task<Detection> DetectAsync()
    {
        if (unavailable() is { } reason)
            return Task.FromResult(Detection.Unavailable(reason));

        using var key = Registry.CurrentUser.OpenSubKey(path);
        var value = key?.GetValue(name);
        return Task.FromResult(value switch
        {
            null => Detection.Available(new(null, defaultLabel), on, off),
            int number when number.ToString() == on.Value => Detection.Available(on, on, off),
            int number when number.ToString() == off.Value => Detection.Available(off, on, off),
            _ => Detection.Unavailable("Windows reported an unfamiliar value. Use Windows Settings to review it.")
        });
    }

    public Task WriteAsync(TweakOption target, TweakOption expected)
    {
        if (target.Value is null)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(path, writable: true);
            existing?.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            if (target.Value != on.Value && target.Value != off.Value)
                throw new ArgumentException("Invalid registry value.");
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue(name, int.Parse(target.Value), RegistryValueKind.DWord);
        }
        return Task.CompletedTask;
    }
}

public static class TweakCatalog
{
    public const string Animations = "ui.animations";
    public const string Extensions = "explorer.extensions";
    public const string GameMode = "gaming.mode";
    public const string PowerPlan = "gaming.power";
    private const string CloudContent = @"Software\Policies\Microsoft\Windows\CloudContent";

    public static IReadOnlyList<ITweak> Create() =>
    [
        new BooleanTweak(new(Animations, Page.Optimization, "Window and app animations",
            "Control client-area animations used by Windows and apps that honor the system preference.", Risk.Low,
            "Windows 10 / 11", "Some apps may need to reopen before they pick up the preference."),
            WindowsUserParameters.ReadAnimations, WindowsUserParameters.SetAnimations),
        new PowerPlanTweak(),
        new DwordTweak(new(GameMode, Page.Gaming, "Windows Game Mode",
            "Use the Windows gaming preference for supported games.", Risk.Low, "Windows 11",
            "A running game may need to restart. The effect varies by game and hardware."),
            @"Software\Microsoft\GameBar", "AutoGameModeEnabled", new("1", "On"), new("0", "Off"), "Windows default",
            () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? null : "Use Game Mode in Windows Settings on Windows 10."),
        new BooleanTweak(new(Extensions, Page.Customization, "File name extensions",
            "Show extensions for known file types in File Explorer.", Risk.Low,
            "Windows 10 / 11", "Open Explorer windows may need to refresh."),
            WindowsUserParameters.ReadFileExtensions, WindowsUserParameters.SetFileExtensions),
        Policy("privacy.suggestions", "Suggestions in Windows Settings", "Block suggested content presented by the Settings app.",
            "DisableWindowsSpotlightOnSettings", "Enterprise / Education / IoT Enterprise · build 17134+",
            () => PolicyEdition(17134, professional: false)),
        Policy("privacy.personalization", "Tailored experiences policy",
            "Prevent diagnostic data from being used to personalize Windows tips, offers and recommendations.",
            "DisableTailoredExperiencesWithDiagnosticData", "Pro / Enterprise / Education / IoT Enterprise · build 15063+",
            () => PolicyEdition(15063, professional: true))
    ];

    private static DwordTweak Policy(string id, string name, string description, string value, string compatibility,
        Func<string?> unavailable) => new(
        new(id, Page.Privacy, name, description, Risk.Moderate, compatibility,
            "This sets a current-user policy. Windows may display a managed-setting notice. Sign out and back in if needed."),
        CloudContent, value, new("1", "Blocked by policy"), new("0", "Allowed by policy"), "Policy not configured",
        () => unavailable() ?? (HasMachinePolicy(CloudContent, value) ? "A machine-wide Windows policy is present." : null));

    private static bool HasMachinePolicy(string path, string value)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key?.GetValue(value) is not null;
    }

    private static string? PolicyEdition(int minimumBuild, bool professional)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var edition = key?.GetValue("EditionID") as string ?? "";
        var allowed = edition.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase) ||
                      edition.StartsWith("Education", StringComparison.OrdinalIgnoreCase) ||
                      edition.StartsWith("IoTEnterprise", StringComparison.OrdinalIgnoreCase) ||
                      professional && (edition is "Professional" or "ProfessionalN" or "ProfessionalWorkstation" or "ProfessionalWorkstationN" or "ProfessionalEducation" or "ProfessionalEducationN");
        return allowed && OperatingSystem.IsWindowsVersionAtLeast(10, 0, minimumBuild) ? null : "This policy is unavailable on this Windows edition or build.";
    }
}
