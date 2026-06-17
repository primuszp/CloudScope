using System.Runtime.InteropServices;

namespace CloudScope.Platform.Windows;

internal static class WindowsOpenFileDialog
{
    public static bool IsAvailable => OperatingSystem.IsWindows();

    public static string? PickLasFile()
    {
        if (!IsAvailable)
            return null;

        IntPtr fileBuffer = Marshal.AllocHGlobal(4096 * sizeof(char));
        IntPtr filter = Marshal.StringToHGlobalUni("LAS/LAZ point clouds\0*.las;*.laz\0All files\0*.*\0\0");
        IntPtr title = Marshal.StringToHGlobalUni("Open LAS point cloud");
        IntPtr defExt = Marshal.StringToHGlobalUni("las");

        try
        {
            Span<byte> bytes = new byte[4096 * sizeof(char)];
            Marshal.Copy(bytes.ToArray(), 0, fileBuffer, bytes.Length);

            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                lpstrFilter = filter,
                lpstrFile = fileBuffer,
                nMaxFile = 4096,
                lpstrTitle = title,
                lpstrDefExt = defExt,
                Flags =
                    OpenFileNameFlags.Explorer |
                    OpenFileNameFlags.FileMustExist |
                    OpenFileNameFlags.PathMustExist |
                    OpenFileNameFlags.NoChangeDir,
            };

            return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(fileBuffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
            Marshal.FreeHGlobal(filter);
            Marshal.FreeHGlobal(title);
            Marshal.FreeHGlobal(defExt);
        }
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OpenFileName openFileName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public OpenFileNameFlags Flags;
        public short nFileOffset;
        public short nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [Flags]
    private enum OpenFileNameFlags
    {
        NoChangeDir = 0x00000008,
        PathMustExist = 0x00000800,
        FileMustExist = 0x00001000,
        Explorer = 0x00080000,
    }
}
