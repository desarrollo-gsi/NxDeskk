using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NxDesk.Host
{
    public static class InputSimulator
    {
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
            public static int Size => Marshal.SizeOf(typeof(INPUT));
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        const int INPUT_MOUSE = 0;
        const int INPUT_KEYBOARD = 1;

        const uint MOUSEEVENTF_MOVE = 0x0001;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        const uint MOUSEEVENTF_VIRTUALDESK = 0x4000; 
        const uint MOUSEEVENTF_WHEEL = 0x0800;

        const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);

        const int SM_XVIRTUALSCREEN = 76;
        const int SM_YVIRTUALSCREEN = 77;
        const int SM_CXVIRTUALSCREEN = 78;
        const int SM_CYVIRTUALSCREEN = 79;


        public static void SimulateMouseMove(double normalizedX, double normalizedY, int screenIndex = 0)
        {
            var screens = Screen.AllScreens;
            if (screenIndex >= screens.Length) screenIndex = 0;
            var bounds = screens[screenIndex].Bounds;

            int pixelX = bounds.X + (int)(normalizedX * bounds.Width);
            int pixelY = bounds.Y + (int)(normalizedY * bounds.Height);

            var input = CreateMouseInput(pixelX, pixelY, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);

            SendInputSafe(input);
        }

        public static void SimulateMouseDown(string button)
        {
            uint flag = 0;
            switch (button)
            {
                case "left": flag = MOUSEEVENTF_LEFTDOWN; break;
                case "right": flag = MOUSEEVENTF_RIGHTDOWN; break;
                case "middle": flag = MOUSEEVENTF_MIDDLEDOWN; break;
            }
            if (flag != 0) SendClick(flag);
        }

        public static void SimulateMouseUp(string button)
        {
            uint flag = 0;
            switch (button)
            {
                case "left": flag = MOUSEEVENTF_LEFTUP; break;
                case "right": flag = MOUSEEVENTF_RIGHTUP; break;
                case "middle": flag = MOUSEEVENTF_MIDDLEUP; break;
            }
            if (flag != 0) SendClick(flag);
        }

        public static void SimulateMouseWheel(int delta)
        {
            INPUT input = new INPUT { type = INPUT_MOUSE };
            input.u.mi.dwFlags = MOUSEEVENTF_WHEEL;
            input.u.mi.mouseData = (uint)delta;
            SendInputSafe(input);
        }

        public static void SimulateKeyEvent(byte virtualKeyCode, bool isKeyDown)
        {
            INPUT input = new INPUT { type = INPUT_KEYBOARD };
            input.u.ki.wVk = virtualKeyCode;
            input.u.ki.dwFlags = isKeyDown ? 0 : KEYEVENTF_KEYUP;
            SendInputSafe(input);
        }

        private static void SendClick(uint flag)
        {
            INPUT input = new INPUT { type = INPUT_MOUSE };
            input.u.mi.dwFlags = flag;

            if (!SendInputSafe(input))
            {
                Console.WriteLine($"[ERROR INPUT] Falló clic {flag}. Admin: {IsAdmin()}");
            }
        }

        private static INPUT CreateMouseInput(int x, int y, uint flags)
        {
            int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            int normX = (int)((x - vLeft) * 65535.0 / vWidth);
            int normY = (int)((y - vTop) * 65535.0 / vHeight);

            INPUT input = new INPUT { type = INPUT_MOUSE };
            input.u.mi.dx = normX;
            input.u.mi.dy = normY;
            input.u.mi.dwFlags = flags;
            return input;
        }

        private static bool SendInputSafe(INPUT input)
        {
            var result = SendInput(1, new INPUT[] { input }, INPUT.Size);
            if (result == 0)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == 5) Console.WriteLine("[INPUT BLOQUEADO] Windows bloqueó esta acción (UIPI).");
                return false;
            }
            return true;
        }

        private static bool IsAdmin()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                return new System.Security.Principal.WindowsPrincipal(identity)
                       .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }
    }
}