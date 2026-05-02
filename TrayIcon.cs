using System;
using System.Runtime.InteropServices;

namespace Clipora
{
    internal sealed class TrayIcon : IDisposable
    {
        private const int WM_APP = 0x8000;
        private const int WM_TRAYICON = WM_APP + 1;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_DELETE = 0x00000002;

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;

        private const int WM_COMMAND = 0x0111;

        private const int GWL_WNDPROC = -4;

        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_RETURNCMD = 0x0100;

        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;

        private const uint MIIM_ID = 0x00000002;
        private const uint MIIM_STRING = 0x00000040;
        private const uint MIIM_FTYPE = 0x00000100;

        private const uint MFT_STRING = 0x00000000;
        private const uint MFT_SEPARATOR = 0x00000800;

        private const uint WM_NULL = 0x0000;

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x0010;
        private const uint LR_DEFAULTSIZE = 0x0040;

        private const uint SW_SHOWNORMAL = 1;

        private const uint MENU_OPEN = 1001;
        private const uint MENU_EXIT = 1002;

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const int VK_V = 0x56;
        private const int HOTKEY_ID = 9000;

        private readonly IntPtr _hWnd;
        private readonly Action _onOpen;
        private readonly Action _onExit;
        private readonly Action? _onHotkey;

        private IntPtr _hIcon;
        private bool _added;
        private bool _hotkeyRegistered;

        private IntPtr _oldWndProc;
        private WndProcDelegate? _newWndProc;

        private IntPtr _hMenu;

        public TrayIcon(IntPtr hWnd, string tooltip, string iconPath, Action onOpen, Action onExit, Action? onHotkey = null)
        {
            _hWnd = hWnd;
            _onOpen = onOpen;
            _onExit = onExit;
            _onHotkey = onHotkey;

            HookWndProc();

            if (_onHotkey != null)
            {
                _hotkeyRegistered = RegisterHotKey(_hWnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_V);
            }

            _hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (_hIcon == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to load icon: {iconPath}");

            var nid = new NOTIFYICONDATA();
            nid.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>();
            nid.hWnd = _hWnd;
            nid.uID = 1;
            nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            nid.uCallbackMessage = WM_TRAYICON;
            nid.hIcon = _hIcon;
            nid.szTip = tooltip ?? "";

            if (!Shell_NotifyIcon(NIM_ADD, ref nid))
                throw new InvalidOperationException("Shell_NotifyIcon(NIM_ADD) failed.");

            _added = true;

            BuildMenu();
        }

        private void BuildMenu()
        {
            _hMenu = CreatePopupMenu();
            if (_hMenu == IntPtr.Zero)
                return;

            InsertMenuItem(_hMenu, 0, true, new MENUITEMINFO
            {
                cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                fMask = MIIM_FTYPE | MIIM_ID | MIIM_STRING,
                fType = MFT_STRING,
                wID = MENU_OPEN,
                dwTypeData = "Open"
            });

            InsertMenuItem(_hMenu, 1, true, new MENUITEMINFO
            {
                cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                fMask = MIIM_FTYPE,
                fType = MFT_SEPARATOR
            });

            InsertMenuItem(_hMenu, 2, true, new MENUITEMINFO
            {
                cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                fMask = MIIM_FTYPE | MIIM_ID | MIIM_STRING,
                fType = MFT_STRING,
                wID = MENU_EXIT,
                dwTypeData = "Exit"
            });
        }

        private void HookWndProc()
        {
            _newWndProc = WndProc;
            _oldWndProc = SetWindowLongPtr(_hWnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if ((int)msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                _onHotkey?.Invoke();
                return IntPtr.Zero;
            }

            if ((int)msg == WM_TRAYICON)
            {
                int mouseMsg = lParam.ToInt32();

                if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK)
                {
                    _onOpen?.Invoke();
                    return IntPtr.Zero;
                }

                if (mouseMsg == WM_RBUTTONUP)
                {
                    ShowMenu();
                    return IntPtr.Zero;
                }
            }
            else if ((int)msg == WM_COMMAND)
            {
                uint cmd = (uint)(wParam.ToInt64() & 0xFFFF);

                if (cmd == MENU_OPEN)
                {
                    _onOpen?.Invoke();
                    return IntPtr.Zero;
                }
                if (cmd == MENU_EXIT)
                {
                    _onExit?.Invoke();
                    return IntPtr.Zero;
                }
            }

            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        private void ShowMenu()
        {
            if (_hMenu == IntPtr.Zero) return;

            GetCursorPos(out POINT pt);

            SetForegroundWindow(_hWnd);

            uint cmd = TrackPopupMenuEx(
                _hMenu,
                TPM_RIGHTBUTTON | TPM_RETURNCMD,
                pt.X,
                pt.Y,
                _hWnd,
                IntPtr.Zero);

            if (cmd != 0)
            {

                PostMessage(_hWnd, WM_COMMAND, (IntPtr)cmd, IntPtr.Zero);
            }

            PostMessage(_hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }

        public void Dispose()
        {
            try
            {
                if (_hotkeyRegistered)
                {
                    UnregisterHotKey(_hWnd, HOTKEY_ID);
                    _hotkeyRegistered = false;
                }

                if (_added)
                {
                    var nid = new NOTIFYICONDATA();
                    nid.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>();
                    nid.hWnd = _hWnd;
                    nid.uID = 1;
                    Shell_NotifyIcon(NIM_DELETE, ref nid);
                    _added = false;
                }

                if (_hIcon != IntPtr.Zero)
                {
                    DestroyIcon(_hIcon);
                    _hIcon = IntPtr.Zero;
                }

                if (_hMenu != IntPtr.Zero)
                {
                    DestroyMenu(_hMenu);
                    _hMenu = IntPtr.Zero;
                }

                if (_oldWndProc != IntPtr.Zero)
                {
                    SetWindowLongPtr(_hWnd, GWL_WNDPROC, _oldWndProc);
                    _oldWndProc = IntPtr.Zero;
                }
            }
            catch { }
        }

        // ===== P/Invoke =====

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            public uint dwState;
            public uint dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;

            public uint uTimeoutOrVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;

            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MENUITEMINFO
        {
            public uint cbSize;
            public uint fMask;
            public uint fType;
            public uint fState;
            public uint wID;
            public IntPtr hSubMenu;
            public IntPtr hbmpChecked;
            public IntPtr hbmpUnchecked;
            public IntPtr dwItemData;
            public string? dwTypeData;
            public uint cch;
            public IntPtr hbmpItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool InsertMenuItem(IntPtr hMenu, uint item, bool fByPosition, in MENUITEMINFO mii);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}