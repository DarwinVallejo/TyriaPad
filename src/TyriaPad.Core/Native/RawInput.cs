using System.Runtime.InteropServices;

namespace TyriaPad.Core.Native;

/// <summary>
/// Raw Input (user32), a message-only window to receive it, and HID report parsing
/// (hid.dll, HidP_*). With <see cref="RidevInputSink"/> Windows delivers input even when the app isn't
/// in the foreground, also in the Xbox full screen experience.
/// </summary>
internal static unsafe partial class RawInput
{
    public const uint WmClose = 0x0010;
    public const uint WmDestroy = 0x0002;
    public const uint WmInput = 0x00FF;
    public const uint WmInputDeviceChange = 0x00FE;
    public const uint RidInput = 0x10000003;
    public const uint RidiPreparsedData = 0x20000005;
    public const uint RidiDeviceName = 0x20000007;
    public const uint RidiDeviceInfo = 0x2000000B;
    public const uint RimTypeHid = 2;
    public const uint RidevRemove = 0x00000001;
    public const uint RidevInputSink = 0x00000100;
    public const uint RidevDevNotify = 0x00002000;
    public const nint GidcArrival = 1;
    public const int HidpInput = 0;
    public const int HidpStatusSuccess = 0x00110000;
    public static readonly nint HwndMessage = -3;

    [StructLayout(LayoutKind.Sequential)]
    public struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public nint WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDeviceList
    {
        public nint Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint WParam;
    }

    /// <summary>RID_DEVICE_INFO with the HID part of the union (the union is 24 bytes).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct RidDeviceInfo
    {
        [FieldOffset(0)]
        public uint Size;

        [FieldOffset(4)]
        public uint Type;

        [FieldOffset(8)]
        public uint VendorId;

        [FieldOffset(12)]
        public uint ProductId;

        [FieldOffset(16)]
        public uint Version;

        [FieldOffset(20)]
        public ushort UsagePage;

        [FieldOffset(22)]
        public ushort Usage;
    }

    /// <summary>HIDP_VALUE_CAPS (72 bytes); only the fields in use.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct HidpValueCaps
    {
        [FieldOffset(0)]
        public ushort UsagePage;

        [FieldOffset(12)]
        public byte IsRange;

        [FieldOffset(18)]
        public ushort BitSize;

        [FieldOffset(40)]
        public int LogicalMin;

        [FieldOffset(44)]
        public int LogicalMax;

        /// <summary>Usage (or UsageMin if it's a range).</summary>
        [FieldOffset(56)]
        public ushort Usage;

        [FieldOffset(58)]
        public ushort UsageMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(WndClassEx* windowClass);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(Msg* message, nint window, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(Msg* message);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(Msg* message);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices(RawInputDevice* devices, uint count, uint size);

    [LibraryImport("user32.dll")]
    public static partial uint GetRawInputData(nint rawInput, uint command, void* data, uint* size, uint headerSize);

    [LibraryImport("user32.dll")]
    public static partial uint GetRawInputDeviceList(RawInputDeviceList* list, uint* count, uint size);

    [LibraryImport("user32.dll")]
    public static partial uint GetRawInputDeviceInfoW(nint device, uint command, void* data, uint* size);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetCaps(void* preparsedData, HidpCaps* caps);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetValueCaps(int reportType, HidpValueCaps* caps, ushort* length, void* preparsedData);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, ushort* usages, uint* length, void* preparsedData, byte* report, uint reportLength);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, uint* value, void* preparsedData, byte* report, uint reportLength);

    /// <summary>Creates a message-only window (not visible, not in Alt+Tab) with that procedure.</summary>
    public static nint CreateMessageWindow(string className, delegate* unmanaged<nint, uint, nint, nint, nint> wndProc)
    {
        fixed (char* name = className)
        {
            var windowClass = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WndProc = (nint)wndProc,
                Instance = GetModuleHandleW(null),
                ClassName = name,
            };
            RegisterClassExW(&windowClass);
            return CreateWindowExW(0, name, name, 0, 0, 0, 0, 0, HwndMessage, 0, windowClass.Instance, 0);
        }
    }

    /// <summary>Thread message loop until WM_QUIT.</summary>
    public static void RunMessageLoop()
    {
        Msg message;
        while (GetMessageW(&message, 0, 0, 0) > 0)
        {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }

    /// <summary>Data of a WM_INPUT in a new buffer, or null if it isn't from an HID device.</summary>
    public static byte[]? ReadInput(nint handle, out nint device)
    {
        device = 0;
        uint headerSize = (uint)sizeof(RawInputHeader);
        uint size = 0;
        GetRawInputData(handle, RidInput, null, &size, headerSize);
        if (size == 0 || size > 8192)
        {
            return null;
        }

        byte[] buffer = new byte[size];
        fixed (byte* pointer = buffer)
        {
            if (GetRawInputData(handle, RidInput, pointer, &size, headerSize) != size)
            {
                return null;
            }

            var header = (RawInputHeader*)pointer;
            device = header->Device;
            return header->Type == RimTypeHid ? buffer : null;
        }
    }

    /// <summary>Last report of an HID WM_INPUT (RAWHID: size, report count and the reports back to back).</summary>
    public static ReadOnlySpan<byte> LastReport(byte[] input)
    {
        int headerSize = sizeof(RawInputHeader);
        if (input.Length < headerSize + 8)
        {
            return default;
        }

        uint reportSize = BitConverter.ToUInt32(input, headerSize);
        uint reportCount = BitConverter.ToUInt32(input, headerSize + 4);
        long end = headerSize + 8 + ((long)reportSize * reportCount);
        return reportSize == 0 || reportCount == 0 || end > input.Length
            ? default
            : input.AsSpan((int)(end - reportSize), (int)reportSize);
    }

    public static RidDeviceInfo? GetDeviceInfo(nint device)
    {
        var info = new RidDeviceInfo { Size = (uint)sizeof(RidDeviceInfo) };
        uint size = info.Size;
        return (int)GetRawInputDeviceInfoW(device, RidiDeviceInfo, &info, &size) > 0 && info.Type == RimTypeHid ? info : null;
    }

    public static string GetDeviceName(nint device)
    {
        uint size = 0;
        GetRawInputDeviceInfoW(device, RidiDeviceName, null, &size);
        if (size is 0 or > 1024)
        {
            return "?";
        }

        char* buffer = stackalloc char[(int)size];
        return (int)GetRawInputDeviceInfoW(device, RidiDeviceName, buffer, &size) > 0 ? new string(buffer).TrimEnd('\0') : "?";
    }

    /// <summary>VID/PID, collection and name of the device, for the log.</summary>
    public static string Describe(nint device)
        => GetDeviceInfo(device) is { } info
            ? $"VID {info.VendorId:X4} PID {info.ProductId:X4} page {info.UsagePage:X2} usage {info.Usage:X2} ({GetDeviceName(device)})"
            : GetDeviceName(device);

    /// <summary>Connected HID devices.</summary>
    public static List<nint> ListHidDevices()
    {
        uint count = 0;
        uint size = (uint)sizeof(RawInputDeviceList);
        var result = new List<nint>();
        if (GetRawInputDeviceList(null, &count, size) != 0 || count == 0)
        {
            return result;
        }

        var list = new RawInputDeviceList[count];
        fixed (RawInputDeviceList* pointer = list)
        {
            count = GetRawInputDeviceList(pointer, &count, size);
        }

        for (int i = 0; i < list.Length && i < (int)count; i++)
        {
            if (list[i].Type == RimTypeHid)
            {
                result.Add(list[i].Device);
            }
        }

        return result;
    }
}
