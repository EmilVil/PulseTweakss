using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pulse.Models;
using Pulse.Services;
using Pulse.Tweaks;
using Pulse.ViewModels;
using Pulse.Views;
using Page = Pulse.Models.Page;

namespace Pulse.Tests;

internal static class Program
{
    private static readonly TweakOption Off = new("0", "Off");
    private static readonly TweakOption On = new("1", "On");
    private static readonly TweakOption Default = new(null, "Windows default");

    [STAThread]
    private static int Main(string[] args)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Pulse.Tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonStore(directory);
            VerifyEngineAsync(store).GetAwaiter().GetResult();
            Console.WriteLine("PASS apply, original backup, null-value restore, stale preview, non-reversible history, failure recovery, settings persistence, export.");
            if (args.Length > 0)
                VerifyWindow(store, args[0]);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                    File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }

    private static async Task VerifyEngineAsync(JsonStore store)
    {
        var engine = new TweakEngine(store);
        var setting = new FakeTweak("demo", Default) { OnWrite = () =>
            Check(store.ReadHistory().Any(entry => entry.Info.Id == "demo" &&
                entry.Status is ChangeStatus.Pending or ChangeStatus.Reverting), "Write must follow durable journal") };
        var change = await engine.ApplyAsync(new(setting, Default, On));
        Check(change.Status == ChangeStatus.Applied && change.Before.Value is null && setting.Current == On, "Apply and original absence");
        Check(store.ReadHistory().Single().Before.Value is null, "Persist original absence");
        var result = await engine.RevertAsync(change.Id, setting);
        Check(result.Status == ChangeStatus.Reverted && setting.Current.Value is null, "Restore absent value");

        await ExpectFailureAsync(() => engine.ApplyAsync(new(setting, Off, On)));
        Check(store.ReadHistory().Count == 1 && setting.Current == Default, "Stale preview is rejected");

        var first = await engine.ApplyAsync(new(setting, Default, On));
        var second = await engine.ApplyAsync(new(setting, On, Off));
        Check(!TweakEngine.CanRevert(store.ReadHistory(), first), "Undo more recent changes first");
        setting.Current = Default;
        await ExpectFailureAsync(() => engine.RevertAsync(second.Id, setting));
        Check(setting.Current == Default, "Preserve external changes");
        setting.Current = Off;
        await engine.RevertAsync(second.Id, setting);
        await engine.RevertAsync(first.Id, setting);
        Check(setting.Current == Default, "Restore chained changes");

        var irreversible = new FakeTweak("package", Off, reversible: false);
        var removed = await engine.ApplyAsync(new(irreversible, Off, On));
        Check(!TweakEngine.CanRevert(store.ReadHistory(), removed), "Do not offer undo for removal");
        await ExpectFailureAsync(() => engine.RevertAsync(removed.Id, irreversible));

        var failing = new FakeTweak("failure", Off) { OnWrite = () => throw new InvalidOperationException("simulated write failure") };
        var failed = await engine.ApplyAsync(new(failing, Off, On));
        Check(failed.Status == ChangeStatus.Failed && failed.Detail == "simulated write failure", "Record a verified failed write");

        var uncertain = new FakeTweak("uncertain", Off);
        uncertain.OnWrite = () => uncertain.CanRead = false;
        var incomplete = await engine.ApplyAsync(new(uncertain, Off, On));
        Check(incomplete.Status == ChangeStatus.ReviewRequired, "Retain backup when post-write verification fails");
        uncertain.CanRead = true;
        uncertain.OnWrite = null;
        var recovered = await engine.RevertAsync(incomplete.Id, uncertain);
        Check(recovered.Status == ChangeStatus.Reverted && uncertain.Current == Off, "Reconcile with the actual value on revert");

        var preferences = new Preferences(Profile.Gaming, false, false);
        store.SavePreferences(preferences);
        Check(store.ReadPreferences() == preferences, "Round-trip preferences");
        store.SavePreferences(new());

        var export = Path.Combine(Path.GetTempPath(), "Pulse.Export-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            JsonStore.Export(export, preferences, new[] { "sample" }, store.ReadHistory());
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(export));
            Check(document.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
                  document.RootElement.GetProperty("history").GetArrayLength() == store.ReadHistory().Count, "Export current schema and history");
        }
        finally { File.Delete(export); }
    }

    private static void VerifyWindow(JsonStore store, string imagePath)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Pulse;component/Themes/Dark.xaml", UriKind.Relative) });
        using var bindings = new StringWriter();
        var trace = new TextWriterTraceListener(bindings);
        PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var window = new MainWindow(store);
        Exception? error = null;
        app.MainWindow = window;
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                var model = (MainViewModel)window.DataContext;
                var timeout = Stopwatch.StartNew();
                while ((model.Busy || model.Windows == "Detecting…" || model.CpuLabel == "—") && timeout.Elapsed < TimeSpan.FromSeconds(40))
                    await Task.Delay(150);

                Check(!model.Busy && model.Windows.StartsWith("Windows"), "Read existing Windows version");
                Check(model.CpuLabel.EndsWith('%') && model.RamLabel.EndsWith('%'), "Read live CPU and memory percentages");
                Check(((Image)window.FindName("Brand")).Source is not null, "Load supplied logo");
                await window.Dispatcher.InvokeAsync(() => Capture(window, imagePath), DispatcherPriority.ApplicationIdle);
                foreach (var page in Enum.GetValues<Page>())
                {
                    model.Navigate(page);
                    await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
                    Check(model.PageTitle == page.ToString(), $"Navigate to {page}");
                }
                model.Navigate(Page.Gaming);
                await Task.Delay(175);
                await window.Dispatcher.InvokeAsync(() =>
                {
                    window.UpdateLayout();
                    var powerPlan = model.VisibleRows.Single(setting => setting.Info.Id == TweakCatalog.PowerPlan);
                    if (powerPlan.HasChoices)
                    {
                        var picker = Descendants(window).OfType<ComboBox>().Single(control => control.IsVisible);
                        Check(Descendants(picker).OfType<TextBlock>().Any(text => text.Text == powerPlan.Target!.Label),
                            "Render the selected power plan's friendly name");
                    }
                    var gamingShot = Path.Combine(Path.GetDirectoryName(imagePath)!, "gaming-check.png");
                    Capture(window, gamingShot);
                }, DispatcherPriority.ApplicationIdle);

                window.Width = 785;
                model.Navigate(Page.Overview);
                await Task.Delay(175);
                await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
                var search = (TextBox)window.FindName("SearchBox");
                search.SetCurrentValue(TextBox.TextProperty, "file name extensions");
                await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
                var row = model.VisibleRows.Single();
                Check(row.Info.Name == "File name extensions", "Search returned the expected system setting");

                var historyBefore = store.ReadHistory().Count;
                row.Staged = true;
                Check(model.SelectedCount == 1 && model.ApplyCommand.CanExecute(null), "Stage one change");
                var cancel = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(175) };
                var sawPreview = false;
                cancel.Tick += (_, _) =>
                {
                    cancel.Stop();
                    if (app.Windows.OfType<ReviewWindow>().SingleOrDefault() is { } preview)
                    {
                        sawPreview = true;
                        var previewShot = Path.Combine(Path.GetDirectoryName(imagePath)!, "preview-check.png");
                        Capture(preview, previewShot);
                        preview.DialogResult = false;
                    }
                };
                cancel.Start();
                model.ApplyCommand.Execute(null);
                Check(sawPreview && store.ReadHistory().Count == historyBefore && model.SelectedCount == 1, "Cancel preview before any Windows write");
                model.ClearCommand.Execute(null);
                Check(model.SelectedCount == 0, "Clear stages");

                trace.Flush();
                Check(bindings.ToString().Length == 0, bindings.ToString());
                Console.WriteLine("PASS WPF render, exact logo, all nine pages, native telemetry, resize, search, queue, and modal cancellation.");
            }
            catch (Exception ex) { error = ex; }
            finally { window.Close(); }
        };
        app.Run(window);
        PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
        if (error is not null)
            throw new InvalidOperationException("UI smoke test failed.", error);
    }

    private static void Capture(Window window, string path)
    {
        var frame = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        frame.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static async Task ExpectFailureAsync(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected an operation to fail.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FakeTweak(string id, TweakOption initial, bool reversible = true) : ITweak
    {
        public TweakInfo Info { get; } = new(id, Page.Optimization, id, "Test", Risk.Low, "Test", "Test", reversible);
        public TweakOption Current { get; set; } = initial;
        public bool CanRead { get; set; } = true;
        public Action? OnWrite { get; set; }

        public Task<Detection> DetectAsync() => Task.FromResult(CanRead
            ? Detection.Available(Current, Off, On)
            : Detection.Unavailable("simulated detector failure"));

        public Task WriteAsync(TweakOption target, TweakOption expected)
        {
            Check(Current == expected, "Unexpected original test state");
            OnWrite?.Invoke();
            Current = target;
            return Task.CompletedTask;
        }
    }
}
