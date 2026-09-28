using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TyriaPad.App;

/// <summary>The TyriaPad icon (Assets/TyriaPad.ico, embedded in the executable).</summary>
internal static class AppIcon
{
    private const string ResourceName = "TyriaPad.App.TyriaPad.ico";

    /// <summary>For the tray, at Windows' small size (16 px at 100%, 24 px at 150%).</summary>
    public static Drawing.Icon CreateTrayIcon()
    {
        using Stream stream = Open();
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    /// <summary>For the WPF windows: with every size, WPF picks the one for the title bar and the one for the taskbar.</summary>
    public static ImageSource Image { get; } = LoadImage();

    private static ImageSource LoadImage()
    {
        using Stream stream = Open();
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
    }

    private static Stream Open() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException($"Missing resource {ResourceName}");
}
