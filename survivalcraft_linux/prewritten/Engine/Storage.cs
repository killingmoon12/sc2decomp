using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Engine;

public static class Storage
{
	/// <summary>Folder created under ~/.config that holds everything the game persists.</summary>
	public const string DataFolderName = "Survivalcraft2";

	/// <summary>Optional override for the data root (used by the launcher/tests).</summary>
	public const string DataFolderEnvironmentVariable = "SC_DATA_DIR";

	private static string m_dataRoot;

	/// <summary>
	/// Directory containing worlds, settings, texture packs, logs and everything
	/// else the game writes. Defaults to ~/.config/Survivalcraft2, honouring
	/// XDG_CONFIG_HOME and SC_DATA_DIR when they are set.
	/// </summary>
	public static string DataRoot
	{
		get
		{
			if (m_dataRoot == null)
			{
				m_dataRoot = ResolveDataRoot();
				MigrateLegacyData(m_dataRoot);
			}
			return m_dataRoot;
		}
	}

	private static string ResolveDataRoot()
	{
		string root = Environment.GetEnvironmentVariable(DataFolderEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(root))
		{
			string configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
			if (string.IsNullOrWhiteSpace(configHome))
			{
				// SpecialFolder.ApplicationData is ~/.config on Linux.
				configHome = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			}
			root = Path.Combine(configHome.TrimEnd(Path.DirectorySeparatorChar), DataFolderName);
		}
		try { Directory.CreateDirectory(root); }
		catch (Exception ex) { throw new IOException($"Cannot create data directory \"{root}\".", ex); }
		return root;
	}

	/// <summary>
	/// Earlier Linux builds wrote into LocalApplicationData directly (without an
	/// application folder), so ~/ Worlds, Settings.xml and friends were scattered
	/// across ~/.local/share. Move them into the new root once.
	/// </summary>
	private static void MigrateLegacyData(string root)
	{
		try
		{
			string legacyRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			if (string.IsNullOrWhiteSpace(legacyRoot) || !Directory.Exists(legacyRoot))
			{
				return;
			}
			string[] legacyDirectories = { "Worlds", "TexturePacks", "CharacterSkins", "FurniturePacks" };
			string[] legacyFiles = { "Settings.xml", "UserId.dat", "CommunityContentCache.xml" };
			foreach (string name in legacyDirectories)
			{
				MigrateDirectory(Path.Combine(legacyRoot, name), Path.Combine(root, name));
			}
			foreach (string name in legacyFiles)
			{
				MigrateFile(Path.Combine(legacyRoot, name), Path.Combine(root, name));
			}
			// The log directory name is generic, so only take the game's own file.
			MigrateFile(Path.Combine(legacyRoot, "Logs", "Game.log"), Path.Combine(root, "Logs", "Game.log"));
			string legacyLogs = Path.Combine(legacyRoot, "Logs");
			if (Directory.Exists(legacyLogs) && !Directory.EnumerateFileSystemEntries(legacyLogs).Any())
			{
				Directory.Delete(legacyLogs);
			}
		}
		catch
		{
			// Migration must never stop the game from starting; the worst case is
			// that old data stays where it is and can be moved by hand.
		}
	}

	private static void MigrateDirectory(string source, string destination)
	{
		if (!Directory.Exists(source)) return;
		if (!Directory.Exists(destination))
		{
			string parent = Path.GetDirectoryName(destination) ?? string.Empty;
			if (parent.Length > 0) Directory.CreateDirectory(parent);
			Directory.Move(source, destination);
			return;
		}
		foreach (string child in Directory.GetDirectories(source))
		{
			MigrateDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
		}
		foreach (string child in Directory.GetFiles(source))
		{
			MigrateFile(child, Path.Combine(destination, Path.GetFileName(child)));
		}
		if (!Directory.EnumerateFileSystemEntries(source).Any())
		{
			Directory.Delete(source);
		}
	}

	private static void MigrateFile(string source, string destination)
	{
		if (!File.Exists(source) || File.Exists(destination)) return;
		string parent = Path.GetDirectoryName(destination) ?? string.Empty;
		if (parent.Length > 0) Directory.CreateDirectory(parent);
		File.Move(source, destination);
	}

	public static long FreeSpace
	{
		get
		{
			try
			{
				var drives = DriveInfo.GetDrives();
				long totalFree = 0;
				foreach (var drive in drives)
				{
					if (drive.IsReady) totalFree += drive.AvailableFreeSpace;
				}
				return totalFree > 0 ? totalFree : long.MaxValue;
			}
			catch { return long.MaxValue; }
		}
	}

	public static bool FileExists(string path) => File.Exists(ProcessPath(path, false, true));
	public static bool DirectoryExists(string path) => Directory.Exists(ProcessPath(path, false, true));
	public static long GetFileSize(string path) => new FileInfo(ProcessPath(path, false, true)).Length;
	public static DateTime GetFileLastWriteTime(string path) => File.GetLastWriteTimeUtc(ProcessPath(path, false, true));

