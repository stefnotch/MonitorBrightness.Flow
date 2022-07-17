using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Flow.Launcher.Plugin;
using Monitorian.Core.Models.Monitor;
using Monitorian.Core.Models.Watcher;

namespace MonitorBrightness
{
    public class Main : IAsyncPlugin, IDisposable, IResultUpdated, IPluginI18n, IAppControllerCore
    {
        internal PluginInitContext Context = null!;

        // Docs: https://www.flowlauncher.com/docs/#/API-Reference/Flow.Launcher.Plugin/IResultUpdated
        public event ResultUpdatedEventHandler? ResultsUpdated;

        private static readonly Regex DigitRegex = new Regex(@"^[ +-]*(\d+)[ +-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        
        private readonly DisplayWatcher _displayWatcher = new DisplayWatcher();
        private readonly SessionWatcher _sessionWatcher = new SessionWatcher();
        private readonly PowerWatcher _powerWatcher = new PowerWatcher();
        private readonly BrightnessWatcher _brightnessWatcher = new BrightnessWatcher();

        public List<MonitorViewModel> Monitors { get; } = new List<MonitorViewModel>();
        protected readonly object _monitorsLock = new();

        private bool HandleKeyboardShortcut(int keyevent, int vkcode, SpecialKeyState state)
        {
            // Docs: https://www.flowlauncher.com/docs/#/API-Reference/Flow.Launcher.Plugin/FlowLauncherGlobalKeyboardEventHandlerR

            //Context.API.ChangeQuery(keyevent + "," + vkcode + "," + (state.CtrlPressed?"1":"0") + (state.WinPressed ? "1" : "0"));
            const int KeydownEvent = 256;
            const int ArrowUpVk = 38;
            const int ArrowDownVk = 40;

            if(keyevent == KeydownEvent && (vkcode == ArrowUpVk || vkcode == ArrowDownVk) && state.CtrlPressed && state.WinPressed)
            {
                int delta = vkcode == ArrowUpVk ? 20 : -20;

                NativeMethods.GetCursorPos(out var cursorPos);

                Context.API.LogInfo(
                    "Main.cs", 
                    "Cursor: " + cursorPos.X + "," + cursorPos.Y + 
                    "  Monitors: " + string.Join(", ", Monitors.Select(m => m.MonitorRect))
                    );

                // TODO: Ask if there is an API to tell if a result is still visible/gives me a callback when a result goes away
                /*
                var currentBrightnesses = GetBrightnessAtCursor();
                if (currentBrightnesses == null) return true;

                int targetBrightness = GetAverageBrightness(currentBrightnesses);
                targetBrightness += vkcode == ArrowUpVk ? 20 : -20;

                Context.API.ChangeQuery(Context.CurrentPluginMetadata.ActionKeyword + " " + targetBrightness);
                Context.API.ShowMainWindow();
                */
                return false;
            }
            return true;
        }

        public string GetTranslatedPluginTitle()
        {
            return Context.API.GetTranslation("plugin_monitorbrightness_plugin_name");
        }

        public string GetTranslatedPluginDescription()
        {
            return Context.API.GetTranslation("plugin_monitorbrightness_plugin_description");
        }

        private void LogException(Exception exception)
        {
            Context.API.LogException("Main.cs", "MonitorBrightness.Flow", exception);
        }

        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            return new();
            /*
            string? errorMessage = null;
            var digitMatches = DigitRegex.Match(query.Search);

            List<Monitors.BrightnessInfo>? currentBrightnesses = null;
            int targetBrightness;
            if (digitMatches.Success)
            {
                if (int.TryParse(digitMatches.Groups[0].Value, out int value))
                {
                    targetBrightness = value;
                }
                else
                {
                    targetBrightness = 0;
                    errorMessage ??= "Failed to parse the number";
                }
            }
            else
            {
                currentBrightnesses = GetBrightnessAtCursor();
                if (currentBrightnesses == null)
                {
                    targetBrightness = 69;
                    errorMessage ??= "Failed to get current brightness";
                }
                else
                {
                    targetBrightness = GetAverageBrightness(currentBrightnesses);
                }
            }

            targetBrightness += query.Search.Count(v => v == '+');
            targetBrightness += -1 * query.Search.Count(v => v == '-');

            targetBrightness = Math.Clamp(targetBrightness, 0, 100);

            string targetBrightnessString = errorMessage == null ? "" + targetBrightness : errorMessage;
            string subtitle = currentBrightnesses != null ?
                Context.API.GetTranslation("plugin_monitorbrightness_current_brightness") + ": " +
                string.Join(", ", currentBrightnesses.Select(v => v.CurrentValue))
                : "";

            var result = new Result
            {
                Title = $"{Context.API.GetTranslation("plugin_monitorbrightness_title")}: {targetBrightnessString}",
                SubTitle = subtitle,
                Action = c =>
                {
                    SetBrightnessAtCursor((v) =>
                    {
                        v.Brightness.CurrentValue = (uint)Math.Clamp(targetBrightness, v.Brightness.MinValue, v.Brightness.MaxValue);
                    });
                    return true;
                },
                IcoPath = "Images/app.png"
            };
            return new List<Result> { result };*/
        }

