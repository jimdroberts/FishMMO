using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FishMMO.Logging;

/// <summary>
/// Scans the configured patches directory at startup and exposes an indexed,
/// immutable view of available patches. Each entry includes the file's
/// fully-resolved absolute path and a SHA-256 content hash used by the client
/// to verify integrity after download.
/// </summary>
public class PatchVersionService : IDisposable
{
	private readonly IHostEnvironment env;
	private readonly IConfiguration config;

	/// <summary>
	/// The latest client version discovered from patch files, or <c>null</c>
	/// if no valid patches were found.
	/// </summary>
	public string? LatestVersion { get; private set; }

	/// <summary>
	/// Absolute, canonical path to the patches directory used as the trust root
	/// for path-traversal checks. Trailing separator is always present.
	/// </summary>
	public string PatchesRoot { get; private set; } = "";

	/// <summary>
	/// Information about a single available patch file.
	/// </summary>
	public sealed record PatchEntry(string FromVersion, string ToVersion, string FullPath, long Size, string Sha256Hex);

	// Indexed by "{from}-{to}" exactly as it appears in the file name (case-sensitive
	// to match the on-disk name and our regex output). Reads use a swap-on-write
	// pattern so live request threads see a consistent snapshot even while a
	// reindex is in progress.
	private volatile Dictionary<string, PatchEntry> _patches = new(StringComparer.Ordinal);

	private FileSystemWatcher? _watcher;
	private readonly object _reindexLock = new();
	private Timer? _debounceTimer;

	private static readonly Regex PatchFileNameRegex =
		new Regex(@"^(\d{1,9}\.\d{1,9}\.\d{1,9}(?:\.[A-Za-z0-9\-]{1,32})?)-(\d{1,9}\.\d{1,9}\.\d{1,9}(?:\.[A-Za-z0-9\-]{1,32})?)\.zip$", RegexOptions.Compiled);

	public PatchVersionService(IHostEnvironment env, IConfiguration config)
	{
		this.env = env;
		this.config = config;
		InitializeLatestVersion();
		StartWatcher();
	}

	/// <summary>
	/// Looks up a patch entry by its parsed client/target versions.
	/// Returns <c>null</c> if no such patch is indexed.
	/// </summary>
	public PatchEntry? TryGetPatch(string fromVersion, string toVersion)
	{
		return _patches.TryGetValue($"{fromVersion}-{toVersion}", out var entry) ? entry : null;
	}

	/// <summary>
	/// Starts a <see cref="FileSystemWatcher"/> over <see cref="PatchesRoot"/> so
	/// patches dropped in after startup are picked up without a server restart.
	/// Triggers are debounced (1s) to coalesce bursts during multi-file copies.
	/// </summary>
	private void StartWatcher()
	{
		if (!Directory.Exists(PatchesRoot)) return;
		try
		{
			_watcher = new FileSystemWatcher(PatchesRoot, "*.zip")
			{
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
				IncludeSubdirectories = false,
				EnableRaisingEvents = true,
			};
			FileSystemEventHandler onChange = (_, _) => ScheduleReindex();
			RenamedEventHandler onRename = (_, _) => ScheduleReindex();
			_watcher.Created += onChange;
			_watcher.Changed += onChange;
			_watcher.Deleted += onChange;
			_watcher.Renamed += onRename;
			Log.Info("PatchVersionService", $"FileSystemWatcher started on '{PatchesRoot}'.");
		}
		catch (Exception ex)
		{
			Log.Warning("PatchVersionService", $"Could not start FileSystemWatcher: {ex.Message}. Hot-reindex disabled.");
		}
	}

	private void ScheduleReindex()
	{
		// Use CompareExchange so two FileSystemWatcher callbacks racing on the very
		// first event can never construct two Timers (and leak the loser). After the timer
		// exists, the cheap Change() call is safe to race on — Timer.Change is thread-safe.
		Timer? existing = _debounceTimer;
		if (existing == null)
		{
			var created = new Timer(_ =>
			{
				lock (_reindexLock)
				{
					try
					{
						InitializeLatestVersion();
					}
					catch (Exception ex)
					{
						Log.Error("PatchVersionService", $"Reindex failed: {ex.Message}");
					}
				}
			}, null, Timeout.Infinite, Timeout.Infinite);

			Timer? prior = Interlocked.CompareExchange(ref _debounceTimer, created, null);
			if (prior != null)
			{
				// Lost the race; drop the duplicate.
				created.Dispose();
			}
		}
		_debounceTimer!.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
	}

	public void Dispose()
	{
		_watcher?.Dispose();
		_debounceTimer?.Dispose();
	}

