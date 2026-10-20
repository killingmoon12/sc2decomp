#!/usr/bin/env python3
"""Grab a named X window's pixels via XGetImage and save as PNG."""
import ctypes, sys
from PIL import Image

x11 = ctypes.CDLL("libX11.so.6")
x11.XOpenDisplay.restype = ctypes.c_void_p
x11.XDefaultRootWindow.restype = ctypes.c_ulong
x11.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
x11.XQueryTree.argtypes = [ctypes.c_void_p, ctypes.c_ulong,
                           ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_ulong),
                           ctypes.POINTER(ctypes.POINTER(ctypes.c_ulong)), ctypes.POINTER(ctypes.c_uint)]
x11.XFetchName.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_char_p)]
x11.XGetImage.restype = ctypes.c_void_p
x11.XGetImage.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_int,
                          ctypes.c_uint, ctypes.c_uint, ctypes.c_ulong, ctypes.c_int]
x11.XGetPixel.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
x11.XGetPixel.restype = ctypes.c_ulong
x11.XDestroyImage.argtypes = [ctypes.c_void_p]
x11.XGetGeometry.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong),
                             ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int),
                             ctypes.POINTER(ctypes.c_uint), ctypes.POINTER(ctypes.c_uint),
                             ctypes.POINTER(ctypes.c_uint), ctypes.POINTER(ctypes.c_uint)]

dpy = x11.XOpenDisplay(None)
if not dpy:
    sys.exit("cannot open display")

def window_name(w):
    name = ctypes.c_char_p()
    if x11.XFetchName(dpy, w, ctypes.byref(name)) and name.value:
        return name.value.decode("utf-8", "replace")
    return None

def find(w, target):
    if window_name(w) == target:
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
win = find(root, sys.argv[1] if len(sys.argv) > 1 else "Survivalcraft 2")
if not win:
    sys.exit("window not found")
out = sys.argv[2] if len(sys.argv) > 2 else "/tmp/opencode/port/shot.png"

gr = ctypes.c_ulong(); xr = ctypes.c_int(); yr = ctypes.c_int()
wr = ctypes.c_uint(); hr = ctypes.c_uint(); bw = ctypes.c_uint(); dr = ctypes.c_uint()
x11.XGetGeometry(dpy, win, ctypes.byref(gr), ctypes.byref(xr), ctypes.byref(yr),
                 ctypes.byref(wr), ctypes.byref(hr), ctypes.byref(bw), ctypes.byref(dr))
wr = wr.value; hr = hr.value
img = x11.XGetImage(dpy, win, 0, 0, wr, hr, 0xFFFFFFFF, 2)
if not img:
    sys.exit("XGetImage failed")
buf = bytearray(wr * hr * 3)
for y in range(hr):
    for x in range(wr):
        px = x11.XGetPixel(img, x, y)
        i = (y * wr + x) * 3
        buf[i] = px & 0xFF
        buf[i + 1] = (px >> 8) & 0xFF
        buf[i + 2] = (px >> 16) & 0xFF
x11.XDestroyImage(img)
Image.frombytes("RGB", (wr, hr), bytes(buf)).save(out)
print(f"saved {out} ({wr}x{hr})")
