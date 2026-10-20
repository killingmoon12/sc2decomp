using System;
using System.Runtime.InteropServices;

namespace OpenTK.Platform.Linux
{
    internal static class X11
    {
        public const string Lib = "libX11.so.6";

        public const int ExposureMask = 0x8000;
        public const int KeyPressMask = 0x01;
        public const int KeyReleaseMask = 0x02;
        public const int ButtonPressMask = 0x04;
        public const int ButtonReleaseMask = 0x08;
        public const int PointerMotionMask = 0x40;
        public const int StructureNotifyMask = 0x20000;
        public const int FocusChangeMask = 0x200000;
        public const int CWBackPixel = 0x0002;
        public const int CWBorderPixel = 0x0008;
        public const int CWEventMask = 0x0800;
        public const int CWColormap = 0x2000;
        public const int InputOutput = 1;
        public const int AllocNone = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct XSetWindowAttributes
        {
            public IntPtr background_pixmap, background_pixel, border_pixmap, border_pixel;
            public int bit_gravity, win_gravity, backing_store;
            public IntPtr backing_planes, backing_pixel;
            public int save_under;
            public IntPtr event_mask, do_not_propagate_mask;
            public int override_redirect;
            public IntPtr colormap, cursor;
        }

        // Layout of Xlib's XVisualInfo (LP64): Visual*, VisualID, ints.
        [StructLayout(LayoutKind.Sequential)]
        public struct XVisualInfo
        {
            public IntPtr visual;
            public ulong visualid;
            public int screen;
            public int depth;
            public int @class;
            public ulong red_mask, green_mask, blue_mask;
            public int colormap_size;
            public int bits_per_rgb;
        }

        // Xlib's XEvent is a union padded to 24 longs (192 bytes on LP64).
        // XNextEvent() writes the full XEvent, so the size must match exactly.
        [StructLayout(LayoutKind.Sequential, Size = 192)]
        public struct XEvent
        {
            public int type;
            public IntPtr serial;
            public bool send_event;
            public IntPtr display, window, root, subwindow;
            public int time, x, y, x_root, y_root, state, keycode;
            public bool same_screen;
        }

        public const int KeyPress = 2;
        public const int KeyRelease = 3;
        public const int ButtonPress = 4;
        public const int ButtonRelease = 5;
        public const int MotionNotify = 6;
        public const int ClientMessage = 33;
        public const int MapNotify = 19;
        public const int ConfigureNotify = 22;
        public const int FocusIn = 9;
        public const int FocusOut = 10;

        // Xlib event layouts (LP64). Offsets are explicit because the XEvent
        // struct above only matches the first fields; here we need x/y/state/button.
        [StructLayout(LayoutKind.Explicit, Size = 192)]
        public struct XKeyEvent
        {
            [FieldOffset(0)] public int type;
            [FieldOffset(64)] public int x;
            [FieldOffset(68)] public int y;
            [FieldOffset(80)] public uint state;
            [FieldOffset(84)] public uint keycode;
        }

        [StructLayout(LayoutKind.Explicit, Size = 192)]
        public struct XButtonEvent
        {
            [FieldOffset(0)] public int type;
            [FieldOffset(64)] public int x;
            [FieldOffset(68)] public int y;
            [FieldOffset(80)] public uint state;
            [FieldOffset(84)] public uint button;
        }

        [StructLayout(LayoutKind.Explicit, Size = 192)]
        public struct XMotionEvent
        {
            [FieldOffset(0)] public int type;
            [FieldOffset(64)] public int x;
            [FieldOffset(68)] public int y;
            [FieldOffset(80)] public uint state;
        }

        // XConfigureEvent (LP64): type 0, serial 8, send_event 16, display 24,
        // event 32, window 40, x 48, y 52, width 56, height 60.
        [StructLayout(LayoutKind.Explicit, Size = 192)]
        public struct XConfigureEvent
        {
            [FieldOffset(0)] public int type;
            [FieldOffset(40)] public IntPtr window;
            [FieldOffset(48)] public int x;
            [FieldOffset(52)] public int y;
            [FieldOffset(56)] public int width;
            [FieldOffset(60)] public int height;
        }

        [DllImport(Lib)] public static extern IntPtr XOpenDisplay(IntPtr display_name);
        [DllImport(Lib)] public static extern int XCloseDisplay(IntPtr display);
        [DllImport(Lib)] public static extern int XDefaultScreen(IntPtr display);
        [DllImport(Lib)] public static extern IntPtr XRootWindow(IntPtr display, int screen);
        [DllImport(Lib)] public static extern IntPtr XCreateColormap(IntPtr display, IntPtr window, IntPtr visual, int alloc);
        [DllImport(Lib)] public static extern IntPtr XCreateWindow(IntPtr display, IntPtr parent, int x, int y, int width, int height, int border_width, int depth, int class_, IntPtr visual, int valuemask, ref XSetWindowAttributes attributes);
        [DllImport(Lib)] public static extern int XDestroyWindow(IntPtr display, IntPtr window);
        [DllImport(Lib)] public static extern int XMapWindow(IntPtr display, IntPtr window);
        [DllImport(Lib)] public static extern int XStoreName(IntPtr display, IntPtr window, string name);
        [DllImport(Lib)] public static extern int XFlush(IntPtr display);
        [DllImport(Lib)] public static extern int XPending(IntPtr display);
        [DllImport(Lib)] public static extern int XNextEvent(IntPtr display, IntPtr event_return);
        [DllImport(Lib)] public static extern int XMoveResizeWindow(IntPtr display, IntPtr window, int x, int y, int width, int height);
        [DllImport(Lib)] public static extern ulong XLookupKeysym(IntPtr key_event, int index);
        [DllImport(Lib)] public static extern int XSetInputFocus(IntPtr display, IntPtr window, int revert_to, ulong time);
        [DllImport(Lib)] public static extern int XWarpPointer(IntPtr display, IntPtr src_w, IntPtr dest_w, int src_x, int src_y, int src_width, int src_height, int dest_x, int dest_y);
        [DllImport(Lib)] public static extern IntPtr XCreateBitmapFromData(IntPtr display, IntPtr drawable, byte[] data, int width, int height);
        [DllImport(Lib)] public static extern IntPtr XCreatePixmapCursor(IntPtr display, IntPtr source, IntPtr mask, ref XColor foreground, ref XColor background, int x, int y);
        [DllImport(Lib)] public static extern int XDefineCursor(IntPtr display, IntPtr window, IntPtr cursor);
        [DllImport(Lib)] public static extern int XUndefineCursor(IntPtr display, IntPtr window);
        [DllImport(Lib)] public static extern int XFreeCursor(IntPtr display, IntPtr cursor);
        [DllImport(Lib)] public static extern int XFreePixmap(IntPtr display, IntPtr pixmap);
        [DllImport(Lib)] public static extern int XGrabPointer(IntPtr display, IntPtr grab_window, int owner_events, uint event_mask, int pointer_mode, int grab_mode, IntPtr confine_to, IntPtr cursor, ulong time);
        [DllImport(Lib)] public static extern int XUngrabPointer(IntPtr display, ulong time);

        [StructLayout(LayoutKind.Sequential)]
        public struct XColor
        {
            public ulong pixel;
            public ushort red, green, blue;
            public byte flags, pad;
        }

        public const int GrabModeAsync = 1;
    }
}
