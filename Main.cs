using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using Flow.Launcher.Plugin;

namespace MonitorBrightness
{
    public class Main : IPlugin, IResultUpdated, IPluginI18n
    {
        internal PluginInitContext Context = null!;

        // Docs: https://www.flowlauncher.com/docs/#/API-Reference/Flow.Launcher.Plugin/IResultUpdated
        public event ResultUpdatedEventHandler? ResultsUpdated;

        private static readonly Regex DigitRegex = new Regex(@"^[ +-]*(\d+)[ +-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public List<Result> Query(Query query)
        {
            string? errorMessage = null;
            var digitMatches = DigitRegex.Match(query.Search);

            List<Monitors.BrightnessInfo>? currentBrightnesses = null;
            int targetBrightness;
            if(digitMatches.Success)
            {
                if(int.TryParse(digitMatches.Groups[0].Value, out int value)) 
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
            return new List<Result> { result };
        }

        public void Init(PluginInitContext context)
        {
            Context = context;

            // TODO: Ask Flow devs "Do I ever need to call Context.API.RemoveGlobalKeyboardCallback()"? Either way, please clarify docs.
            Context.API.RegisterGlobalKeyboardCallback(HandleKeyboardShortcut);
        }

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
                SetBrightnessAtCursor((v) =>
                {
                    v.Brightness.CurrentValue = (uint)Math.Clamp((int)v.Brightness.CurrentValue + delta, v.Brightness.MinValue, v.Brightness.MaxValue);
                });

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

        private static int GetAverageBrightness(List<Monitors.BrightnessInfo> brightnesses)
        {
            return (int)brightnesses.Average(v => v.CurrentValue);
        }

        private List<Monitors.BrightnessInfo>? GetBrightnessAtCursor()
        {
            var monitor = Monitors.GetHMonitorAtCursor();
            if(monitor == null)
            {
                LogException(new Exception("Cannot get monitor at cursor"));
                return null;
            }

            var physicalMonitors = Monitors.GetMonitorsFrom(monitor);
            if (physicalMonitors == null)
            {
                LogException(new Exception("Cannot get physical monitors"));
                return null;
            }
            if (physicalMonitors.Length == 0)
            {
                Context.API.LogWarn("Main.cs", "Could not find any monitors");
            }

            try
            {
                return Monitors.GetMonitorBrightnesses(physicalMonitors);
            } 
            catch(Exception e)
            {
                LogException(e);
                return null;
            }
            finally
            {
                Monitors.Destroy(physicalMonitors);
            }
        }

        private void SetBrightnessAtCursor(Action<Monitors.PhysicalMonitor> callback)
        {
            var monitor = Monitors.GetHMonitorAtCursor();
            if (monitor == null)
            {
                LogException(new Exception("Cannot get monitor at cursor"));
                return;
            }

            var physicalMonitors = Monitors.GetMonitorsFrom(monitor);
            if (physicalMonitors == null)
            {
                LogException(new Exception("Cannot get physical monitors"));
                return;
            }
            try
            {
                Monitors.UpdateMonitorBrightnesses(physicalMonitors, callback);
            }
            catch(Exception e)
            {
                LogException(e);
            }
            finally
            {
                Monitors.Destroy(physicalMonitors);
            }
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
    }
}
