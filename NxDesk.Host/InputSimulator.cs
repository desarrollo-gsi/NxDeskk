using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NxDesk.Host
{
    public static class InputSimulator
    {
        private const int MOUSEEVENTF_MOVE = 0x0001;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const int MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const int MOUSEEVENTF_WHEEL = 0x0800;
        private const int MOUSEEVENTF_ABSOLUTE = 0x8000;

        private const int KEYEVENTF_KEYDOWN = 0x0000;
        private const int KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int X, int Y);

        // --- CORRECCIÓN AQUÍ: Agregar el parámetro opcional screenIndex ---
        public static void SimulateMouseMove(double normalizedX, double normalizedY, int screenIndex = 0)
        {
            var screens = Screen.AllScreens;

            // Validación simple para evitar índices fuera de rango
            if (screenIndex < 0 || screenIndex >= screens.Length)
            {
                screenIndex = 0;
            }

            var bounds = screens[screenIndex].Bounds;

            // Calcular la posición absoluta sumando el offset de la pantalla (bounds.X, bounds.Y)
            int absoluteX = bounds.X + (int)(normalizedX * bounds.Width);
            int absoluteY = bounds.Y + (int)(normalizedY * bounds.Height);

            SetCursorPos(absoluteX, absoluteY);
        }
        // ------------------------------------------------------------------

        public static void SimulateMouseDown(string button)
        {
            int flags = 0;
            switch (button)
            {
                case "left":
                    flags = MOUSEEVENTF_LEFTDOWN;
                    break;
                case "right":
                    flags = MOUSEEVENTF_RIGHTDOWN;
                    break;
                case "middle":
                    flags = MOUSEEVENTF_MIDDLEDOWN;
                    break;
            }
            if (flags != 0)
                mouse_event(flags, 0, 0, 0, 0);
        }

        public static void SimulateMouseUp(string button)
        {
            int flags = 0;
            switch (button)
            {
                case "left":
                    flags = MOUSEEVENTF_LEFTUP;
                    break;
                case "right":
                    flags = MOUSEEVENTF_RIGHTUP;
                    break;
                case "middle":
                    flags = MOUSEEVENTF_MIDDLEUP;
                    break;
            }
            if (flags != 0)
                mouse_event(flags, 0, 0, 0, 0);
        }

        public static void SimulateMouseWheel(int delta)
        {
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, delta, 0);
        }

        public static void SimulateKeyEvent(byte virtualKeyCode, bool isKeyDown)
        {
            int flags = isKeyDown ? KEYEVENTF_KEYDOWN : KEYEVENTF_KEYUP;
            keybd_event(virtualKeyCode, 0, flags, 0);
        }
    }
}