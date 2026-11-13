using System.Reflection;
using System.Windows;
using Peritum.Protect.App.Native;
using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.App.Views;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Clipboard;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Links;
using Peritum.Protect.Core.Profiles;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App;

public partial class App : Application
{
    private SingleInstance? _single;
    private TrayService? _tray;
    private ClipboardWatcher? _clipboard;
    private ForegroundWatcher? _foreground;
    private NotificationService? _notifications;
    private MainWindow? _main;
    private AppState? _state;
    private readonly ClipboardAlertGate _alerts = new();
    private readonly HashSet<string> _seenLinks = new(StringComparer.Ordinal);
    private PasteWarningViewModel? _openWarning;
    private uint? _openSequence;
    private string? _lastForeground;
    private string? _lastCopyText;
    private string? _previousCopyText;
    private bool _lastCopyFromBrowser;
    private DateTime _lastCopyAt;
    private DateTime _lastErrorBox = DateTime.MinValue;
    private ThemeService? _theme;
    private System.Windows.Threading.DispatcherTimer? _pauseTimer;
    private PolicyChangeWatcher? _policyWatch;
    public Action<string>? ApplyTheme { get; private set; }

    /// <summary>Set by the visual contrast test so startup does not open the tray.</summary>
    public static bool UiTestHost { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppUserModel.SetCurrentProcess();
        base.OnStartup(e);
        UiScale.Read(e.Args);
        var args = UiScale.Strip(e.Args);
        if (TryWriteIcons(args) || TryUninstallCleanup(args))
        {
            Shutdown();
            return;
        }
        if (UiTestHost)
            return;
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Report(args.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            var error = args.Exception.GetBaseException();
            Dispatcher.BeginInvoke(() => Report(error));
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception error)
                ErrorLog.Write(error, VersionLabel());
        };

