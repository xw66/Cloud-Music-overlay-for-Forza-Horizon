using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HorizonRadioOverlay.Services;

public sealed class TrayIconService : IDisposable
{
    private const int NIM_ADD = 0x00;
    private const int NIM_MODIFY = 0x01;
    private const int NIM_DELETE = 0x02;
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIF_SHOWTIP = 0x80;
    private const int NOTIFYICON_VERSION_4 = 0x04;
    private const int NIM_SETVERSION = 0x04;

    private const int WM_USER_TRAYICON = 0x0400 + 1;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_BOTTOMALIGN = 0x0020;

    private NOTIFYICONDATAW _nid;
    private IntPtr _hWnd;
    private IntPtr _hIcon;
    private IntPtr _hMenu;
    private Action? _onDoubleClick;
    private bool _disposed;

    public void Initialize(IntPtr hWnd, string tipText, Action onDoubleClick)
    {
        _hWnd = hWnd;
        _onDoubleClick = onDoubleClick;

        _hIcon = LoadIconFromResource();

        _nid = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = hWnd,
            uID = 1,
            uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE | NIF_SHOWTIP,
            uCallbackMessage = WM_USER_TRAYICON,
            hIcon = _hIcon
        };
        SetTipText(tipText);

        Shell_NotifyIconW(NIM_ADD, ref _nid);
        Shell_NotifyIconW(NIM_SETVERSION, ref _nid);

        _hMenu = CreatePopupMenu();
    }

    public void SetVisible(bool visible)
    {
        if (visible)
            _nid.uFlags |= NIF_ICON;
        else
            _nid.uFlags &= ~NIF_ICON;
        Shell_NotifyIconW(NIM_MODIFY, ref _nid);
    }

    public void SetTipText(string text)
    {
        _nid.szTip = text.Length > 127 ? text.Substring(0, 127) : text;
        Shell_NotifyIconW(NIM_MODIFY, ref _nid);
    }

    public void ShowContextMenu()
    {
        if (_hMenu == IntPtr.Zero) return;

        while (GetMenuItemCount(_hMenu) > 0)
            RemoveMenu(_hMenu, 0, 0x0400);

        AppendMenuW(_hMenu, 0, 1, "显示主窗口");
        AppendMenuW(_hMenu, 0x00000800, 0, "");
        AppendMenuW(_hMenu, 0, 2, "退出");

        GetCursorPos(out POINT pt);
        TrackPopupMenu(_hMenu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN,
            pt.X, pt.Y, 0, _hWnd, IntPtr.Zero);
    }

    public void HandleMessage(int msg, IntPtr lParam)
    {
        if (msg != WM_USER_TRAYICON) return;

        int l = (int)lParam;
        if (l == WM_LBUTTONDBLCLK)
            _onDoubleClick?.Invoke();
        else if (l == WM_RBUTTONUP)
            ShowContextMenu();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Shell_NotifyIconW(NIM_DELETE, ref _nid);

        if (_hMenu != IntPtr.Zero)
        {
            DestroyMenu(_hMenu);
            _hMenu = IntPtr.Zero;
        }
        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    private static IntPtr LoadIconFromResource()
    {
        try
        {
            var asm = typeof(TrayIconService).Assembly;
            var stream = asm.GetManifestResourceStream("HorizonRadioOverlay.icon.ico");
            if (stream != null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                var icon = new System.Drawing.Icon(ms);
                return icon.Handle;
            }
        }
        catch { }

        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath))
            {
                var icon = new System.Drawing.Icon(iconPath);
                return icon.Handle;
            }
        }
        catch { }

        return IntPtr.Zero;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATAW pnid);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool AppendMenuW(IntPtr hMenu, int uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool RemoveMenu(IntPtr hMenu, int uPosition, int uFlags);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(IntPtr hMenu, int uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
