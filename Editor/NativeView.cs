using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace AuroraView.Unity
{
    internal sealed class NativeView : IDisposable
    {
        private uint handle;
        private readonly StringBuilder buffer = new StringBuilder(65537);
        internal NativeView(IntPtr parent, string url, string dataDirectory)
        {
            handle = av_create(parent, url, dataDirectory);
            if (handle == 0) throw new InvalidOperationException("Native panel creation rejected the parent HWND or arguments.");
        }
        internal int State => av_state(handle);
        internal string Error { get { buffer.Clear(); av_error(handle, buffer, buffer.Capacity); return buffer.ToString(); } }
        internal void Place(IntPtr parent, Rect area, bool visible) => av_place(handle, parent,
            Mathf.RoundToInt(area.x), Mathf.RoundToInt(area.y), Mathf.RoundToInt(area.width), Mathf.RoundToInt(area.height), visible ? 1 : 0);
        internal bool Poll(out string json)
        {
            buffer.Clear();
            var length = av_poll(handle, buffer, buffer.Capacity);
            json = buffer.ToString();
            return length > 0;
        }
        internal void Evaluate(string script)
        {
            if (av_eval(handle, script) == 0) throw new InvalidOperationException("Native script queue is full or closed.");
        }
        public void Dispose() { if (handle != 0) { av_destroy(handle); handle = 0; } }
        private const string Library = "auroraview_unity";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern uint av_create(IntPtr parent, string url, string dataDirectory);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int av_state(uint handle);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void av_place(uint handle, IntPtr parent, int x, int y, int width, int height, int visible);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int av_poll(uint handle, StringBuilder text, int capacity);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int av_error(uint handle, StringBuilder text, int capacity);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int av_eval(uint handle, string script);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void av_destroy(uint handle);
    }

    internal static class HostWindow
    {
        internal static IntPtr Find(Rect screenArea)
        {
            var found = IntPtr.Zero;
            var smallestArea = long.MaxValue;
            var pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var owner);
                if (owner != pid || !IsWindowVisible(window) || !GetClientRect(window, out var rect)) return true;
                var origin = new Point();
                if (!ClientToScreen(window, ref origin)) return true;
                var area = (long)rect.right * rect.bottom;
                if (screenArea.center.x >= origin.x && screenArea.center.y >= origin.y &&
                    screenArea.center.x < origin.x + rect.right && screenArea.center.y < origin.y + rect.bottom && area < smallestArea)
                { found = window; smallestArea = area; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
        internal static Rect ToClient(IntPtr parent, Rect screenArea)
        {
            var origin = new Point(); ClientToScreen(parent, ref origin);
            return new Rect(screenArea.x - origin.x, screenArea.y - origin.y, screenArea.width, screenArea.height);
        }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int left, top, right, bottom; }
        private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Bounds rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    }
}