	public static Stream OpenFile(string path, OpenFileMode openFileMode)
	{
		if (openFileMode is not (OpenFileMode.Read or OpenFileMode.ReadWrite or OpenFileMode.Create or OpenFileMode.CreateOrOpen))
			throw new ArgumentException("openFileMode");
		string fullPath = ProcessPath(path, openFileMode != OpenFileMode.Read, false, out bool isApp);
		if (isApp)
		{
			var assembly = typeof(Storage).Assembly;
			var stream = assembly.GetManifestResourceStream(fullPath);
			if (stream == null) throw new FileNotFoundException($"Embedded resource not found: {fullPath}");
			return stream;
		}
		FileMode mode = openFileMode switch
		{
			OpenFileMode.Create => FileMode.Create,
			OpenFileMode.CreateOrOpen => FileMode.OpenOrCreate,
			_ => FileMode.Open,
		};
		FileAccess access = openFileMode == OpenFileMode.Read ? FileAccess.Read : FileAccess.ReadWrite;
		return File.Open(fullPath, mode, access, FileShare.Read);
	}

	public static void DeleteFile(string path) => File.Delete(ProcessPath(path, true, true));
	public static void CopyFile(string sourcePath, string destinationPath)
	{
		using var src = OpenFile(sourcePath, OpenFileMode.Read);
		using var dst = OpenFile(destinationPath, OpenFileMode.Create);
		src.CopyTo(dst);
	}

	public static void MoveFile(string sourcePath, string destinationPath)
	{
		string src = ProcessPath(sourcePath, true, true);
		string dst = ProcessPath(destinationPath, true, true);
		File.Delete(dst);
		File.Move(src, dst);
	}

	public static void CreateDirectory(string path) => Directory.CreateDirectory(ProcessPath(path, true, true));
	public static void DeleteDirectory(string path) => Directory.Delete(ProcessPath(path, true, true));
	public static void DeleteDirectoryRecursive(string path)
	{
		foreach (var item in ListDirectoryNames(path)) DeleteDirectoryRecursive(CombinePaths(path, item));
		foreach (var item in ListFileNames(path)) DeleteFile(CombinePaths(path, item));
		DeleteDirectory(path);
	}
	public static void MoveDirectory(string sourcePath, string destinationPath)
	{
		string src = ProcessPath(sourcePath, true, true);
		string dst = ProcessPath(destinationPath, true, true);
		if (Path.GetDirectoryName(src) != Path.GetDirectoryName(dst))
			throw new IOException("Moving directories not supported, only renaming.");
		Directory.Move(src, dst);
	}

	public static IEnumerable<string> ListFileNames(string path) => Directory.EnumerateFiles(ProcessPath(path, false, true)).Select(Path.GetFileName);
	public static IEnumerable<string> ListDirectoryNames(string path) => Directory.EnumerateDirectories(ProcessPath(path, false, true)).Select(Path.GetFileName).Where(s => s != ".__override__");

	public static string ReadAllText(string path) => ReadAllText(path, Encoding.UTF8);
	public static string ReadAllText(string path, Encoding encoding)
	{
		using var reader = new StreamReader(OpenFile(path, OpenFileMode.Read), encoding);
		return reader.ReadToEnd();
	}
	public static void WriteAllText(string path, string text) => WriteAllText(path, text, Encoding.UTF8);
	public static void WriteAllText(string path, string text, Encoding encoding)
	{
		using var writer = new StreamWriter(OpenFile(path, OpenFileMode.Create), encoding);
		writer.Write(text);
	}
	public static byte[] ReadAllBytes(string path)
	{
		using var reader = new BinaryReader(OpenFile(path, OpenFileMode.Read));
		return reader.ReadBytes((int)reader.BaseStream.Length);
	}
	public static void WriteAllBytes(string path, byte[] bytes)
	{
		using var writer = new BinaryWriter(OpenFile(path, OpenFileMode.Create));
		writer.Write(bytes);
	}

	public static string GetSystemPath(string path) => ProcessPath(path, false, true);
	public static string GetExtension(string path) => Path.GetExtension(path);
	public static string GetFileName(string path) => Path.GetFileName(path);
	public static string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);
	public static string GetDirectoryName(string path) => Path.GetDirectoryName(path) ?? string.Empty;

	public static string CombinePaths(params string[] paths)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < paths.Length; i++)
		{
			if (paths[i].Length > 0)
			{
				sb.Append(paths[i]);
				if (i < paths.Length - 1 && (sb.Length == 0 || sb[^1] != Path.DirectorySeparatorChar))
					sb.Append(Path.DirectorySeparatorChar);
			}
		}
		return sb.ToString();
	}
	public static string ChangeExtension(string path, string extension) => CombinePaths(GetDirectoryName(path), GetFileNameWithoutExtension(path)) + extension;

	private static string ProcessPath(string path, bool writeAccess, bool failIfApp)
	{
		if (path == null) throw new ArgumentNullException(nameof(path));
		string processed = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
		if (processed.StartsWith("app:"))
		{
			if (failIfApp) throw new InvalidOperationException($"Access denied to \"{path}\".");
			return processed.Substring(4).TrimStart(Path.DirectorySeparatorChar);
		}
		if (processed.StartsWith("data:"))
		{
			return Path.Combine(DataRoot, processed.Substring(5).TrimStart(Path.DirectorySeparatorChar));
		}
		return processed;
	}

	private static string ProcessPath(string path, bool writeAccess, bool failIfApp, out bool isApp)
	{
		isApp = false;
		if (path == null) throw new ArgumentNullException(nameof(path));
		string processed = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
		if (processed.StartsWith("app:"))
		{
			if (failIfApp) throw new InvalidOperationException($"Access denied to \"{path}\".");
			isApp = true;
			return processed.Substring(4).TrimStart(Path.DirectorySeparatorChar);
		}
		if (processed.StartsWith("data:"))
		{
			return Path.Combine(DataRoot, processed.Substring(5).TrimStart(Path.DirectorySeparatorChar));
		}
		return processed;
	}
}
