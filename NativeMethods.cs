using System;
using System.Runtime.InteropServices;

namespace MonitorBrightness
{
    internal class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT virtualCursorPos);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT point, MonitorFromPointFlags flags);

        [DllImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitorPtr, ref uint numberOfPhysicalMonitors);

        [DllImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitorPtr, uint physicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] physicalMonitorArray);

        [DllImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorBrightness(IntPtr physicalMonitorPtr, ref uint minimumBrightness, ref uint currentBrightness, ref uint maxBrightness);

        [DllImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetMonitorBrightness(IntPtr physicalMonitorPtr, uint newBrightness);

        // Maybe: GetMonitorContrast
        // Maybe: GetMonitorRedGreenOrBlueDrive

        [DllImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyPhysicalMonitor(IntPtr physicalMonitorPtr);


        [DllImport("dxva2.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyPhysicalMonitors(uint physicalMonitorArraySize, ref PHYSICAL_MONITOR[] physicalMonitorArray);


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr physicalMonitorPtr;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string physicalMonitorDescription;
        }

        public enum MonitorFromPointFlags: uint
        {
            MONITOR_DEFAULTTONULL = 0,
            MONITOR_DEFAULTTOPRIMARY = 1,
            MONITOR_DEFAULTTONEAREST = 2
        }
    }
}
