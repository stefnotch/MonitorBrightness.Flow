using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;

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
            PhysicalMonitorsCallback(physicalMonitors, (v) =>
            {
                result.Add(v.Brightness);
            });
            if(result.Count == 0 && physicalMonitors.Length > 0)
            {
                throw new Exception("Failed to get monitor brightnesses");
            }
            return result;
        }

        public static void PhysicalMonitorsCallback(NativeMethods.PHYSICAL_MONITOR[] physicalMonitors, Action<PhysicalMonitor> callback)
        {
            var exceptions = new List<Exception>();
            foreach (var monitor in physicalMonitors)
            {
                if (!NativeMethods.GetMonitorCapabilities(monitor.physicalMonitorPtr, out uint monitorCapabilitiesFlags, out uint _))
                {
                    exceptions.Add(new Exception("Could not fetch monitor capabilities " + (uint)Marshal.GetLastWin32Error()));
                    continue;
                }

                if (!NativeMethods.HasFlag(monitorCapabilitiesFlags, NativeMethods.MonitorCapabilitiesFlags.MC_CAPS_BRIGHTNESS))
                {
                    try
                    {
                        // TODO: Verify that this one can be controlled with WMI
                        var managmentScope = new ManagementScope("root\\WMI");
                        var getBrightnessQuery = new SelectQuery("WmiMonitorBrightness");

                        var wmiMonitors = new Dictionary<string, PhysicalMonitor>();

                        using (var managmentSearcher = new ManagementObjectSearcher(managmentScope, getBrightnessQuery))
                        {
                            using (var objectCollection = managmentSearcher.Get())
                            {
                                foreach (var managmentObject in objectCollection)
                                {
                                    uint currentBrightness = (byte)managmentObject.GetPropertyValue("CurrentBrightness");
                                    string instanceName = (string)managmentObject.GetPropertyValue("InstanceName");

                                    var monitorBrightness = new BrightnessInfo()
                                    {
                                        MinValue = 0,
                                        MaxValue = 100,
                                        CurrentValue = currentBrightness,
                                    };
                                    var physicalMonitor = new PhysicalMonitor(monitorBrightness, true);
                                    wmiMonitors.Add(instanceName, physicalMonitor);
                                    // TODO: Level[] and Levels?
                                    // https://docs.microsoft.com/en-au/windows/win32/wmicoreprov/wmimonitorbrightness
                                }
                            }
                        }

                        var setBrightnessQuery = new SelectQuery("WmiMonitorBrightnessMethods");
                        using (var managmentSearcher = new ManagementObjectSearcher(managmentScope, setBrightnessQuery))
                        {
                            using (var objectCollection = managmentSearcher.Get())
                            {
                                foreach (ManagementObject managmentObject in objectCollection)
                                {
                                    string instanceName = (string)managmentObject.GetPropertyValue("InstanceName");
                                    if (wmiMonitors.TryGetValue(instanceName, out var physicalMonitor))
                                    {
                                        physicalMonitor.UpdateBrightness = (newBrightness) =>
                                        {
                                            newBrightness = Math.Clamp(physicalMonitor.Brightness.CurrentValue, physicalMonitor.Brightness.MinValue, physicalMonitor.Brightness.MaxValue);
                                            managmentObject.InvokeMethod("WmiSetBrightness", new object[] { int.MaxValue, newBrightness });
                                        };
                                    } 
                                    else
                                    {
                                        exceptions.Add(new Exception("Could not find WMI monitor when searching for it again"));
                                    }
                                }
                            }
                        }

                        foreach ((var instanceName, var physicalMonitor) in wmiMonitors)
                        {
                            callback(physicalMonitor);
                        }
                    }
                    catch (Exception e)
                    {
                        exceptions.Add(e);
                    }
                    continue;
                }

                uint minValue = 0, currentValue = 0, maxValue = 0;
                if (!NativeMethods.GetMonitorBrightness(monitor.physicalMonitorPtr, ref minValue, ref currentValue, ref maxValue))
                {
                    exceptions.Add(new Exception("Could not read monitor brightness"));
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

                    var physicalMonitor = new PhysicalMonitor(monitorBrightness, false);
                    physicalMonitor.UpdateBrightness = (newBrightness) =>
                    {
                        newBrightness = Math.Clamp(physicalMonitor.Brightness.CurrentValue, physicalMonitor.Brightness.MinValue, physicalMonitor.Brightness.MaxValue);

                        if (!NativeMethods.SetMonitorBrightness(monitor.physicalMonitorPtr, newBrightness))
                        {
                            throw new Exception("Failed to set monitor brightness");
                        }
                    };

                    callback(physicalMonitor);
                }
                catch (Exception e)
                {
                    exceptions.Add(e);
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(exceptions);
            }
        }

        public static void UpdateMonitorBrightnesses(NativeMethods.PHYSICAL_MONITOR[] physicalMonitors, Action<PhysicalMonitor> callback)
        {
            PhysicalMonitorsCallback(physicalMonitors, (v) =>
            {
                var currentValue = v.Brightness.CurrentValue;
                callback(v);

                if (v.IsWmi)
                {
                    if (v.Brightness.CurrentValue != currentValue)
                    {
                        v.UpdateBrightness(v.Brightness.CurrentValue);
                    }
                } 
                else
                {
                    if (v.Brightness.CurrentValue != currentValue)
                    {
                        v.UpdateBrightness(v.Brightness.CurrentValue);
                    }
                }
            });
        }

        public static void Destroy(NativeMethods.PHYSICAL_MONITOR[] physicalMonitors)
        {
            NativeMethods.DestroyPhysicalMonitors((uint)physicalMonitors.Length, ref physicalMonitors);
        }

        public class PhysicalMonitor
        {
            public BrightnessInfo Brightness { get; }

            public bool IsWmi { get; }

            public PhysicalMonitor(BrightnessInfo brightness, bool isWmi)
            {
                Brightness = brightness;
                IsWmi = isWmi;
            }

            // hm slightly ugly design
            internal Action<uint> UpdateBrightness;
        }

        public class BrightnessInfo
        {
            public uint MinValue { get; init; }
            public uint MaxValue { get; init; }
            public uint CurrentValue { get; set; }
        }
    }
}
