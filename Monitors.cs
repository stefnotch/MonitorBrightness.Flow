using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonitorBrightness
{
    internal class Monitors
    {
        public static HMonitor? GetHMonitorAtCursor()
        {
            if (!NativeMethods.GetCursorPos(out var cursor))
            {
                return null;
            }
            IntPtr monitorPtr = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorFromPointFlags.MONITOR_DEFAULTTONEAREST);
            if (monitorPtr == IntPtr.Zero)
            {
                return null;
            }
            return new HMonitor(monitorPtr);
        }

        public class HMonitor
        {
            public HMonitor(IntPtr monitorPtr)
            {
                MonitorPtr = monitorPtr;
            }

            public IntPtr MonitorPtr { get; }
        }

        public static NativeMethods.PHYSICAL_MONITOR[]? GetMonitorsFrom(HMonitor hMonitor)
        {
            uint physicalMonitorsCount = 0;
            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor.MonitorPtr, ref physicalMonitorsCount))
            {
                return null;
            }

            var physicalMonitors = new NativeMethods.PHYSICAL_MONITOR[physicalMonitorsCount];
            if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor.MonitorPtr, physicalMonitorsCount, physicalMonitors))
            {
                return null;
            }

            return physicalMonitors;
        }

        public static List<BrightnessInfo> GetMonitorBrightnesses(NativeMethods.PHYSICAL_MONITOR[] physicalMonitors)
        {
            var result = new List<BrightnessInfo>();
            foreach (var monitor in physicalMonitors)
            {
                uint minValue = 0, currentValue = 0, maxValue = 0;
                if (!NativeMethods.GetMonitorBrightness(monitor.physicalMonitorPtr, ref minValue, ref currentValue, ref maxValue))
                {
                    continue;
                }

                result.Add(new BrightnessInfo()
                {
                    MinValue = minValue,
                    MaxValue = maxValue,
                    CurrentValue = currentValue,
                });
            }
            return result;
        }

        public static void UpdateMonitorBrightnesses(NativeMethods.PHYSICAL_MONITOR[] physicalMonitors, Action<BrightnessInfo> callback)
        {
            var exceptions = new List<Exception>();
            foreach (var monitor in physicalMonitors)
            {
                uint minValue = 0, currentValue = 0, maxValue = 0;
                if (!NativeMethods.GetMonitorBrightness(monitor.physicalMonitorPtr, ref minValue, ref currentValue, ref maxValue))
                {
                    continue;
                }

                try
                {
                    var monitorBrightness = new BrightnessInfo()
                    {
                        MinValue = minValue,
                        MaxValue = maxValue,
                        CurrentValue = currentValue,
                    };
                    callback(monitorBrightness);

                    if (monitorBrightness.CurrentValue != currentValue)
                    {
                        uint newBrightness = Math.Clamp(monitorBrightness.CurrentValue, minValue, maxValue);

                        if (!NativeMethods.SetMonitorBrightness(monitor.physicalMonitorPtr, newBrightness))
                        {
                            exceptions.Add(new Exception("Failed to set monitor brightness"));
                        }
                    }
                } 
                catch (Exception e)
                {
                    exceptions.Add(e);
                }
            }

            if(exceptions.Count > 0)
            {
                throw new AggregateException(exceptions);
            }
        }

        public static void Destroy(NativeMethods.PHYSICAL_MONITOR[] physicalMonitors)
        {
            NativeMethods.DestroyPhysicalMonitors((uint)physicalMonitors.Length, ref physicalMonitors);
        }

        public class BrightnessInfo
        {
            public uint MinValue { get; init; }
            public uint MaxValue { get; init; }
            public uint CurrentValue { get; set; }
        }
    }
}
