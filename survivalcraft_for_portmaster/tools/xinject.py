#!/usr/bin/env python3
"""Inject synthetic X11 input into the Survivalcraft window (XTest)."""
import ctypes, ctypes.util, sys, time

x11 = ctypes.CDLL("libX11.so.6")
xtst = ctypes.CDLL("libXtst.so.6")

x11.XOpenDisplay.restype = ctypes.c_void_p
x11.XDefaultRootWindow.restype = ctypes.c_ulong
x11.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
x11.XQueryTree.argtypes = [ctypes.c_void_p, ctypes.c_ulong,
                           ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_ulong),
                           ctypes.POINTER(ctypes.POINTER(ctypes.c_ulong)), ctypes.POINTER(ctypes.c_uint)]
x11.XFetchName.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_char_p)]
x11.XFree.argtypes = [ctypes.c_void_p]
x11.XSetInputFocus.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
x11.XWarpPointer.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_ulong,
                             ctypes.c_int, ctypes.c_int, ctypes.c_uint, ctypes.c_uint,
                             ctypes.c_int, ctypes.c_int]
x11.XFlush.argtypes = [ctypes.c_void_p]
x11.XStringToKeysym.argtypes = [ctypes.c_char_p]
x11.XStringToKeysym.restype = ctypes.c_ulong
x11.XKeysymToKeycode.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
x11.XKeysymToKeycode.restype = ctypes.c_ubyte
xtst.XTestFakeKeyEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
xtst.XTestFakeButtonEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
xtst.XTestFakeMotionEvent.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_ulong]

dpy = x11.XOpenDisplay(None)
if not dpy:
    sys.exit("cannot open display")

def window_name(w):
    name = ctypes.c_char_p()
    if x11.XFetchName(dpy, w, ctypes.byref(name)) and name.value:
        return name.value.decode("utf-8", "replace")
    return None

def find(w, target):
    n = window_name(w)
    if n == target:
        return w
    root = ctypes.c_ulong(); parent = ctypes.c_ulong()
    children = ctypes.POINTER(ctypes.c_ulong)(); nchildren = ctypes.c_uint()
    if x11.XQueryTree(dpy, w, ctypes.byref(root), ctypes.byref(parent),
                      ctypes.byref(children), ctypes.byref(nchildren)):
        for i in range(nchildren.value):
            r = find(children[i], target)
            if r:
                return r
    return None

root = x11.XDefaultRootWindow(dpy)
win = find(root, "Survivalcraft 2")
if not win:
    sys.exit("window not found")

print("window id:", hex(win))
x11.XSetInputFocus(dpy, win, 2, 0)
x11.XFlush(dpy)
time.sleep(0.2)

for action in sys.argv[1:]:
    kind, _, value = action.partition(":")
    if kind == "key":
        ks = x11.XStringToKeysym(value.encode())
        kc = x11.XKeysymToKeycode(dpy, ks)
        print(f"key {value} keysym={hex(ks)} keycode={kc}")
        xtst.XTestFakeKeyEvent(dpy, kc, 1, 0)
        x11.XFlush(dpy); time.sleep(0.15)
        xtst.XTestFakeKeyEvent(dpy, kc, 0, 0)
        x11.XFlush(dpy)
    elif kind == "move":
        x, y = (int(v) for v in value.split(","))
        x11.XWarpPointer(dpy, 0, win, 0, 0, 0, 0, x, y)
        x11.XFlush(dpy)
    elif kind == "click":
        x, y = (int(v) for v in value.split(","))
        x11.XWarpPointer(dpy, 0, win, 0, 0, 0, 0, x, y)
        x11.XFlush(dpy); time.sleep(0.15)
        xtst.XTestFakeButtonEvent(dpy, 1, 1, 0)
        x11.XFlush(dpy); time.sleep(0.15)
        xtst.XTestFakeButtonEvent(dpy, 1, 0, 0)
        x11.XFlush(dpy)
    time.sleep(0.2)

print("done")