        TextCatalog.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "lang"));
        var command = ParseCommand(args);
        _single = new SingleInstance();
        if (!_single.IsFirst)
        {
            if (command is { } pending)
                QueueCommand(pending.Verb, pending.Path);
            _single.SignalShow();
            Shutdown();
            return;
        }

        _single.Listen(() => Dispatcher.BeginInvoke(() =>
        {
            ShowMain();
            _ = RunQueuedCommandAsync();
        }));

        _state = new AppState();
        _theme = new ThemeService();
        _theme.Changed += () => _tray?.ApplyTheme(_theme.IsDark, _theme.HighContrast);
        _theme.Apply(_state.Settings.Theme);
        ApplyTheme = choice => _theme.Apply(choice);
        try
        {
            StartupShortcutService.Apply(_state.Settings.StartWithWindows);
        }
        catch
        {
            // Startup folder can be locked. The toggle still saves the setting.
        }

        _policyWatch = new PolicyChangeWatcher(PolicyPaths.DefaultFile);
        _policyWatch.Changed += () => Dispatcher.BeginInvoke(ReloadOrganizationPolicy);
        _policyWatch.Start();

        var language = TextCatalog.NormalizeSetting(_state.Settings.UiLanguage);
        TextCatalog.Use(TextCatalog.IsDeveloperLanguage(language) ? language : "en");
        _clipboard = new ClipboardWatcher(Dispatcher);
        _notifications = new NotificationService();
        _main = new MainWindow(_state, _clipboard);
        UiScale.Apply(_main);
        _main.PauseChanged += () => RefreshPause();

        _tray = new TrayService();
        _tray.ApplyTheme(_theme.IsDark, _theme.HighContrast);
        _tray.OpenRequested += ShowMain;
        _tray.SettingsRequested += () =>
        {
            ShowMain();
            _main.ShowSettings();
        };
        _tray.PauseForRequested += duration =>
        {
            _state.Pause.PauseFor(duration, DateTimeOffset.Now);
            RefreshPause();
        };
        _tray.PauseUntilResumeRequested += () =>
        {
            _state.Pause.PauseUntilResume(DateTimeOffset.Now);
            RefreshPause();
        };
        _tray.ResumeRequested += () =>
        {
            _state.Pause.Resume();
            RefreshPause();
        };
        _tray.QuitRequested += Quit;
        RefreshPause();
        _pauseTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _pauseTimer.Tick += (_, _) => RefreshPause();
        _pauseTimer.Start();

        _clipboard.Faulted += message =>
        {
            _main.ViewModel.ShowError("Clipboard", message);
            if (!_main.IsVisible)
                _tray?.ShowWarning(message);
        };
        _clipboard.Captured += OnCaptured;
        _clipboard.Start();
        _foreground = new ForegroundWatcher();
        _foreground.Changed += name => Dispatcher.BeginInvoke(() => OnForeground(name));
        _foreground.Start();
        var mapDays = _state.Settings.MapRetentionDays is < 1 or > 3650 ? 30 : _state.Settings.MapRetentionDays;
        new MapStore(Paths.MapsDir).DeleteOlderThan(TimeSpan.FromDays(mapDays));

        if (_state.Settings.ShowFirstRun)
        {
            var profileLocked = _state.Policy.Locks(PolicyKeys.Profile);
            var first = new FirstRunView(
                profileLocked,
                profileLocked ? _state.Settings.Profile : _state.UserSettings.Profile,
                _state.Policy.Locks(PolicyKeys.WatchLinks));
            var continued = first.ShowDialog() == true;
            if (!continued)
            {
                Quit();
                return;
            }

            if (!profileLocked)
            {
                var known = _state.Packs.SelectMany(pack => pack.Rules).Select(rule => rule.Id);
                ProfileCatalog.TryApply(first.SelectedProfileId, _state.UserSettings, known, profileLocked: false);
            }

            if (!_state.Policy.Locks(PolicyKeys.WatchLinks))
                _state.UserSettings.WatchLinks = first.WarnAboutLinks;

            _state.UserSettings.ShowFirstRun = false;
            _state.SaveSettings();
        }

        if (command is { } startupCommand)
            _ = RunCommandAsync(startupCommand.Verb, startupCommand.Path);
        else
            ShowMain();
    }

    private void ReloadOrganizationPolicy() => _state?.ReloadPolicy();

    protected override void OnExit(ExitEventArgs e)
    {
        _policyWatch?.Dispose();
        _pauseTimer?.Stop();
        _foreground?.Dispose();
        _clipboard?.Dispose();
        _tray?.Dispose();
        _theme?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }

    private void OnCaptured(ClipboardScanInput input, string? process, uint sequence)
    {
        var state = _state;
        var main = _main;
        if (state is null || main is null || state.Paused || !state.Settings.WatchClipboard)
            return;
        var sameWindow = string.Equals(_lastForeground, process, StringComparison.OrdinalIgnoreCase);
        _lastForeground = process;
        if (!string.IsNullOrEmpty(input.Text))
        {
            var now = DateTime.UtcNow;
            if (state.Settings.WatchCryptoSwap &&
                CryptoWatch.WasSwapped(_previousCopyText, input.Text, now - _lastCopyAt, sameWindow, false))
            {
                MessageBox.Show(
                    RiskExplanations.ForKey(RiskExplanations.CryptoSwap)?.Paragraph
                    ?? TextCatalog.Get("cryptoSwapMessage"),
                    TextCatalog.Get("product"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            _previousCopyText = _lastCopyText;
            _lastCopyText = input.Text;
            _lastCopyAt = now;
            _lastCopyFromBrowser = CommandClassifier.IsBrowser(process);
        }
        var rules = state.ActiveRules().ToList();
        bool Allowed(string value) => state.AllowList.Contains(value);
        var minHits = state.Settings.MinHitsForPopup;
        Task.Run(() => BuildSession(state, input, rules, Allowed, minHits))
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    var error = task.Exception?.GetBaseException() ?? task.Exception;
                    if (error is not null)
                        Dispatcher.Invoke(() => Report(error));
                    return;
                }
                if (task.Result is null)
                    return;
                Dispatcher.Invoke(() => AcceptScan(task.Result, process, sequence, minHits));
            }, TaskScheduler.Default);
    }

    private void OnForeground(string? process)
    {
        _lastForeground = process;
        if (_state is null || _state.Paused || !_state.Settings.WatchClipboard)
            return;
        if (CommandDue(process) && TryPresentLink(process, _alerts.LatestSequence, command: true))
            return;
        if (CommandDue(process))
        {
            var clear = MessageBox.Show(
                RiskExplanations.ForKey(RiskExplanations.ClickFix)?.Paragraph
                ?? TextCatalog.Get("clickFixMessage"),
                TextCatalog.Get("product"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            _state.Events.Append(new ProtectEvent
            {
                Ts = DateTimeOffset.Now,
                Source = "clipboard",
                Action = EventActions.ClickFix
            });
            _main?.ViewModel.ReloadEvents();
            if (clear == MessageBoxResult.Yes)
                _clipboard?.ApplyStop(false);
            return;
        }
        if (_alerts.LatestSequence is uint sequence)
            MaybeWarn(process, sequence, _state.Settings.MinHitsForPopup);
    }

    private void AcceptScan(ScanSession session, string? process, uint sequence, int minHits)
    {
        if (_state is null || _main is null)
            return;
        _alerts.Store(sequence, session);
        var action = session.Result.Error is not null
            ? EventActions.Error
            : session.Result.HitCount == 0 ? EventActions.Allow : EventActions.Warn;
        _state.Events.Append(_state.Events.FromScan(
            session.Result,
            action,
            process,
            session.Request.FilePath,
            _state.Settings.RememberFileLocations));
        _main.ViewModel.ReloadEvents();
        if (session.Result.Error is not null)
        {
            _main.ViewModel.Show(session, process, logNow: false);
            return;
        }
        if (TryPresentLink(process, sequence, command: CommandDue(process)))
            return;
        MaybeWarn(process, sequence, minHits);
    }

    private void MaybeWarn(string? process, uint sequence, int minHits)
    {
        if (_state is null)
            return;
        var target = !_state.Settings.OnlyWhenTargetFocused ||
            TargetProcessList.Matches(process, _state.Settings.TargetProcesses);
        if (!_alerts.ShouldWarn(sequence, target, minHits))
            return;
        var session = _alerts.Peek(sequence);
        if (session is null)
            return;
        if (_notifications?.IsPopupOpen == true && _openWarning is not null)
        {
            _openWarning.Retarget(session, process);
            _alerts.MarkShown(sequence);
            _openSequence = sequence;
            _main?.ViewModel.Show(session, process, logNow: false);
            return;
        }
        PresentClipboard(session, process, minHits, sequence);
    }

    private void Report(Exception error)
    {
        ErrorLog.Write(error, VersionLabel());
        var now = DateTime.UtcNow;
        if (now - _lastErrorBox < TimeSpan.FromMinutes(1))
        {
            _tray?.ShowWarning(TextCatalog.Get("problemBalloon"));
            return;
        }
        _lastErrorBox = now;
        MessageBox.Show(
            TextCatalog.Get("problemBox"),
            TextCatalog.Get("product"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static string VersionLabel()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "1.0.0";
        try
        {
            return version + " " + File.GetLastWriteTime(Environment.ProcessPath ?? AppContext.BaseDirectory).ToString("yyyy-MM-dd");
        }
        catch
        {
            return version;
        }
    }

    private static ScanSession? BuildSession(
        AppState state,
        ClipboardScanInput input,
        IReadOnlyList<Rule> rules,
        Func<string, bool> isAllowed,
        int minHits)
    {
        ScanSession? session = null;
        if (input.HasRich)
            session = RichClipboard.Inspect(input, rules, state.Engine.Ocr, isAllowed);
        else if (!string.IsNullOrWhiteSpace(input.Text))
            session = state.Engine.Scan(ScanRequest.ForText(input.Text, ScanSource.Clipboard), rules, isAllowed);

        if (session is not null && (session.Result.Error is not null || session.Result.HitCount >= minHits))
            return session;

        foreach (var file in input.FilePaths.Take(3))
        {
            if (!IsSupported(file))
                continue;
            var fileSession = state.Engine.Scan(ScanRequest.ForFile(file), rules, isAllowed);
            if (fileSession.Result.Error is not null || fileSession.Result.HitCount >= minHits)
                return fileSession;
        }

        return session;
    }

    private void PresentClipboard(ScanSession session, string? process, int minHits, uint sequence)
    {
        if (_main is null || _state is null)
            return;
        _main.ViewModel.Show(session, process, logNow: false);
        if (session.Result.Error is not null)
        {
            _main.ViewModel.LogDecision(session, EventActions.Error);
            return;
        }
        if (session.Result.HitCount < minHits)
            return;

        _alerts.MarkShown(sequence);
        _openSequence = sequence;
        var model = new PasteWarningViewModel(
            session,
            process,
            _state.Settings.DefaultPasteAction,
            allowAlwaysIgnore: !_state.Policy.LockIgnore,
            remembered: _state.AllowList);
        _openWarning = model;
        model.NotSensitive += ruleId =>
        {
            _state.Events.Append(_state.Events.FalsePositive(ruleId));
            _main.ViewModel.ReloadEvents();
        };
        model.Decided += choice =>
        {
            ApplyChoice(model, choice);
            uint? closed = _openSequence;
            if (closed is uint shown)
                _alerts.MarkDecided(shown);
            _openSequence = null;
            _openWarning = null;
            if (_alerts.LatestSequence is uint latest && latest != closed)
                MaybeWarn(_lastForeground, latest, _state.Settings.MinHitsForPopup);
        };
        model.DetailsRequested += () =>
        {
            ShowMain();
            _main.ViewModel.OpenDetails();
        };
        _notifications?.ShowPaste(model);
    }

    private void ApplyChoice(PasteWarningViewModel model, PasteChoice choice)
    {
        if (_state is null || _clipboard is null || _main is null)
            return;
        var session = model.Session;
        switch (choice)
        {
            case PasteChoice.Ignore:
                _main.ViewModel.LogDecision(session, EventActions.Ignore);
                _main.ViewModel.ResultBody = TextCatalog.Get("ignoredOnce");
                break;
            case PasteChoice.Stop:
                _clipboard.ApplyStop(_state.Settings.StopBehavior != "clear-clipboard");
                _main.ViewModel.LogDecision(session, EventActions.Stop);
                _main.ViewModel.ResultBody = _state.Settings.StopBehavior == "clear-clipboard"
                    ? TextCatalog.Get("clipboardCleared")
                    : TextCatalog.Get("clipboardRestored");
                break;
            case PasteChoice.Redact:
                var findings = model.Included.Select(row => row.Finding).ToList();
                var reversible = _state.Settings.RedactionMode != "permanent";
                if (session.Result.FileType is "clipboard" or "image"
                    || session.ClipboardImage is not null
                    || session.ClipboardHtmlFragment is not null)
                {
                    var write = RichClipboard.Redact(session, findings, reversible);
                    if (write.Error is not null)
                    {
                        _main.ViewModel.ResultBody = write.Error;
                        break;
                    }

                    if (write.Map is { Tokens.Count: > 0 })
                        new MapStore(Paths.MapsDir).Save(write.Map);
                    _clipboard.SetRich(write.PlainText, write.Html, write.ImagePng);
                    _main.ViewModel.LogDecision(session, EventActions.Redact);
                    _main.ViewModel.ResultBody = write.ImagePng is not null && write.Html is not null
                        ? TextCatalog.Get("redactedTextAndImage")
                        : write.ImagePng is not null
                            ? TextCatalog.Get("redactedImageOnClipboard")
                            : TextCatalog.Get("redactedOnClipboard");
                    break;
                }

                if (reversible)
                {
                    var visible = VisibleClipboardText(session);
                    var (redacted, map) = ReversibleRedactor.Apply(visible, findings, reversible: true);
                    if (map.Tokens.Count > 0)
                    {
                        new MapStore(Paths.MapsDir).Save(map);
                        redacted += Environment.NewLine + TextCatalog.Format("mapLine", ("id", map.Id));
                    }
                    _clipboard.SetText(redacted);
                }
                else
                    _clipboard.SetText(TextRedactor.Apply(VisibleClipboardText(session), findings));
                _main.ViewModel.LogDecision(session, EventActions.Redact);
                _main.ViewModel.ResultBody = TextCatalog.Get("redactedOnClipboard");
                break;
            case PasteChoice.AlwaysIgnore:
                if (_state.Policy.LockIgnore)
                {
                    _main.ViewModel.ResultBody = UiText.SetByOrganization;
                    break;
                }
                var row = model.Selected;
                if (row is not null)
                    _state.AllowList.Add(row.Value);
                _main.ViewModel.LogDecision(session, EventActions.Ignore);
                _main.ViewModel.ResultBody = TextCatalog.Get("wontWarn");
                break;
            case PasteChoice.Dismiss:
                _main.ViewModel.LogDecision(session, EventActions.Dismiss);
                _main.ViewModel.ResultBody = TextCatalog.Get("warningDismissed");
                break;
            default:
                _main.ViewModel.LogDecision(session, EventActions.Warn);
                break;
        }
    }

    private static string VisibleClipboardText(ScanSession session)
    {
        if (session.VisibleLength >= 0 && session.VisibleLength <= session.Text.Length)
            return session.Text[..session.VisibleLength];
        return session.Text;
    }

    private void ShowMain()
    {
        if (_main is null)
            return;
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.ShowInTaskbar = true;
        _main.Activate();
    }

    private void RefreshPause()
    {
        if (_state is null || _main is null)
            return;
        var now = DateTimeOffset.Now;
        _main.ViewModel.RefreshPause(now);
        var paused = _state.Pause.IsPaused(now);
        _tray?.SetPauseState(paused, _state.Pause.Status(now));
        if (_state.Pause.ShouldRemind(now))
        {
            _tray?.ShowWarning(TextCatalog.Get("pauseReminder"));
            _state.Pause.MarkReminded();
        }
    }

    private void Quit()
    {
        if (_main?.PrepareExit() == false)
        {
            ShowMain();
            return;
        }
        Shutdown();
    }

    private static bool IsSupported(string path)
        => SupportedFileTypes.IsSupported(path);

    private static bool TryWriteIcons(string[] args)
    {
        if (args.Length < 2)
            return false;
        var verb = args[0].Trim().TrimStart('-', '/');
        if (!verb.Equals("write-icons", StringComparison.OrdinalIgnoreCase))
            return false;
        IconRaster.WriteRepo(args[1].Trim('"'));
        return true;
    }

    // The uninstaller runs this before it deletes the files. Data in %LOCALAPPDATA%\Peritum\Protect is kept.
    private static bool TryUninstallCleanup(string[] args)
    {
        if (args.Length != 1)
            return false;
        var verb = args[0].Trim().TrimStart('-', '/');
        if (!verb.Equals("uninstall-cleanup", StringComparison.OrdinalIgnoreCase))
            return false;
        ExplorerMenu.Unregister();
        foreach (var shortcut in new[] { StartupShortcutService.ShortcutPath, StartupShortcutService.LegacyShortcutPath })
        {
            try
            {
                StartupShortcutService.Remove(shortcut);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return true;
    }

    private static (string Verb, string Path)? ParseCommand(string[] args)
    {
        if (args.Length >= 1)
        {
            var verb = args[0].Trim().TrimStart('-', '/').ToLowerInvariant();
            if (verb is "show-warning")
                return ("warn", "-");
            if (verb is "show-link-warning")
                return ("link-warn", "-");
            if (args.Length >= 2 && verb is "scan" or "protect")
                return (verb, args[1].Trim('"'));
        }
        if (args.Length == 1 && File.Exists(args[0]))
            return ("scan", args[0]);
        return null;
    }

    private static void QueueCommand(string verb, string path)
    {
        Paths.EnsureDirectories();
        File.WriteAllText(Paths.CommandFile, verb + "\n" + path);
    }

    private async Task RunQueuedCommandAsync()
    {
        if (!File.Exists(Paths.CommandFile))
            return;
        string text;
        try
        {
            text = File.ReadAllText(Paths.CommandFile);
            File.Delete(Paths.CommandFile);
        }
        catch
        {
            return;
        }
        var parts = text.Split('\n', 2);
        if (parts.Length == 2)
            await RunCommandAsync(parts[0].Trim(), parts[1].Trim());
    }

    private async Task RunCommandAsync(string verb, string path)
    {
        ShowMain();
        if (_main is null)
            return;
        if (verb == "warn")
        {
            ShowSampleWarning();
            return;
        }
        if (verb == "link-warn")
        {
            ShowSampleLinkWarning();
            return;
        }
        if (Directory.Exists(path))
        {
            await _main.ScanPathsAsync([path]);
            return;
        }
        if (!File.Exists(path))
        {
            _main.ViewModel.ResultTitle = Path.GetFileName(path);
            _main.ViewModel.ResultBody = TextCatalog.Get("couldNotRead");
            return;
        }
        await _main.ScanFileAsync(path);
        if (verb == "protect" && _main.ViewModel.CanMakeCopy)
            _main.ViewModel.MakeCopyCommand.Execute(null);
    }

    private bool CommandDue(string? process) =>
        _state is not null
        && _state.Settings.WatchCommands
        && _lastCopyFromBrowser
        && CommandClassifier.IsCommand(_lastCopyText ?? "")
        && CommandClassifier.IsShellTarget(process, WindowNative.ForegroundTitle());

    private bool TryPresentLink(string? process, uint? sequence, bool command)
    {
        if (_state is null || _notifications is null || _notifications.IsPopupOpen)
            return false;
        if (!_state.Settings.WatchLinks || string.IsNullOrWhiteSpace(_lastCopyText))
            return false;
        var plan = LinkNotice.Build(
            _lastCopyText,
            enabled: true,
            _state.RegionLinkCatalog(),
            _state.TrustedSites.Contains,
            _seenLinks);
        if (!plan.Warns)
            return false;

        var session = sequence is uint id ? _alerts.Peek(id) : null;
        var data = session is not null && session.Result.HitCount >= _state.Settings.MinHitsForPopup;
        var model = new LinkWarningViewModel(plan, command, data);
        foreach (var key in model.RememberKeys)
            _seenLinks.Add(key);
        _state.Events.Append(new ProtectEvent
        {
            Ts = DateTimeOffset.Now,
            Source = "clipboard",
            Action = EventActions.LinkWarning,
            Hits = plan.ReasonCodes
                .GroupBy(code => code, StringComparer.Ordinal)
                .Select(group => new HitCount { RuleId = group.Key, Count = group.Count() })
                .ToList()
        });
        if (command)
        {
            _state.Events.Append(new ProtectEvent
            {
                Ts = DateTimeOffset.Now,
                Source = "clipboard",
                Action = EventActions.ClickFix
            });
        }

        _main?.ViewModel.ReloadEvents();
        model.WhyRequested += () => MessageBox.Show(
            TextCatalog.Get("linkLimits"),
            TextCatalog.Get("product"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        model.Decided += choice =>
        {
            if (_state is null)
                return;
            if (choice == LinkChoice.Clear)
                _clipboard?.ApplyStop(false);
            if (choice == LinkChoice.Trust)
            {
                foreach (var domain in model.Domains)
                    _state.TrustedSites.Add(domain);
            }

            if (choice == LinkChoice.Redact && session is not null)
            {
                var paste = new PasteWarningViewModel(session, process, "redact", allowAlwaysIgnore: !_state.Policy.LockIgnore, remembered: _state.AllowList);
                ApplyChoice(paste, PasteChoice.Redact);
            }

            if (sequence is uint shown)
            {
                _alerts.MarkShown(shown);
                _alerts.MarkDecided(shown);
            }
        };
        _notifications.ShowLink(model);
        if (sequence is uint shownNow)
        {
            _alerts.MarkShown(shownNow);
            _alerts.MarkDecided(shownNow);
        }

        return true;
    }

    private void ShowSampleLinkWarning()
    {
        var plan = LinkNotice.Build("https://paypa1.example/signin", true, BrandCatalog.LoadAll());
        var model = new LinkWarningViewModel(plan, command: false, data: false);
        model.WhyRequested += () => MessageBox.Show(
            TextCatalog.Get("linkLimits"),
            TextCatalog.Get("product"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        _notifications?.ClosePopups();
        _notifications?.ShowLink(model);
    }

    private void ShowSampleWarning()
    {
        if (_state is null)
            return;
        var session = _state.Engine.Scan(
            ScanRequest.ForText("ada@example.com", ScanSource.Clipboard),
            _state.ActiveRules());
        PresentClipboard(session, "chrome.exe", 1, 1);
    }
}
