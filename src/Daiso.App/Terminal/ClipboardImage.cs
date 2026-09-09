using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Daiso.App.Terminal;

/// <summary>
/// 그림 파일을 시스템 클립보드에 CF_DIB 로 올린다. CLI(Claude Code·Codex·Gemini)가 그림을 읽을 때 쓰는 게 이 옛 형식이라
/// (PowerShell <c>Get-Clipboard -Format Image</c> 등), WinRT <c>DataPackage.SetBitmap</c> 으로 올린 스트림은 못 읽는다.
/// 32bpp BGRA, 위에서 아래로(높이 음수) 한 가지 꼴만 만든다.
/// </summary>
internal static class ClipboardImage
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp",
    };

    public static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>파일을 디코드해 클립보드에 올린다. 못 열거나 클립보드가 잠겨 있으면 false.</summary>
    public static async Task<bool> PutFileAsync(string path)
    {
        byte[] dib;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
            dib = ToDib(bitmap);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            return false;
        }

        return Put(dib);
    }

    private static byte[] ToDib(SoftwareBitmap bitmap)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var pixels = new byte[width * height * 4];
        bitmap.CopyToBuffer(pixels.AsBuffer());

        const int headerSize = 40; // BITMAPINFOHEADER
        var dib = new byte[headerSize + pixels.Length];
        var header = dib.AsSpan(0, headerSize);
        BitConverter.TryWriteBytes(header[0..4], headerSize);
        BitConverter.TryWriteBytes(header[4..8], width);
        BitConverter.TryWriteBytes(header[8..12], -height);   // 음수 = 첫 줄이 위
        BitConverter.TryWriteBytes(header[12..14], (short)1); // planes
        BitConverter.TryWriteBytes(header[14..16], (short)32); // bpp
        BitConverter.TryWriteBytes(header[16..20], 0);        // BI_RGB
        BitConverter.TryWriteBytes(header[20..24], pixels.Length);
        BitConverter.TryWriteBytes(header[24..28], 3780);     // 96 dpi
        BitConverter.TryWriteBytes(header[28..32], 3780);
        pixels.CopyTo(dib, headerSize);
        return dib;
    }

    private static bool Put(byte[] dib)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            EmptyClipboard();
            var handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)dib.Length);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var target = GlobalLock(handle);
            Marshal.Copy(dib, 0, target, dib.Length);
            GlobalUnlock(handle);

            if (SetClipboardData(CF_DIB, handle) == IntPtr.Zero)
            {
                GlobalFree(handle);
                return false;
            }

            return true; // 성공하면 메모리 소유가 시스템으로 넘어간다
        }
        finally
        {
            CloseClipboard();
        }
    }

    private const uint CF_DIB = 8;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);
}
