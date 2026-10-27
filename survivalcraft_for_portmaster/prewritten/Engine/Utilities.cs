using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Engine;

public static class Utilities
{
	public static void Swap<T>(ref T a, ref T b) { T val = a; a = b; b = val; }
	public static int SizeOf<T>() => Marshal.SizeOf(typeof(T));
	public static T PtrToStructure<T>(nint ptr) => (T)Marshal.PtrToStructure(ptr, typeof(T));
	public static T ArrayToStructure<T>(Array array)
	{
		var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
		try { return PtrToStructure<T>(handle.AddrOfPinnedObject()); }
		finally { handle.Free(); }
	}
	public static byte[] StructureToArray<T>(T structure)
	{
		var array = new byte[SizeOf<T>()];
		var handle = GCHandle.Alloc(structure, GCHandleType.Pinned);
		try { Marshal.Copy(handle.AddrOfPinnedObject(), array, 0, array.Length); return array; }
		finally { handle.Free(); }
	}
	public static void Dispose<T>(ref T disposable) where T : IDisposable
	{
		if (disposable != null) { disposable.Dispose(); disposable = default; }
	}
	public static void DisposeCollection<T>(ICollection<T> collection) where T : IDisposable
	{
		if (collection == null) return;
		foreach (var item in collection) item?.Dispose();
		if (!collection.IsReadOnly) collection.Clear();
	}
	public static long GetTotalAvailableMemory()
	{
		try
		{
			var lines = File.ReadAllLines("/proc/meminfo");
			foreach (var line in lines)
			{
				if (line.StartsWith("MemAvailable:"))
				{
					var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length >= 2 && long.TryParse(parts[1], out long kb))
						return kb * 1024;
				}
			}
		}
		catch { }
		return long.MaxValue;
	}
}