        public async Task InitAsync(PluginInitContext context)
        {
            Context = context;

            // TODO: Ask Flow devs "Do I ever need to call Context.API.RemoveGlobalKeyboardCallback()"? Either way, please clarify docs.
            Context.API.RegisterGlobalKeyboardCallback(HandleKeyboardShortcut);

            await ScanAsync();
            _displayWatcher.Subscribe(() => OnMonitorsChangeInferred(nameof(DisplayWatcher)));
			_sessionWatcher.Subscribe((e) => OnMonitorsChangeInferred(nameof(SessionWatcher), e));
			_powerWatcher.Subscribe((e) => OnMonitorsChangeInferred(nameof(PowerWatcher), e), PowerManagement.GetOnPowerSettingChanged());
			_brightnessWatcher.Subscribe((instanceName, brightness) =>
			{
				if (!_sessionWatcher.IsLocked)
					Update(instanceName, brightness);
			},
            (message) => Context.API.LogWarn("Main.cs", message));
        }

        protected virtual async void OnMonitorsChangeInferred(object sender = null, ICountEventArgs e = null)
        {
            if (e?.Count == 0)
                return;

            await ScanAsync(TimeSpan.FromSeconds(3));
        }

        public void OnMonitorAccessFailed(AccessResult result)
        {
            Context.API.LogWarn("Main.cs", $"{nameof(OnMonitorAccessFailed)}" + Environment.NewLine
                + $"Status: {result.Status}" + Environment.NewLine
                + $"Message: {result.Message}");
        }

        public void OnMonitorsChangeFound()
        {
            if (Monitors.Any())
            {
                _displayWatcher.RaiseDisplaySettingsChanged();
            }
        }

        internal event EventHandler<bool>? ScanningChanged;

        protected virtual Task<byte> GetMaxMonitorsCountAsync() => Task.FromResult<byte>(4);
        protected const int MaxKnownMonitorsCount = 64;

        protected virtual MonitorViewModel GetMonitor(IMonitor monitorItem) => new MonitorViewModel(this, monitorItem);
        protected virtual void DisposeMonitor(MonitorViewModel monitor) => monitor?.Dispose();

        private int _scanCount = 0;
        private int _updateCount = 0;

        internal Task ScanAsync() => ScanAsync(TimeSpan.Zero);