	private void InitializeLatestVersion()
	{
		var patchesDirectoryConfig = config["Patches:DirectoryName"] ?? "Patches";
		var patchesPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, patchesDirectoryConfig));
		PatchesRoot = patchesPath.EndsWith(Path.DirectorySeparatorChar) ? patchesPath : patchesPath + Path.DirectorySeparatorChar;

		// Defence-in-depth: even though the value comes from local config and is
		// resolved relative to BaseDirectory, an attacker who can write a config
		// file with a value containing ".." segments or an absolute path could
		// otherwise pivot the patcher to serve files from anywhere on disk.
		// Refuse to start if PatchesRoot escapes BaseDirectory.
		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		if (!baseDir.EndsWith(Path.DirectorySeparatorChar)) baseDir += Path.DirectorySeparatorChar;
		if (!PatchesRoot.StartsWith(baseDir, StringComparison.Ordinal))
		{
			throw new InvalidOperationException(
				$"Patches:DirectoryName resolves to '{PatchesRoot}' which is outside the application base directory '{baseDir}'. Refusing to start.");
		}

		if (!Directory.Exists(patchesPath))
		{
			Log.Warning("PatchVersionService", $"Patches directory not found: {patchesPath}. Setting latest version to 0.0.0.");
			LatestVersion = "0.0.0";
			return;
		}

		try
		{
			VersionConfig? highestVersion = null;
			// Build into a fresh map so a partial reindex never exposes a half-emptied
			// view to concurrent readers. Swap onto _patches atomically when complete.
			var fresh = new Dictionary<string, PatchEntry>(StringComparer.Ordinal);

			foreach (var filePath in Directory.EnumerateFiles(patchesPath, "*.zip", SearchOption.TopDirectoryOnly))
			{
				string fileName = Path.GetFileName(filePath);
				Match match = PatchFileNameRegex.Match(fileName);

				if (!match.Success)
				{
					Log.Warning("PatchVersionService", $"File '{fileName}' does not match the expected patch naming pattern. Ignored.");
					continue;
				}

				string fromString = match.Groups[1].Value;
				string toString = match.Groups[2].Value;

				if (VersionConfig.Parse(fromString) == null || VersionConfig.Parse(toString) is not VersionConfig toVersion)
				{
					Log.Warning("PatchVersionService", $"Could not parse versions from patch file name '{fileName}'.");
					continue;
				}

				// Resolve to a canonical absolute path and verify it lives inside PatchesRoot.
				string fullPath = Path.GetFullPath(filePath);
				if (!fullPath.StartsWith(PatchesRoot, StringComparison.Ordinal))
				{
					Log.Warning("PatchVersionService", $"Patch file '{fileName}' resolves outside PatchesRoot. Ignored.");
					continue;
				}

				var info = new FileInfo(fullPath);

				// Reject symlinks / junctions / mount points.  Hashing a symlink
				// would follow it and leak the target file's hash in the version
				// metadata response (content oracle).  The PatchController already
				// checks ReparsePoint at serve time, but we must also block it at
				// index time to prevent hash disclosure.
				if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
				{
					Log.Warning("PatchVersionService", $"Skipping symlink/reparse-point: {fileName}");
					continue;
				}

				string sha256;
				try
				{
					sha256 = ComputeSha256Hex(fullPath);
				}
				catch (Exception ex)
				{
					Log.Error("PatchVersionService", $"Failed to hash '{fileName}': {ex.Message}. Skipping.");
					continue;
				}

				var entry = new PatchEntry(fromString, toString, fullPath, info.Length, sha256);
				fresh[$"{fromString}-{toString}"] = entry;

				if (highestVersion == null || toVersion > highestVersion)
				{
					highestVersion = toVersion;
				}

				Log.Info("PatchVersionService", $"Indexed patch {fromString} -> {toString} ({info.Length} bytes, sha256={sha256.Substring(0, 12)}…)");
			}

			_patches = fresh; // atomic reference swap
			LatestVersion = highestVersion?.FullVersion ?? "0.0.0";
			Log.Info("PatchVersionService", $"Patch indexing complete. Latest version: {LatestVersion}. Total patches: {_patches.Count}.");
		}
		catch (Exception ex)
		{
			Log.Error("PatchVersionService", $"Error during patch indexing in '{patchesPath}': {ex.Message}");
			LatestVersion = "0.0.0";
		}
	}

	private static string ComputeSha256Hex(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: false);
		using var sha = SHA256.Create();
		byte[] hash = sha.ComputeHash(stream);
		return Convert.ToHexString(hash).ToLowerInvariant();
	}
}
