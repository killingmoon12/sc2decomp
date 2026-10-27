using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Engine;

/// <summary>
/// Resolves the two native libraries whose P/Invoke names are not the SONAMEs
/// installed on Linux devices. GPU/display libraries are never bundled: the
/// candidates only point at the CFW's own copies.
/// </summary>
internal static class NativeLibraryResolver
{
    [ModuleInitializer]
    internal static void Register()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // A resolver was already registered for this assembly.
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (string.IsNullOrEmpty(libraryName)) return IntPtr.Zero;

        if (libraryName.StartsWith("libGLESv2", StringComparison.Ordinal))
        {
            return ResolveGles(libraryName);
        }
        if (libraryName.StartsWith("libopenal", StringComparison.Ordinal))
        {
            return ResolveOpenAl(libraryName);
        }
        return IntPtr.Zero;
    }

    private static IntPtr ResolveGles(string requested)
    {
        // The engine asks for the unversioned name; devices ship the versioned
        // SONAME. Never load a bundled GLES driver.
        string[] candidates =
        {
            requested,
            "libGLESv2.so.2",
            "/usr/lib/aarch64-linux-gnu/libGLESv2.so.2",
            "/usr/lib/arm-linux-gnueabihf/libGLESv2.so.2",
            "/usr/lib/x86_64-linux-gnu/libGLESv2.so.2",
            "/usr/lib64/libGLESv2.so.2",
            "/usr/lib/libGLESv2.so.2",
            "/usr/local/lib/libGLESv2.so.2",
        };

        foreach (string candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out IntPtr handle)) return handle;
        }
        return FindLoaded("/proc/self/maps", "libGLESv2.so");
    }

    private static IntPtr ResolveOpenAl(string requested)
    {
        // The port ships OpenAL Soft as libopenal.so.1 in libs.$DEVICE_ARCH.
        // LD_LIBRARY_PATH is the normal lookup path; the explicit locations are
        // a fallback for launchers/tests that run without it.
        string[] fileNames = { requested, "libopenal.so.1", "libopenal.so" };
        string baseDir = AppContext.BaseDirectory;
        string parentDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
        string[] libDirs =
        {
            Path.Combine(baseDir, "libs.aarch64"),
            Path.Combine(baseDir, "libs.arm64"),
            Path.Combine(baseDir, "libs.x86_64"),
            Path.Combine(parentDir, "libs.aarch64"),
            Path.Combine(parentDir, "libs.arm64"),
            Path.Combine(parentDir, "libs.x86_64"),
            baseDir,
        };

        foreach (string dir in libDirs)
        {
            foreach (string fileName in fileNames)
            {
                string path = Path.Combine(dir, fileName);
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle)) return handle;
            }
        }

        string[] systemCandidates =
        {
            "libopenal.so.1",
            "/usr/lib/aarch64-linux-gnu/libopenal.so.1",
            "/usr/lib/arm-linux-gnueabihf/libopenal.so.1",
            "/usr/lib/x86_64-linux-gnu/libopenal.so.1",
            "/usr/lib64/libopenal.so.1",
            "/usr/lib/libopenal.so.1",
            "/usr/local/lib/libopenal.so.1",
        };

        foreach (string candidate in systemCandidates)
        {
            if (NativeLibrary.TryLoad(candidate, out IntPtr handle)) return handle;
        }
        return FindLoaded("/proc/self/maps", "libopenal.so");
    }

    private static IntPtr FindLoaded(string mapsPath, string marker)
    {
        try
        {
            foreach (string line in File.ReadLines(mapsPath))
            {
                int slash = line.IndexOf('/');
                if (slash < 0) continue;
                string path = line.Substring(slash).Trim();
                if (path.IndexOf(marker, StringComparison.Ordinal) < 0) continue;
                if (path.EndsWith("(deleted)", StringComparison.Ordinal)) continue;
                if (NativeLibrary.TryLoad(path, out IntPtr handle)) return handle;
            }
        }
        catch
        {
            // /proc is not available outside Linux; fall through.
        }
        return IntPtr.Zero;
    }
}