        protected virtual async Task ScanAsync(TimeSpan interval)
        {
            var isEntered = false;
            try
            {
                isEntered = (Interlocked.Increment(ref _scanCount) == 1);
                if (isEntered)
                {
                    ScanningChanged?.Invoke(this, true);

                    var intervalTask = (interval > TimeSpan.Zero) ? Task.Delay(interval) : Task.CompletedTask;

                    await Task.Run(async () =>
                    {
                        var oldMonitorIndices = Enumerable.Range(0, Monitors.Count).ToList();
                        var newMonitorItems = new List<IMonitor>();

                        foreach (var item in await MonitorManager.EnumerateMonitorsAsync())
                        {
                            var oldMonitorExists = false;

                            foreach (int index in oldMonitorIndices)
                            {
                                var oldMonitor = Monitors[index];
                                if (string.Equals(oldMonitor.DeviceInstanceId, item.DeviceInstanceId, StringComparison.OrdinalIgnoreCase))
                                {
                                    oldMonitorExists = true;
                                    oldMonitorIndices.Remove(index);
                                    oldMonitor.Replace(item);
                                    break;
                                }
                            }

                            if (!oldMonitorExists)
                                newMonitorItems.Add(item);
                        }

                        if (oldMonitorIndices.Count > 0)
                        {
                            oldMonitorIndices.Reverse(); // Reverse indices to start removing from the tail.
                            foreach (int index in oldMonitorIndices)
                            {
                                DisposeMonitor(Monitors[index]);
                                lock (_monitorsLock)
                                {
                                    Monitors.RemoveAt(index);
                                }
                            }
                        }

                        if (newMonitorItems.Count > 0)
                        {
                            foreach (var item in newMonitorItems)
                            {
                                var newMonitor = GetMonitor(item);
                                lock (_monitorsLock)
                                {
                                    Monitors.Add(newMonitor);
                                }
                            }
                        }
                    });

                    var maxMonitorsCount = await GetMaxMonitorsCountAsync();

                    var updateResults = await Task.WhenAll(Monitors
                        .Where(x => x.IsReachable)
                        .Select((x, index) =>
                        {
                            if (index < maxMonitorsCount)
                            {
                                return Task.Run(() =>
                                {
                                    if (x.UpdateBrightness())
                                    {
                                        x.IsTarget = true;
                                    }
                                    return x.IsControllable;
                                });
                            }
                            x.IsTarget = false;
                            return Task.FromResult(false);
                        }));

                    var controllableMonitorExists = updateResults.Any(x => x);

                    foreach (var m in Monitors.Where(x => !x.IsControllable))
                        m.IsTarget = !controllableMonitorExists;

                    await intervalTask;
                }
            }
            finally
            {
                if (isEntered)
                {
                    ScanningChanged?.Invoke(this, false);

                    Interlocked.Exchange(ref _scanCount, 0);
                }
            }
        }

        protected virtual async Task CheckUpdateAsync()
        {
            if (_scanCount > 0)
                return;

            var isEntered = false;
            try
            {
                isEntered = (Interlocked.Increment(ref _updateCount) == 1);
                if (isEntered)
                {
                    if (await Task.Run(() => MonitorManager.CheckMonitorsChanged()))
                    {
                        OnMonitorsChangeFound();
                    }
                    else
                    {
                        await Task.WhenAll(Monitors
                            .Where(x => x.IsTarget)
                            .SelectMany(x => new[]
                            {
                                Task.Run(() => x.UpdateBrightness()),
                                (x.IsContrastChanging ? Task.Run(() => x.UpdateContrast()) : Task.CompletedTask),
                            }));
                    }
                }
            }
            finally
            {
                if (isEntered)
                {
                    Interlocked.Exchange(ref _updateCount, 0);
                }
            }
        }

        protected virtual void Update(string instanceName, int brightness)
        {
            var monitor = Monitors.FirstOrDefault(x => instanceName.StartsWith(x.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
            monitor?.UpdateBrightness(brightness);
        }

        public void Dispose()
        {
            MonitorsDispose();

            _displayWatcher.Dispose();
            _sessionWatcher.Dispose();
            _powerWatcher.Dispose();
            _brightnessWatcher.Dispose();
        }
        private void MonitorsDispose()
        {
            foreach (var m in Monitors)
                m.Dispose();
        }
    }
}
