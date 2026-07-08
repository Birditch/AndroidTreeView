using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using AndroidTreeView.Mini.Models;

namespace AndroidTreeView.Mini.Services;

/// <summary>
/// Enumerates Android phones on the Windows USB bus with SetupAPI — no adb, no admin. This lets the Mini
/// tell "phone plugged in but USB debugging OFF" (guide the user) apart from "debugging ON" (adb handles
/// it), and identify the brand / approximate model even without adb access. Every failure is swallowed and
/// yields an empty list; this never throws.
/// </summary>
public static partial class AndroidUsb
{
    // Known Android OEM USB vendor ids (hex, upper-case) -> display brand.
    private static readonly IReadOnlyDictionary<string, string> Vendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["18D1"] = "Google",
        ["04E8"] = "Samsung",
        ["22B8"] = "Motorola",
        ["2717"] = "Xiaomi",
        ["2A70"] = "OnePlus",
        ["22D9"] = "OPPO",
        ["2D95"] = "vivo",
        ["12D1"] = "Huawei",
        ["1004"] = "LG",
        ["0FCE"] = "Sony",
        ["0BB4"] = "HTC",
        ["2A45"] = "Meizu",
        ["19D2"] = "ZTE",
        ["1BBB"] = "TCL/Alcatel",
        ["109B"] = "Hisense",
        ["0E8D"] = "MediaTek",
        ["05C6"] = "Qualcomm",
        ["2916"] = "Yulong",
        ["0489"] = "Foxconn/Sharp",
    };

    // The Android ADB USB interface signature — present only when USB debugging is enabled.
    private const string AdbInterfaceSignature = "Class_FF&SubClass_42&Prot_01";

    /// <summary>Scans present USB devices and returns the Android phones found (empty on non-Windows / any error).</summary>
    public static IReadOnlyList<UsbAndroidDevice> Scan()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<UsbAndroidDevice>();
        }

        try
        {
            return ScanCore();
        }
        catch
        {
            // Never let a native-interop hiccup crash the Mini.
            return Array.Empty<UsbAndroidDevice>();
        }
    }

    private sealed class RawEntry
    {
        public string InstanceId = string.Empty;
        public string? Vid;
        public string? Pid;
        public string? Name;
        public bool IsAdbInterface;
        public bool IsInterface; // instance id has "&MI_" (a composite child interface)
    }

    private static List<UsbAndroidDevice> ScanCore()
    {
        var results = new List<UsbAndroidDevice>();

        var handle = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (handle == IntPtr.Zero || handle == InvalidHandle)
        {
            return results;
        }

        try
        {
            var data = default(SP_DEVINFO_DATA);
            data.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

            var raws = new List<RawEntry>();
            for (uint i = 0; SetupDiEnumDeviceInfo(handle, i, ref data); i++)
            {
                var instanceId = GetInstanceId(handle, ref data);
                if (string.IsNullOrEmpty(instanceId))
                {
                    continue;
                }

                var (vid, pid) = ParseVidPid(instanceId);
                var hardware = GetMultiSz(handle, ref data, SPDRP_HARDWAREID);
                var compatible = GetMultiSz(handle, ref data, SPDRP_COMPATIBLEIDS);
                var name = GetString(handle, ref data, SPDRP_FRIENDLYNAME)
                           ?? GetString(handle, ref data, SPDRP_DEVICEDESC);

                var isAdb = hardware.Concat(compatible)
                    .Any(s => s.Contains(AdbInterfaceSignature, StringComparison.OrdinalIgnoreCase));

                raws.Add(new RawEntry
                {
                    InstanceId = instanceId,
                    Vid = vid,
                    Pid = pid,
                    Name = name,
                    IsAdbInterface = isAdb,
                    IsInterface = instanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase),
                });
            }

            // Aggregate to one entry per Android phone, grouped by VID+PID (a phone's composite parent and
            // its MI interfaces share the same VID+PID). The ADB interface can live on any child.
            foreach (var group in raws
                         .Where(r => r.Vid is not null && Vendors.ContainsKey(r.Vid))
                         .GroupBy(r => r.Vid + ":" + r.Pid))
            {
                var entries = group.ToList();
                var parent = entries.FirstOrDefault(e => !e.IsInterface) ?? entries[0];
                var vid = parent.Vid!;
                var serial = ExtractSerial(parent.InstanceId);
                var name = entries.Select(e => e.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                var hasAdb = entries.Any(e => e.IsAdbInterface);
                var manufacturer = Vendors.TryGetValue(vid, out var brand) ? brand : null;

                results.Add(new UsbAndroidDevice(serial ?? group.Key, manufacturer, name, serial, hasAdb));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(handle);
        }

        return results;
    }

    private static (string? vid, string? pid) ParseVidPid(string instanceId)
    {
        var match = VidPidRegex().Match(instanceId);
        return match.Success
            ? (match.Groups[1].Value.ToUpperInvariant(), match.Groups[2].Value.ToUpperInvariant())
            : (null, null);
    }

    private static string? ExtractSerial(string instanceId)
    {
        var idx = instanceId.LastIndexOf('\\');
        if (idx < 0 || idx + 1 >= instanceId.Length)
        {
            return null;
        }

        var last = instanceId[(idx + 1)..];
        // Windows-generated ids (device has no real iSerial) contain '&'; a real serial does not.
        return last.Contains('&') || last.Length < 3 ? null : last;
    }

    [GeneratedRegex(@"VID_([0-9A-Fa-f]{4}).*?PID_([0-9A-Fa-f]{4})")]
    private static partial Regex VidPidRegex();

    // ---- SetupAPI interop -------------------------------------------------------------------------

    private const uint DIGCF_PRESENT = 0x2;
    private const uint DIGCF_ALLCLASSES = 0x4;
    private const uint SPDRP_DEVICEDESC = 0x00000000;
    private const uint SPDRP_HARDWAREID = 0x00000001;
    private const uint SPDRP_COMPATIBLEIDS = 0x00000002;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA data, [Out] char[]? id, uint size, out uint required);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA data, uint property, out uint dataType, byte[]? buffer, uint bufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    private static string GetInstanceId(IntPtr handle, ref SP_DEVINFO_DATA data)
    {
        SetupDiGetDeviceInstanceId(handle, ref data, null, 0, out var need);
        if (need == 0)
        {
            return string.Empty;
        }

        var buffer = new char[need];
        return SetupDiGetDeviceInstanceId(handle, ref data, buffer, need, out _)
            ? new string(buffer).TrimEnd('\0')
            : string.Empty;
    }

    private static string? GetString(IntPtr handle, ref SP_DEVINFO_DATA data, uint property)
    {
        SetupDiGetDeviceRegistryProperty(handle, ref data, property, out _, null, 0, out var need);
        if (need == 0)
        {
            return null;
        }

        var buffer = new byte[need];
        if (!SetupDiGetDeviceRegistryProperty(handle, ref data, property, out _, buffer, need, out _))
        {
            return null;
        }

        var value = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string[] GetMultiSz(IntPtr handle, ref SP_DEVINFO_DATA data, uint property)
    {
        SetupDiGetDeviceRegistryProperty(handle, ref data, property, out _, null, 0, out var need);
        if (need == 0)
        {
            return Array.Empty<string>();
        }

        var buffer = new byte[need];
        if (!SetupDiGetDeviceRegistryProperty(handle, ref data, property, out _, buffer, need, out _))
        {
            return Array.Empty<string>();
        }

        return Encoding.Unicode.GetString(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
