using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace TyriaPad.Core.Native;

internal sealed record HidDeviceInfo(ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage, ushort InputReportLength, string Product);

/// <summary>Enumerates and opens HID devices read-only and shared: never takes one exclusively.</summary>
internal static class HidDevices
{
    public static List<string> EnumeratePaths()
    {
        var paths = new List<string>();
        Hid.HidD_GetHidGuid(out Guid hidGuid);
        nint set = Hid.SetupDiGetClassDevs(ref hidGuid, null, 0, Hid.DigcfPresent | Hid.DigcfDeviceInterface);
        if (set == -1)
        {
            return paths;
        }

        try
        {
            var data = new Hid.DeviceInterfaceData { Size = (uint)Marshal.SizeOf<Hid.DeviceInterfaceData>() };
            for (uint i = 0; Hid.SetupDiEnumDeviceInterfaces(set, 0, ref hidGuid, i, ref data); i++)
            {
                Hid.SetupDiGetDeviceInterfaceDetail(set, ref data, 0, 0, out uint size, 0);
                nint detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64 (DWORD + WCHAR[1] with alignment).
                    Marshal.WriteInt32(detail, 8);
                    if (Hid.SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, 0))
                    {
                        paths.Add(Marshal.PtrToStringUni(detail + 4) ?? "");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            Hid.SetupDiDestroyDeviceInfoList(set);
        }

        return paths;
    }

    public static unsafe HidDeviceInfo? Describe(string path)
    {
        // Access 0: allows querying attributes even of keyboards and mice, without reading them.
        using SafeFileHandle handle = Hid.CreateFile(path, 0, Hid.FileShareRead | Hid.FileShareWrite, 0, Hid.OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            return null;
        }

        var attributes = new Hid.Attributes { Size = (uint)sizeof(Hid.Attributes) };
        Hid.HidD_GetAttributes(handle, ref attributes);

        Hid.Caps caps = default;
        if (Hid.HidD_GetPreparsedData(handle, out nint preparsed))
        {
            if (Hid.HidP_GetCaps(preparsed, out caps) != Hid.HidpStatusSuccess)
            {
                caps = default;
            }

            Hid.HidD_FreePreparsedData(preparsed);
        }

        char* buffer = stackalloc char[128];
        string product = Hid.HidD_GetProductString(handle, buffer, 256) ? new string(buffer) : "?";
        return new HidDeviceInfo(attributes.VendorId, attributes.ProductId, caps.UsagePage, caps.Usage, caps.InputReportByteLength, product);
    }

    /// <summary>Opens for reading reports, shared with the rest of the system. Invalid handle on failure (see GetLastPInvokeError).</summary>
    public static SafeFileHandle OpenForRead(string path)
        => Hid.CreateFile(path, Hid.GenericRead, Hid.FileShareRead | Hid.FileShareWrite, 0, Hid.OpenExisting, 0, 0);
}
