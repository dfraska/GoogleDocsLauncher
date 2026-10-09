using System.Runtime.InteropServices;
using System.Text;

namespace GoogleDocsLauncher;

internal static class Program {
    private const uint CMF_NORMAL = 0x00000000;
    private const int SW_SHOWNORMAL = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(
        IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuStringW(
        IntPtr hMenu, uint uIDItem, StringBuilder lpString, int cchMax, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint GetMenuItemID(IntPtr hMenu, int nPos);

    [STAThread]
    private static int Main(string[] args) {
        if (args.Length != 1) {
            MessageBoxW(IntPtr.Zero, "No document was supplied.", "Google Docs Launcher", 0x10);
            return 1;
        }

        string file = Path.GetFullPath(args[0]);

        if (!File.Exists(file)) {
            MessageBoxW(IntPtr.Zero, $"File does not exist:\n\n{file}", "Google Docs Launcher", 0x10);
            return 1;
        }

        if (!file.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) {
            MessageBoxW(IntPtr.Zero, $"This launcher only handles DOCX files:\n\n{file}",
                "Google Docs Launcher", 0x10);
            return 1;
        }

        LauncherConfig config;
        try {
            config = LauncherConfig.Load();
        } catch (Exception ex) {
            MessageBoxW(IntPtr.Zero,
                $"Couldn't load config.json:\n\n{ex.Message}",
                "Google Docs Launcher", 0x10);
            return 3;
        }

        string driveRoot = Path.GetFullPath(config.GoogleDriveRoot).TrimEnd(Path.DirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;

        if (!file.StartsWith(driveRoot, StringComparison.OrdinalIgnoreCase)) {
            MessageBoxW(IntPtr.Zero,
                $"This DOCX file is not in your Google Drive folder.\n\n{file}\n\n" +
                "Move it into Google Drive before opening it in Google Docs.",
                "Not in Google Drive", 0x30);
            return 2;
        }

        try {
            if (TryInvokeGoogleDocsContextMenu(file)) {
                return 0;
            }

            return WaitForGoogleDrive(file, config.RetryIntervalMs);
        } catch (Exception ex) {
            ShowError(file, ex.Message);
            return 3;
        }
    }

    private static int WaitForGoogleDrive(string file, int retryIntervalMs) {
        Application.EnableVisualStyles();

        using Form form = new() {
            Text = "Google Docs Launcher",
            Width = 420,
            Height = 150,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true
        };

        Label label = new() {
            Text = "Waiting for Google Drive to make this document available...\n\n" +
                   "This might take a minute.",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 65
        };

        Button cancel = new() {
            Text = "Cancel",
            Width = 90,
            Height = 28,
            Left = (form.ClientSize.Width - 90) / 2,
            Top = 70,
            Anchor = AnchorStyles.Bottom
        };

        bool succeeded = false;

        System.Windows.Forms.Timer timer = new() {
            Interval = retryIntervalMs
        };

        timer.Tick += (_, _) => {
            try {
                if (TryInvokeGoogleDocsContextMenu(file)) {
                    succeeded = true;
                    timer.Stop();
                    form.Close();
                }
            } catch (Exception ex) {
                timer.Stop();
                form.Close();
                ShowError(file, ex.Message);
            }
        };

        cancel.Click += (_, _) => {
            timer.Stop();
            form.Close();
        };

        form.Shown += (_, _) => timer.Start();
        form.Controls.Add(label);
        form.Controls.Add(cancel);

        Application.Run(form);
        return succeeded ? 0 : 4;
    }

    private static void ShowError(string file, string message) {
        MessageBoxW(IntPtr.Zero,
            $"Couldn't invoke Google Docs for:\n\n{file}\n\n{message}",
            "Google Docs Launcher", 0x10);
    }

    private static bool TryInvokeGoogleDocsContextMenu(string file) {
        IntPtr pidl = IntPtr.Zero;
        IntPtr parentPtr = IntPtr.Zero;
        IntPtr menu = IntPtr.Zero;

        try {
            int hr = SHParseDisplayName(file, IntPtr.Zero, out pidl, 0, out _);
            Marshal.ThrowExceptionForHR(hr);

            Guid iidShellFolder = typeof(IShellFolder).GUID;
            hr = SHBindToParent(pidl, ref iidShellFolder, out parentPtr, out IntPtr childPidl);
            Marshal.ThrowExceptionForHR(hr);

            IShellFolder parent = (IShellFolder)Marshal.GetObjectForIUnknown(parentPtr);
            Guid iidContextMenu = typeof(IContextMenu).GUID;
            IntPtr[] pidls = [childPidl];

            hr = parent.GetUIObjectOf(
                IntPtr.Zero, 1, pidls, ref iidContextMenu, IntPtr.Zero, out IntPtr contextMenuPtr);
            Marshal.ThrowExceptionForHR(hr);

            try {
                IContextMenu contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);

                menu = CreatePopupMenu();
                if (menu == IntPtr.Zero) {
                    throw new InvalidOperationException("Could not create a temporary context menu.");
                }

                hr = contextMenu.QueryContextMenu(menu, 0, 1, 0x7FFF, CMF_NORMAL);
                if (hr < 0) {
                    Marshal.ThrowExceptionForHR(hr);
                }

                int count = GetMenuItemCount(menu);

                for (int i = 0; i < count; i++) {
                    uint commandId = GetMenuItemID(menu, i);

                    if (commandId == 0xFFFFFFFF) {
                        continue;
                    }

                    StringBuilder text = new(256);
                    GetMenuStringW(menu, (uint)i, text, text.Capacity, 0x00000400);

                    string name = text.ToString().Replace("&", "").Trim();

                    if (name.Contains("Google Docs", StringComparison.OrdinalIgnoreCase)) {
                        InvokeCommand(contextMenu, commandId - 1);
                        return true;
                    }
                }

                return false;
            } finally {
                Marshal.Release(contextMenuPtr);
            }
        } finally {
            if (menu != IntPtr.Zero) {
                DestroyMenu(menu);
            }

            if (parentPtr != IntPtr.Zero) {
                Marshal.Release(parentPtr);
            }

            if (pidl != IntPtr.Zero) {
                CoTaskMemFree(pidl);
            }
        }
    }

    private static void InvokeCommand(IContextMenu contextMenu, uint commandOffset) {
        CMINVOKECOMMANDINFO info = new() {
            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
            fMask = 0,
            hwnd = IntPtr.Zero,
            lpVerb = (IntPtr)commandOffset,
            lpParameters = IntPtr.Zero,
            lpDirectory = IntPtr.Zero,
            nShow = SW_SHOWNORMAL,
            dwHotKey = 0,
            hIcon = IntPtr.Zero
        };

        int hr = contextMenu.InvokeCommand(ref info);
        Marshal.ThrowExceptionForHR(hr);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder {
        [PreserveSig]
        int ParseDisplayName(
            IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);

        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);

        [PreserveSig]
        int GetUIObjectOf(
            IntPtr hwndOwner, uint cidl,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]
            IntPtr[] apidl,
            ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);

        [PreserveSig]
        int SetNameOf(
            IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

        [PreserveSig]
        int GetCommandString(
            nuint idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFO {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
    }
}