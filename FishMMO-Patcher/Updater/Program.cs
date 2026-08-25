using System.Diagnostics;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FishMMO.Patcher;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

class Program
{
	private static readonly string WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;

	/// <summary>
	/// Where the install itself lives. Always the updater's own directory — the updater ships
	/// beside the client binaries it patches, so this is the install root by construction and
	/// is not configurable. Relocating an install means moving the updater with it.
	/// </summary>
	private static string InstallDirectory => WorkingDirectory;

	/// <summary>
	/// Directory patch archives are read from. Defaults to "Patches" under the install root
	/// and is overridden by <c>-patches=</c>.
	/// </summary>
	/// <remarks>
	/// This must resolve to the same folder the launcher downloaded into
	/// (<c>FishMMO.Shared.Constants.GetPatchesDirectory()</c>, which honours the same setting).
	/// If the two ever disagree the archive is not found, the update silently no-ops, and the
	/// client relaunches at the same version forever — so the launcher passes its resolved path
	/// explicitly rather than both sides deriving it and hoping they agree.
	/// <para>
	/// Overriding this does not weaken the integrity guarantee: the launcher hashes the archive
	/// against the server-supplied SHA-256 before invoking the updater at all, and anyone able
	/// to write the launcher's configuration file could equally write a file into the default
	/// location. The override changes where a verified archive is read from, not whether it is
	/// verified.
	/// </para>
	/// </remarks>
	private static string PatchesDirectory = Path.Combine(WorkingDirectory, "Patches");

	private static string Version;
	private static string LatestVersion;
	private static int PID;
	private static string Executable;

	// Dedicated lock object for thread-safe console output.
	private static readonly object consoleLock = new object();

	// Configuration for robust file operations.
	private const int MaxFileOperationRetries = 5;
	private const int FileOperationRetryDelayMs = 200;

	// Process shutdown tuning.
	private const int GracefulExitTimeoutMs = 10000;
	private const int ForceKillTimeoutMs = 5000;
	private const int PostKillSettleMs = 500;

	/// <summary>POSIX SIGTERM. Requests a graceful shutdown.</summary>
	private const int SIGTERM = 15;

	/* Process exit codes.
	 *
	 * The updater used to Environment.Exit(0) on every path, including a patch that failed and
	 * rolled back. SystemUpdaterLauncher reads this value and reports "the updater decided
	 * there was nothing to do" for zero — so a failed apply was indistinguishable from an
	 * up-to-date install, and the launcher relaunched, found the same version mismatch,
	 * downloaded the same archive and tried again. Forever, on a multi-gigabyte download.
	 *
	 * These are only observable when the updater exits BEFORE it kills the launcher, or when
	 * the kill fails; that is exactly the window in which the launcher can still act on them. */

	/// <summary>The patch applied, or there was nothing to apply.</summary>
	private const int ExitSuccess = 0;
	/// <summary>The patch failed and the install was rolled back. Retrying will not help.</summary>
	private const int ExitPatchFailed = 2;
	/// <summary>The expected patch archive was not present.</summary>
	private const int ExitPatchMissing = 3;
	/// <summary>A path or argument was refused for safety reasons.</summary>
	private const int ExitRefused = 4;
	/// <summary>Another updater already owns this install. Nothing was done.</summary>
	private const int ExitAlreadyRunning = 5;

	/// <summary>
	/// Name of the lock file that makes one updater the only one working on an install.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two updaters on one install is reachable — a player double-clicking, an updater started
	/// by hand alongside the launcher's, a retry that overlaps a run whose kill of the launcher
	/// failed — and the consequences are not confined to one of them losing. The second
	/// instance sees the first one's staging directory, reads its LIVE journal as though it
	/// were an interrupted transaction, restores the files the first is actively replacing, and
	/// deletes the staging directory out from under it.
	/// </para>
	/// <para>
	/// Testing found this benign in practice, but only by accident: the loser happens to fail
	/// its <c>old_hash</c> check because the winner has already changed the bytes. That is a
	/// real second line of defence and it stays, but it is not a concurrency control — it does
	/// nothing for a manifest that omits <c>old_hash</c>, and nothing about the journal
	/// interference. An exclusive lock is the primitive that actually says "one at a time".
	/// </para>
	/// <para>
	/// <see cref="FileShare.None"/> is enforced cross-process on both platforms .NET targets
	/// here (a native share mode on Windows, <c>flock</c> on Unix), and the lock is released by
	/// the kernel when the holder dies — so a crashed updater leaves a stale FILE but never a
	/// stale LOCK, and the next run acquires it normally.
	/// </para>
	/// </remarks>
	private const string LockFileName = ".fishmmo-update.lock";

	/// <summary>Held for the process lifetime once acquired; never read or written.</summary>
	private static FileStream updateLock;

	/// <summary>
	/// Takes exclusive ownership of the install, or reports that someone else has it.
	/// </summary>
	private static bool TryAcquireInstallLock()
	{
		string lockPath = Path.Combine(InstallDirectory, LockFileName);
		try
		{
			updateLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			return true;
		}
		catch (IOException)
		{
			Console.WriteLine($"Another update is already running against '{InstallDirectory}'. This instance will exit without touching anything.");
			return false;
		}
		catch (UnauthorizedAccessException ex)
		{
			/* The install is not writable by this user, which the patch itself would fail on a
			 * moment later anyway. Refusing here is the same answer delivered before anything
			 * has been moved. */
			Console.WriteLine($"Cannot take the update lock at '{lockPath}' ({ex.Message}). Refusing to patch an install this user cannot write to.");
			return false;
		}
	}

	/// <summary>
	/// POSIX <c>kill(2)</c>. Used to request a graceful shutdown on Linux/macOS, where
	/// .NET exposes no managed equivalent — <see cref="Process.Kill()"/> sends SIGKILL,
	/// which denies the client any chance to run its shutdown handlers, and
	/// <see cref="Process.CloseMainWindow"/> is Windows-only.
	/// </summary>
	[DllImport("libc", SetLastError = true, EntryPoint = "kill")]
	private static extern int SysKill(int pid, int sig);

	/// <summary>
	/// Points <see cref="PatchesDirectory"/> at <paramref name="path"/> when it is usable,
	/// otherwise leaves the default in place.
	/// </summary>
	/// <remarks>
	/// Falling back rather than failing is deliberate. The override is a convenience — it lets
	/// a player keep patch archives off a small system drive — and an unusable one should cost
	/// them the convenience, not the update. The default location is where the launcher writes
	/// when its own setting is unusable, so the two still agree after the fallback.
	/// </remarks>
	private static void ApplyPatchesDirectoryOverride(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		try
		{
			// Rooted only. A relative path resolves against the updater's current directory,
			// which is not guaranteed to be the install root when it is started by the OS
			// rather than by the launcher — so the same string could mean two places.
			if (!Path.IsPathRooted(path))
			{
				Console.WriteLine($"WARNING: Ignoring -patches='{path}' because it is not an absolute path. Using '{PatchesDirectory}'.");
				return;
			}

			string full = Path.GetFullPath(path);
			if (!Directory.Exists(full))
			{
				Console.WriteLine($"WARNING: Ignoring -patches='{full}' because the directory does not exist. Using '{PatchesDirectory}'.");
				return;
			}

			PatchesDirectory = full;
			Console.WriteLine($"Patch archives will be read from '{PatchesDirectory}'.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"WARNING: Ignoring -patches='{path}' ({ex.Message}). Using '{PatchesDirectory}'.");
		}
	}

	/// <summary>
	/// Helper method for robust file deletion with retries.
	/// </summary>
	/// <param name="path">The path of the file to delete.</param>
	/// <param name="retries">The number of retry attempts.</param>
	/// <returns>True if the file was successfully deleted or didn't exist, false otherwise.</returns>
	private static bool TryDeleteFile(string path, int retries)
	{
		for (int i = 0; i < retries; i++)
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
					lock (consoleLock)
					{
						Console.WriteLine($"DEBUG: Successfully deleted: '{path}' (Attempt {i + 1})");
					}
				}
				return true;
			}
			catch (IOException ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"WARNING: Failed to delete '{path}' (Attempt {i + 1}/{retries}). Reason: {ex.Message}");
				}
				Thread.Sleep(FileOperationRetryDelayMs);
			}
			catch (UnauthorizedAccessException ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"ERROR: Unauthorized access when deleting '{path}'. This usually indicates permission issues. Reason: {ex.Message}");
				}
				return false;
			}
			catch (Exception ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"ERROR: An unexpected error occurred while deleting '{path}'. Reason: {ex.Message}");
				}
				return false;
			}
		}
		lock (consoleLock)
		{
			Console.WriteLine($"ERROR: Failed to delete '{path}' after {retries} attempts. Giving up.");
		}
		return false;
	}

	/// <summary>
	/// Helper method for robust file move with retries.
	/// </summary>
	/// <param name="sourcePath">The source file path.</param>
	/// <param name="destinationPath">The destination file path.</param>
	/// <param name="retries">The number of retry attempts.</param>
	/// <returns>True if the file was successfully moved, false otherwise.</returns>
	private static bool TryMoveFile(string sourcePath, string destinationPath, int retries)
	{
		for (int i = 0; i < retries; i++)
		{
			try
			{
				File.Move(sourcePath, destinationPath);
				lock (consoleLock)
				{
					Console.WriteLine($"DEBUG: Successfully moved '{sourcePath}' to '{destinationPath}' (Attempt {i + 1})");
				}
				return true;
			}
			catch (IOException ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"WARNING: Failed to move '{sourcePath}' to '{destinationPath}' (Attempt {i + 1}/{retries}). Reason: {ex.Message}");
				}
				Thread.Sleep(FileOperationRetryDelayMs);
			}
			catch (UnauthorizedAccessException ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"ERROR: Unauthorized access when moving '{sourcePath}' to '{destinationPath}'. Reason: {ex.Message}");
				}
				return false;
			}
			catch (Exception ex)
			{
				lock (consoleLock)
				{
					Console.WriteLine($"ERROR: An unexpected error occurred while moving '{sourcePath}' to '{destinationPath}'. Reason: {ex.Message}");
				}
				return false;
			}
		}
		lock (consoleLock)
		{
			Console.WriteLine($"ERROR: Failed to move '{sourcePath}' to '{destinationPath}' after {retries} attempts. Giving up.");
		}
		return false;
	}

	static void Main(string[] args)
	{
		// Parse command line arguments.
		foreach (var arg in args)
		{
			if (arg.StartsWith("-version"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2) Version = splitArg[1];
			}
			if (arg.StartsWith("-latestversion"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2) LatestVersion = splitArg[1];
			}
			if (arg.StartsWith("-pid"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2 && int.TryParse(splitArg[1], out int pid)) PID = pid;
			}
			if (arg.StartsWith("-exe"))
			{
				var splitArg = arg.Split('=');
				if (splitArg.Length == 2) Executable = splitArg[1];
			}
			if (arg.StartsWith("-patches"))
			{
				// Split on the first '=' only. A Windows path can contain '=', and
				// Split('=') would truncate it at the first one.
				int separator = arg.IndexOf('=');
				if (separator > 0 && separator < arg.Length - 1)
				{
					ApplyPatchesDirectoryOverride(arg.Substring(separator + 1));
				}
			}
		}

		Console.WriteLine($"Client Patcher started. Current Client Version: {Version}, LatestVersion: {LatestVersion}, Launcher PID: {PID}, Executable: {Executable}");

		/* Claimed before anything is killed, patched or started.
		 *
		 * A second instance that got as far as terminating the launcher, or as far as starting
		 * a second copy of the client, would already have done damage the lock exists to
		 * prevent — so this is the first thing that happens after the arguments are read. The
		 * loser exits silently as far as the install is concerned: no patch, and no client
		 * launch either, because the holder is going to do that. */
		if (!TryAcquireInstallLock())
		{
			Environment.Exit(ExitAlreadyRunning);
			return;
		}

		// Terminate the launcher process before patching begins.
		KillLauncherProcess(PID);

		if (Version == LatestVersion)
		{
			Console.WriteLine("Client is already up-to-date. Exiting patcher.");
			TryStartExecutableAndExit(Executable, PID);
			return;
		}

		// Naming scheme shared with the launcher's download path and the patcher server's
		// index. See PatchesDirectory.
		string expectedPatchFileName = $"{Version}-{LatestVersion}.zip";

		/* Both halves of that name come from the command line, and the launcher's copy of
		 * LatestVersion came from the update server — so this is a server-influenced string
		 * being interpolated into a path. It only ever names a file to READ, but a read of an
		 * arbitrary path is still a read of an arbitrary path, and the launcher-side fix
		 * (Constants.GetPatchFileName) validates the same name for the same reason. */
		if (!PathContainment.TryResolve(PatchesDirectory, expectedPatchFileName, out string patchFilePath, out string patchNameReason))
		{
			Console.WriteLine($"SECURITY: Refusing patch file name '{expectedPatchFileName}': {patchNameReason}");
			TryStartExecutableAndExit(Executable, PID, ExitRefused);
			return;
		}

		if (!File.Exists(patchFilePath))
		{
			Console.WriteLine($"Error: Expected patch file '{expectedPatchFileName}' not found in '{PatchesDirectory}'. Cannot update client.");
			TryStartExecutableAndExit(Executable, PID, ExitPatchMissing);
			return;
		}

		Console.WriteLine($"\nApplying patch: {Path.GetFileName(patchFilePath)}");
		bool applied = ApplyPatchFile(patchFilePath);

		if (applied)
		{
			Console.WriteLine("\nPatch applied. Client is up-to-date.");
			// Remove the consumed archive so Patches/ does not accumulate every update the
			// player has ever installed. Only on success — a failed apply rolls back, and
			// keeping the archive allows a retry without re-downloading.
			if (!TryDeleteFile(patchFilePath, MaxFileOperationRetries))
			{
				Console.WriteLine($"WARNING: Could not remove the applied patch archive '{patchFilePath}'. It can be deleted manually.");
			}
		}
		else
		{
			Console.WriteLine("\nPatch application FAILED. The client has been rolled back to its previous state and remains on the old version.");
			Console.WriteLine($"The patch archive has been left at '{patchFilePath}' for a retry.");
		}

		Console.WriteLine("Exiting patcher.");
		TryStartExecutableAndExit(Executable, PID, applied ? ExitSuccess : ExitPatchFailed);
	}

	/// <summary>
	/// Directory the updater stages displaced files in while a patch is being applied.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Everything a patch displaces — a modified file about to be replaced, a file a
	/// <c>new_files</c> entry overwrites, a file the manifest deletes — is MOVED here rather
	/// than unlinked, and is only discarded once the whole patch has committed. That is what
	/// makes the rollback claim true. The previous implementation deleted outright and backed
	/// up only the modified files, so a patch that failed after its deletion pass left the
	/// install permanently missing files while still printing "the client has been rolled back
	/// to its previous state" — the one message a player would act on by doing nothing.
	/// </para>
	/// <para>
	/// It lives inside the install root so every move is a same-volume rename rather than a
	/// copy, which also keeps the intermediate copies of a multi-gigabyte patch off
	/// <c>/tmp</c> — on Linux frequently a size-capped tmpfs, where a large patch fails with
	/// ENOSPC halfway through. The leading dot keeps it out of the way, it is removed on both
	/// the success and the failure path, and any leftover from a killed process is cleared
	/// before the next patch starts.
	/// </para>
	/// </remarks>
	private const string StagingDirectoryName = ".fishmmo-update-staging";

	/// <summary>Serial number for staged file names. Flat names, so no path arithmetic.</summary>
	private static int stagingCounter;

	/// <summary>
	/// Path comparison for install-relative bookkeeping, matching the filesystem's own rules.
	/// </summary>
	private static StringComparer PathComparer =>
		OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal;

	/// <summary>
	/// A file the patch moved out of the way, and everything needed to put it back.
	/// </summary>
	private sealed class DisplacedFile
	{
		/// <summary>Where the file must be restored to.</summary>
		public string OriginalPath;
		/// <summary>Where it currently sits inside the staging directory.</summary>
		public string StagedPath;
		/// <summary>Permission bits it carried, on platforms that have them.</summary>
		public UnixFileMode? Mode;
	}

	/// <summary>
	/// Applies a single patch file (ZIP archive) to the client.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The apply is a transaction. Nothing the patch displaces is destroyed until every
	/// operation has succeeded: originals are moved into <see cref="StagingDirectoryName"/>
	/// and restored from there if any step fails, and files the patch created where nothing
	/// existed are removed. Only after the last move commits is the staging directory
	/// discarded.
	/// </para>
	/// <para>
	/// <b>Each parallel worker opens its own <see cref="ZipArchive"/>.</b> They used to share
	/// one. <see cref="ZipArchive"/> is explicitly not thread-safe — in read mode every entry
	/// stream reads through the archive's single underlying <see cref="FileStream"/>, so two
	/// workers seeking and reading at once interleave into each other's data. It did not
	/// corrupt silently, which is the one mercy: it surfaced as
	/// "A local file header is corrupt" and rolled the whole patch back, reproducibly, for
	/// <i>any</i> patch touching two or more modified files. A per-worker archive costs one
	/// file handle each and makes the parallelism sound.
	/// </para>
	/// </remarks>
	/// <param name="patchFilePath">The full path to the patch ZIP file.</param>
	/// <returns>
	/// True when every operation committed successfully; false when the patch was rolled
	/// back or otherwise did not complete. Callers must not treat a failed apply as an
	/// upgrade — the install is still on the old version.
	/// </returns>
	static bool ApplyPatchFile(string patchFilePath)
	{
		string stagingRoot = Path.Combine(InstallDirectory, StagingDirectoryName);
		string stagedOriginals = Path.Combine(stagingRoot, "orig");
		string tempRoot = Path.Combine(stagingRoot, "tmp");

		// Originals moved aside, in the order they were displaced.
		List<DisplacedFile> displaced = new List<DisplacedFile>();
		// Paths this patch created where nothing existed; a rollback removes them.
		List<string> createdFiles = new List<string>();
		// Patched temp file -> target, plus the permission bits the target must end up with.
		List<Tuple<string, string, UnixFileMode?>> filesToMove = new List<Tuple<string, string, UnixFileMode?>>();
		List<string> tempFilesCreated = new List<string>();

		ConcurrentBag<Exception> parallelExceptions = new ConcurrentBag<Exception>();
		TransactionJournal journal = null;
		/* Whether THIS run owns the staging directory and may therefore clean it up.
		 *
		 * False until this run has recovered any previous transaction and started its own. The
		 * cleanup in `finally` is unconditional otherwise, and the one path that must never
		 * reach it is the one where recovery FAILED: that path deliberately keeps the staging
		 * directory because it holds the only copy of files it could not put back, and a
		 * cleanup that ran anyway would delete precisely what the message above it just told
		 * the player was still recoverable. */
		bool ownsStaging = false;
		bool committed = false;
		// Originals the rollback could not put back. While this is non-zero the staging
		// directory holds the only copy of them and must survive.
		int unrestoredOriginals = 0;

		try
		{
			/* A staging directory left over from a previous run is NOT simply stale.
			 *
			 * Displacing an original is a move, so a run that died between staging a file and
			 * committing its replacement left the install's only copy of that file in there.
			 * Deleting the directory — which is what this used to do — destroyed it. The journal
			 * inside says what belongs where; replaying it puts the install back at its previous
			 * version, which is the state a failed update is supposed to leave behind. */
			if (Directory.Exists(stagingRoot))
			{
				if (!RecoverInterruptedTransaction(stagingRoot))
				{
					Console.WriteLine("Refusing to apply a patch over an install whose previous update could not be unwound.");
					return false;
				}
				// Only now is it safe to clear: recovery has taken everything worth keeping
				// back out of it. Clearing also keeps it out of the next patch's diff, since
				// the generator scans the whole install tree.
				TryDeleteDirectory(stagingRoot);
			}
			Directory.CreateDirectory(stagedOriginals);
			Directory.CreateDirectory(tempRoot);
			journal = new TransactionJournal(Path.Combine(stagingRoot, JournalFileName));
			ownsStaging = true;

			PatchManifest manifest;
			using (ZipArchive archive = ZipFile.OpenRead(patchFilePath))
			{
				ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json");
				if (manifestEntry == null)
				{
					Console.WriteLine($"Error: manifest.json not found in patch file '{Path.GetFileName(patchFilePath)}'. Skipping.");
					return false;
				}

				using (Stream manifestStream = manifestEntry.Open())
				{
					manifest = JsonSerializer.Deserialize<PatchManifest>(manifestStream);
				}
			}

			if (manifest == null)
			{
				Console.WriteLine($"Error: manifest.json in '{Path.GetFileName(patchFilePath)}' could not be read. Skipping.");
				return false;
			}

			Console.WriteLine($"Loaded manifest from {Path.GetFileName(patchFilePath)}. Old Version: {manifest.OldVersion}, New Version: {manifest.NewVersion}");

			/* The archive's own idea of what it upgrades has to agree with what we were asked
			 * to do. The file NAME already encodes both versions, but a name is not content:
			 * an archive dropped into Patches/ under the right name applies whatever is inside
			 * it, and an operator who mislabels a build gets a client that reports a version it
			 * is not running. Cheap to check, and it is the only place the two are compared. */
			if (!ManifestMatchesRequestedUpgrade(manifest))
			{
				return false;
			}

			/* Refuse a manifest that writes the same path twice.
			 *
			 * Two entries for one target is not something the generator can emit (it keys both
			 * sides by relative path), so a manifest carrying one is malformed or hostile. Left
			 * alone it becomes a race: the write phases run in parallel, two workers call
			 * File.Create on the same path, and which of them staged the original first decides
			 * what the rollback has left to work with. Refusing up front replaces a
			 * nondeterministic failure with a named one, before anything has been touched. */
			if (!ValidateNoDuplicateTargets(manifest, stagingRoot))
			{
				return false;
			}

			// Phase 1: Pre-create all necessary directories.
			PreCreateDirectories(manifest, stagingRoot, parallelExceptions);
			if (!parallelExceptions.IsEmpty)
			{
				throw new AggregateException("Directory pre-creation failed.", parallelExceptions);
			}

			// Phase 2: Process new files in parallel.
			Console.WriteLine($"Processing {manifest.NewFiles?.Count ?? 0} new files in parallel...");
			object newFileLock = new object();
			Parallel.ForEach(
				manifest.NewFiles ?? new List<NewFileEntry>(),
				ParallelOptions,
				() => ZipFile.OpenRead(patchFilePath),
				(newFile, loopState, archive) =>
				{
					string fullPath;
					try
					{
						// CONTAINMENT. Untrusted manifest path -> filesystem write. See
						// PathContainment; a rejection fails the whole patch rather than
						// skipping the entry.
						fullPath = ResolveManifestPath(newFile.RelativePath, stagingRoot);
					}
					catch (PathContainmentException ex)
					{
						lock (consoleLock)
						{
							Console.WriteLine($"\tSECURITY: {ex.Message}");
						}
						parallelExceptions.Add(ex);
						loopState.Stop();
						return archive;
					}

					ZipArchiveEntry newFileZipEntry = archive.GetEntry(newFile.FileDataEntryName);
					if (newFileZipEntry == null)
					{
						lock (consoleLock)
						{
							Console.WriteLine($"\tWarning: New file entry not found in ZIP: {newFile.FileDataEntryName}");
						}
						parallelExceptions.Add(new FileNotFoundException($"New file entry not found in ZIP: {newFile.FileDataEntryName}"));
						loopState.Stop();
						return archive;
					}

					try
					{
						/* A "new" file whose path already exists is a REPLACEMENT, and the
						 * File.Create below truncates it. Staged first, or the original is
						 * gone the moment the write starts and no rollback can bring it back
						 * — which is exactly what happened to any file the generator classed
						 * as an addition because it had been renamed, or that the player had
						 * created themselves at that path. */
						if (File.Exists(fullPath))
						{
							if (!TryStageAside(fullPath, stagedOriginals, out DisplacedFile record, out string stageError))
							{
								throw new IOException($"Could not stage the existing file at '{fullPath}' before replacing it: {stageError}");
							}
							// Journalled before the write that depends on it, so a process death
							// between the two still leaves a record of where this belongs.
							journal.RecordDisplaced(record);
							lock (newFileLock)
							{
								displaced.Add(record);
							}
						}
						else
						{
							journal.RecordCreated(fullPath);
							lock (newFileLock)
							{
								createdFiles.Add(fullPath);
							}
						}

						using (Stream sourceStream = newFileZipEntry.Open())
						using (FileStream destinationStream = File.Create(fullPath))
						{
							sourceStream.CopyTo(destinationStream);
						}

						string actualHash = ComputeFileHash(fullPath);
						if (actualHash != newFile.NewHash)
						{
							throw new InvalidOperationException(
								$"Hash mismatch for new file '{newFile.RelativePath}'. Expected {newFile.NewHash}, got {actualHash}.");
						}

						/* Permissions do not survive a fresh File.Create: it produces 0666
						 * masked by the umask, i.e. 0644, and a newly shipped native binary
						 * that lands non-executable is a file the player cannot run. */
						ApplyModeForNewFile(fullPath, newFile.UnixMode);

						lock (consoleLock)
						{
							Console.WriteLine($"\tAdded new file (hash verified): {newFile.RelativePath}");
						}
					}
					catch (Exception ex)
					{
						lock (consoleLock)
						{
							Console.WriteLine($"\tError adding new file {newFile.RelativePath}: {ex.Message}");
						}
						parallelExceptions.Add(ex);
						loopState.Stop();
					}

					return archive;
				},
				archive => archive.Dispose());

			if (!parallelExceptions.IsEmpty)
			{
				throw new AggregateException("Processing new files failed.", parallelExceptions);
			}

			// Push the journal to disk at each boundary that displaced something. Per-record
			// syncing would cost one fsync per file; this bounds a power-loss window to a
			// single phase for two.
			journal.Sync();

			// Phase 3: Process modified files in parallel (create temp files).
			Console.WriteLine($"Processing {manifest.ModifiedFiles?.Count ?? 0} modified files in parallel (creating temp files)...");
			Parallel.ForEach(
				manifest.ModifiedFiles ?? new List<ModifiedFileEntry>(),
				ParallelOptions,
				() => ZipFile.OpenRead(patchFilePath),
				(modifiedFile, loopState, archive) =>
				{
					string oldFilePath;
					try
					{
						// CONTAINMENT. This path is both read from and (in Phase 7) moved over,
						// so an unchecked entry is an arbitrary-file overwrite.
						oldFilePath = ResolveManifestPath(modifiedFile.RelativePath, stagingRoot);
					}
					catch (PathContainmentException ex)
					{
						lock (consoleLock)
						{
							Console.WriteLine($"\tSECURITY: {ex.Message}");
						}
						parallelExceptions.Add(ex);
						loopState.Stop();
						return archive;
					}

					ZipArchiveEntry patchDataEntry = archive.GetEntry(modifiedFile.PatchDataEntryName);
					if (patchDataEntry == null)
					{
						lock (consoleLock)
						{
							Console.WriteLine($"\tWarning: Patch data entry not found in ZIP for {modifiedFile.RelativePath}: {modifiedFile.PatchDataEntryName}");
						}
						parallelExceptions.Add(new FileNotFoundException($"Patch data entry not found in ZIP: {modifiedFile.PatchDataEntryName}"));
						loopState.Stop();
						return archive;
					}

					string tempPatchedFilePath = null;
					try
					{
						/* The delta is meaningless against anything other than the exact bytes
						 * it was generated from. Checking OLD_HASH first turns "the install has
						 * drifted" into a named failure before a single byte is written, rather
						 * than a patched file that is quietly wrong — which, before NEW_HASH was
						 * checked below, is precisely what got committed. */
						if (!string.IsNullOrEmpty(modifiedFile.OldHash))
						{
							if (!File.Exists(oldFilePath))
							{
								throw new FileNotFoundException($"Cannot patch '{modifiedFile.RelativePath}': the file is not present in this install.");
							}

							string currentHash = ComputeFileHash(oldFilePath);
							if (currentHash != modifiedFile.OldHash)
							{
								throw new InvalidOperationException(
									$"Refusing to patch '{modifiedFile.RelativePath}': it does not match the version this patch was built against " +
									$"(expected {modifiedFile.OldHash}, found {currentHash}). The installation has been modified or is not at version {manifest.OldVersion}.");
							}
						}

						// Temp output goes inside the install's own staging directory: same
						// volume as the target (so the commit is a rename, not a copy) and not
						// subject to a size-capped /tmp.
						tempPatchedFilePath = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ".tmp");

						using (Stream patchDataStream = patchDataEntry.Open())
						using (BinaryReader reader = new BinaryReader(patchDataStream))
						{
							tempPatchedFilePath = new Patcher().Apply(reader, oldFilePath, modifiedFile.FinalFileSize, tempPatchedFilePath);
						}

						if (tempPatchedFilePath == null)
						{
							throw new InvalidOperationException($"Patcher.Apply returned null for {modifiedFile.RelativePath}");
						}

						lock (newFileLock)
						{
							tempFilesCreated.Add(tempPatchedFilePath);
						}

						/* NEW_HASH, checked before this result is allowed anywhere near the
						 * install. It was declared in the manifest and never looked at, which
						 * made every failure mode of the delta format silent: a truncated
						 * patch stream, a wrong final_file_size (the applier zero-pads to it
						 * and says so, to a console nobody reads), a delta applied to drifted
						 * bytes. The file was committed, the archive deleted, and the client
						 * relaunched reporting the new version while running corrupt code. */
						if (!string.IsNullOrEmpty(modifiedFile.NewHash))
						{
							string producedHash = ComputeFileHash(tempPatchedFilePath);
							if (producedHash != modifiedFile.NewHash)
							{
								throw new InvalidOperationException(
									$"Patched result for '{modifiedFile.RelativePath}' does not match the manifest hash " +
									$"(expected {modifiedFile.NewHash}, produced {producedHash}). The patch data is corrupt or incomplete.");
							}
						}

						/* Carry the target's current permissions across the replacement. On
						 * Linux the client executable is a MODIFIED file in essentially every
						 * patch, and Phase 7 replaces it with a temp file created at 0644 — so
						 * a successful update left the game unable to start, with the updater
						 * reporting success and the launcher already shut down. */
						UnixFileMode? mode = modifiedFile.UnixMode.HasValue
							? ToUnixFileMode(modifiedFile.UnixMode.Value)
							: TryGetUnixFileMode(oldFilePath);

						lock (newFileLock)
						{
							filesToMove.Add(Tuple.Create(tempPatchedFilePath, oldFilePath, mode));
						}

						lock (consoleLock)
						{
							Console.WriteLine($"\tPatched (hash verified): {modifiedFile.RelativePath}");
						}
					}
					catch (Exception ex)
					{
						lock (consoleLock)
						{
							Console.WriteLine($"\tError patching {modifiedFile.RelativePath}: {ex.Message}");
						}
						parallelExceptions.Add(ex);
						loopState.Stop();
					}

					return archive;
				},
				archive => archive.Dispose());

			if (!parallelExceptions.IsEmpty)
			{
				throw new AggregateException("Processing modified files failed.", parallelExceptions);
			}

			// Phase 4: Identify files to delete.
			Console.WriteLine($"Identifying {manifest.DeletedFiles?.Count ?? 0} files for deletion...");
			List<string> filesToDelete = new List<string>();
			if (manifest.DeletedFiles != null)
			{
				/* A path that the same manifest also ADDS or MODIFIES must not be deleted.
				 * Deletions run after the write phases, so the delete would land on the file
				 * the patch just produced — which is what a case-only rename looks like to the
				 * generator on a case-insensitive filesystem: one entry in deleted_files and
				 * one in new_files naming the same file. */
				HashSet<string> written = new HashSet<string>(PathComparer);
				foreach (var t in filesToMove)
				{
					written.Add(t.Item2);
				}
				foreach (var newFile in manifest.NewFiles ?? new List<NewFileEntry>())
				{
					if (PathContainment.TryResolve(WorkingDirectory, newFile.RelativePath, out string addedPath, out _))
					{
						written.Add(addedPath);
					}
				}

				// A path listed for deletion twice would be staged twice; the second attempt
				// finds nothing there and fails the whole patch over a harmless redundancy.
				HashSet<string> alreadyQueued = new HashSet<string>(PathComparer);

				foreach (var deletedFile in manifest.DeletedFiles)
				{
					/* CONTAINMENT — and this one is the reason the helper throws.
					 *
					 * An unchecked delete is arbitrary file deletion: it needs no code
					 * execution to be destructive, and unlike the write paths there is no
					 * hash to fail afterwards. Rejecting here throws out to the outer catch,
					 * which rolls the patch back; the alternative — logging and continuing —
					 * would let a hostile manifest mix real deletions with escaping ones and
					 * still have the patch report success. */
					string fullPath = ResolveManifestPath(deletedFile.RelativePath, stagingRoot);

					if (written.Contains(fullPath))
					{
						Console.WriteLine($"\tSkipping deletion of '{deletedFile.RelativePath}': the same patch writes it.");
						continue;
					}

					if (File.Exists(fullPath) && alreadyQueued.Add(fullPath))
					{
						filesToDelete.Add(fullPath);
						Console.WriteLine($"\tQueued for deletion: {deletedFile.RelativePath}");
					}
					else
					{
						Console.WriteLine($"\tWarning: File to delete not found: {deletedFile.RelativePath}");
					}
				}
			}

			// --- Critical Sequential Finalization Phase ---

			// Phase 5: Stage the modified targets aside. Moving rather than copying halves the
			// I/O of a large patch, and the target has to be out of the way for Phase 7 anyway.
			Console.WriteLine($"Staging {filesToMove.Count} modified files...");
			foreach (var fileEntry in filesToMove)
			{
				string targetPath = fileEntry.Item2;
				if (!File.Exists(targetPath))
				{
					Console.WriteLine($"WARNING: Original file '{targetPath}' not found to stage. It will simply be created.");
					journal.RecordCreated(targetPath);
					createdFiles.Add(targetPath);
					continue;
				}

				if (!TryStageAside(targetPath, stagedOriginals, out DisplacedFile record, out string stageError))
				{
					throw new IOException($"Could not stage '{targetPath}' before replacing it: {stageError}");
				}
				journal.RecordDisplaced(record);
				displaced.Add(record);
			}

			// Phase 6: Deletions. Staged, not unlinked, so a later failure can put them back.
			Console.WriteLine($"Staging {filesToDelete.Count} deletions...");
			foreach (string pathToDelete in filesToDelete)
			{
				if (!TryStageAside(pathToDelete, stagedOriginals, out DisplacedFile record, out string stageError))
				{
					throw new IOException($"Failed to remove '{pathToDelete}': {stageError}");
				}
				journal.RecordDisplaced(record);
				displaced.Add(record);
			}

			journal.Sync();

			// Phase 7: Commit the patched files.
			Console.WriteLine($"Performing {filesToMove.Count} sequential file moves...");
			foreach (var fileEntry in filesToMove)
			{
				string tempPath = fileEntry.Item1;
				string targetPath = fileEntry.Item2;

				if (!File.Exists(tempPath))
				{
					throw new FileNotFoundException($"Temporary patched file not found: {tempPath}");
				}

				// Phase 5 moved the original out of the way, so the target should not exist.
				// If something re-created it, it is not ours to keep.
				if (File.Exists(targetPath) && !TryDeleteFile(targetPath, MaxFileOperationRetries))
				{
					throw new IOException($"Failed to delete target file before move: {targetPath}");
				}

				if (!TryMoveFile(tempPath, targetPath, MaxFileOperationRetries))
				{
					throw new IOException($"Failed to move patched file to target: {targetPath}");
				}

				TryApplyUnixFileMode(targetPath, fileEntry.Item3);
				Console.WriteLine($"\tFinalized: {targetPath}");
			}

			committed = true;
			Console.WriteLine("All patch operations completed successfully.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"CRITICAL ERROR during patch application of '{Path.GetFileName(patchFilePath)}': {ex.Message}");
			Console.WriteLine(ex.StackTrace);

			/* Overall rollback. Every category the patch touched is undone here — files it
			 * replaced, files it deleted, and files it created where nothing existed — because
			 * "rolled back" is a claim the player relies on and a partial one is worse than
			 * none: it leaves an install that looks recovered and is not. */
			Console.WriteLine("Attempting overall rollback due to critical error...");

			/* Removals FIRST, restores second — the order matters, and getting it wrong is
			 * silent data loss.
			 *
			 * A single path can end up in both lists: two manifest entries naming the same file
			 * put the original in `displaced` (the first entry staged it) and the same path in
			 * `createdFiles` (the second entry found nothing left there to stage). Restoring
			 * before removing then put the original back and deleted it a moment later, and the
			 * rollback reported success. Doing removals first means a restore always has the
			 * last word, which is the invariant that matters: a path this patch CREATED is one
			 * nothing existed at, so deleting it can never destroy anything a later restore
			 * would not immediately re-supply. */
			foreach (string createdPath in createdFiles)
			{
				if (File.Exists(createdPath))
				{
					Console.WriteLine($"\tRemoving file created by the failed patch: '{createdPath}'");
					TryDeleteFile(createdPath, MaxFileOperationRetries);
				}
			}

			// Newest first: if a path was displaced more than once, the earliest record holds
			// the true original and must be the one that wins.
			for (int i = displaced.Count - 1; i >= 0; --i)
			{
				if (!RestoreDisplaced(displaced[i]))
				{
					unrestoredOriginals += 1;
				}
			}

			committed = false;
		}
		finally
		{
			// Temp files first, so a staging directory that cannot be removed at least does
			// not keep a full second copy of every patched file.
			foreach (string tempPath in tempFilesCreated)
			{
				if (File.Exists(tempPath))
				{
					TryDeleteFile(tempPath, MaxFileOperationRetries);
				}
			}

			/* The journal is retired FIRST, and only when there is nothing left in staging worth
			 * restoring. A staging directory that outlives its journal reads as "cleared but not
			 * fully removed" on the next run rather than as an interrupted transaction — which is
			 * exactly the normal outcome on Windows, where the updater cannot unlink its own
			 * previous image after replacing itself. Retiring in the other order would have the
			 * next run restore the OLD updater over the new one. */
			journal?.Dispose();

			if (!ownsStaging)
			{
				// Recovery of a PREVIOUS transaction failed, so the staging directory and its
				// journal belong to that one and are the only copy of what it displaced.
				Console.WriteLine($"Leaving '{stagingRoot}' untouched: it belongs to an earlier update that could not be unwound.");
			}
			else if (unrestoredOriginals == 0)
			{
				try
				{
					File.Delete(Path.Combine(stagingRoot, JournalFileName));
				}
				catch (Exception)
				{
				}
			}

			if (!ownsStaging)
			{
				// Nothing to clean up here; see above.
			}
			else if (unrestoredOriginals > 0)
			{
				/* The staging directory is the only surviving copy of these files, so it is
				 * kept. Clearing it here would destroy exactly what the message printed during
				 * the rollback told the player was still recoverable. */
				Console.WriteLine($"IMPORTANT: {unrestoredOriginals} original file(s) could not be restored and are being KEPT in '{stagingRoot}'.");
				Console.WriteLine("Do not delete that directory: it holds the only copy of them. The paths they belong at are listed above.");
			}
			else
			{
				Console.WriteLine("Clearing the staging directory...");
				if (!TryDeleteDirectory(stagingRoot))
				{
					Console.WriteLine($"NOTE: Part of the staging directory '{stagingRoot}' could not be removed yet (on Windows this is normal when the updater has just replaced itself). It holds no files the install needs, and the next update clears it.");
				}
			}
		}

		return committed;
	}

	/// <summary>
	/// Bounds patch parallelism to the machine's core count.
	/// </summary>
	/// <remarks>
	/// Each worker holds its own open <see cref="ZipArchive"/> and a 64 KB copy buffer, so
	/// this is also the bound on how many archive handles and buffers exist at once. The
	/// default partitioner would otherwise grow the worker count under I/O wait.
	/// </remarks>
	private static ParallelOptions ParallelOptions => new ParallelOptions
	{
		MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
	};

	/// <summary>
	/// Resolves a manifest-supplied path under the install root, additionally refusing
	/// anything inside the updater's own staging directory.
	/// </summary>
	/// <remarks>
	/// The staging directory is inside the install root, so containment alone allows a
	/// manifest to name a path within it. Nothing legitimate does, and something that did
	/// could steer the transaction's own bookkeeping — deleting a staged original before the
	/// rollback that needs it, for instance.
	/// </remarks>
	private static string ResolveManifestPath(string relativePath, string stagingRoot)
	{
		string fullPath = PathContainment.ResolveOrThrow(WorkingDirectory, relativePath);

		string stagingPrefix = stagingRoot.EndsWith(Path.DirectorySeparatorChar)
			? stagingRoot
			: stagingRoot + Path.DirectorySeparatorChar;

		StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		if (fullPath.StartsWith(stagingPrefix, comparison))
		{
			throw new PathContainmentException(relativePath ?? "<null>", "it names a path inside the updater's staging directory");
		}

		/* The lock file is held open for the whole run, so a manifest naming it would fail the
		 * patch on Windows (where the open handle blocks the move) and quietly hand a patch
		 * author control of the concurrency guard everywhere else. Neither is a thing a real
		 * manifest wants, so it is refused for the same reason the staging directory is. */
		if (string.Equals(fullPath, Path.Combine(InstallDirectory, LockFileName), comparison) ||
			string.Equals(Path.GetFileName(fullPath), LockFileName, comparison))
		{
			throw new PathContainmentException(relativePath ?? "<null>", "it names the updater's lock file");
		}

		return fullPath;
	}

	/// <summary>
	/// Name of the append-only journal written inside the staging directory.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Staging alone makes a patch <i>reversible</i>; it does not make it <i>recoverable</i>.
	/// Displacing an original is a MOVE, so between the moment a file is staged and the moment
	/// its replacement is committed, the install's only copy of it lives under
	/// <see cref="StagingDirectoryName"/> — and the record of where it belongs lives only in the
	/// running process's memory. Kill that process (power loss, OOM, task manager, a crash) and
	/// the mapping is gone: the install is missing files, and the next run's "clear the stale
	/// staging directory" step deletes the only copies.
	/// </para>
	/// <para>
	/// The journal is that mapping, on disk, written before the process can lose it. A staging
	/// directory carrying one describes an interrupted transaction and is REPLAYED on the next
	/// run; one without a journal has nothing to restore and is cleared as before.
	/// </para>
	/// <para>
	/// It is deleted at commit BEFORE the staging directory it lives in. That ordering is what
	/// keeps a partially-removed staging directory — the normal outcome on Windows, where the
	/// updater cannot unlink its own previous image after replacing itself — from looking like
	/// an interrupted transaction and restoring the old files over the new ones on the next run.
	/// </para>
	/// </remarks>
	private const string JournalFileName = "journal.tsv";

	/// <summary>
	/// Append-only record of everything the running patch has displaced or created.
	/// </summary>
	private sealed class TransactionJournal : IDisposable
	{
		private readonly object gate = new object();
		private readonly FileStream stream;
		private readonly StreamWriter writer;

		public TransactionJournal(string path)
		{
			stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
			writer = new StreamWriter(stream) { AutoFlush = false };
		}

		/// <summary>Records an original moved into staging, and where it must go back to.</summary>
		public void RecordDisplaced(DisplacedFile record)
		{
			Append("S\t" + Escape(Path.GetFileName(record.StagedPath)) + "\t" +
				   (record.Mode.HasValue ? ((int)record.Mode.Value).ToString() : "-") + "\t" +
				   Escape(record.OriginalPath));
		}

		/// <summary>Records a path this patch created where nothing existed.</summary>
		public void RecordCreated(string fullPath)
		{
			Append("C\t" + Escape(fullPath));
		}

		/// <summary>
		/// Pushes everything written so far all the way to the storage device.
		/// </summary>
		/// <remarks>
		/// Every record is flushed to the OS as it is written, which is what survives the
		/// process dying — the common case by a wide margin. This is the stronger guarantee and
		/// costs a real disk sync, so it is called at phase boundaries rather than per record:
		/// a patch touching thousands of files would otherwise pay thousands of fsyncs to
		/// narrow a power-loss window that a phase boundary already narrows to one phase.
		/// </remarks>
		public void Sync()
		{
			lock (gate)
			{
				writer.Flush();
				stream.Flush(true);
			}
		}

		private void Append(string line)
		{
			lock (gate)
			{
				writer.WriteLine(line);
				// Managed buffer -> OS. Survives this process being killed, which is what the
				// journal is for; Sync() above is what survives the machine losing power.
				writer.Flush();
			}
		}

		public void Dispose()
		{
			try
			{
				lock (gate)
				{
					writer.Flush();
					writer.Dispose();
				}
			}
			catch (Exception)
			{
			}
		}

		/// <summary>Makes a path safe to store in one tab-separated line.</summary>
		internal static string Escape(string value)
		{
			return value
				.Replace("\\", "\\\\")
				.Replace("\t", "\\t")
				.Replace("\r", "\\r")
				.Replace("\n", "\\n");
		}

		/// <summary>Reverses <see cref="Escape"/>.</summary>
		internal static string Unescape(string value)
		{
			var sb = new System.Text.StringBuilder(value.Length);
			for (int i = 0; i < value.Length; ++i)
			{
				if (value[i] != '\\' || i + 1 >= value.Length)
				{
					sb.Append(value[i]);
					continue;
				}
				++i;
				switch (value[i])
				{
					case 't': sb.Append('\t'); break;
					case 'r': sb.Append('\r'); break;
					case 'n': sb.Append('\n'); break;
					case '\\': sb.Append('\\'); break;
					default: sb.Append('\\').Append(value[i]); break;
				}
			}
			return sb.ToString();
		}
	}

	/// <summary>
	/// Replays a journal left behind by an updater that died mid-apply, putting the install
	/// back the way it was before that patch started.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Runs before anything else touches the install, because the alternative is applying a new
	/// patch on top of a half-applied one — and because the staging directory this reads is the
	/// same one the next transaction is about to reuse.
	/// </para>
	/// <para>
	/// Removals happen before restores, for the same reason the in-process rollback does it in
	/// that order: a path can appear as both created and displaced, and a restore must have the
	/// last word.
	/// </para>
	/// <para>
	/// Every path is re-validated against the install root before it is acted on. The journal
	/// lives inside the install directory, so it is only as trustworthy as that directory — but
	/// "only as trustworthy as the install" is a much smaller claim than "may name any path on
	/// the machine", and this is the one code path that turns a file's CONTENTS into a
	/// filesystem destination.
	/// </para>
	/// </remarks>
	/// <returns>True when the journal was fully replayed and staging may be cleared.</returns>
	private static bool RecoverInterruptedTransaction(string stagingRoot)
	{
		string journalPath = Path.Combine(stagingRoot, JournalFileName);
		if (!File.Exists(journalPath))
		{
			// No journal: either the previous run committed (which deletes the journal first)
			// or it died before displacing anything. Nothing to restore either way.
			return true;
		}

		Console.WriteLine($"A previous update did not finish. Replaying '{journalPath}' to restore the install...");

		string[] lines;
		try
		{
			lines = File.ReadAllLines(journalPath);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"ERROR: The recovery journal could not be read ({ex.Message}). Leaving '{stagingRoot}' in place — it holds the only copy of any displaced file.");
			return false;
		}

		var displaced = new List<DisplacedFile>();
		var created = new List<string>();

		foreach (string line in lines)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] parts = line.Split('\t');
			try
			{
				if (parts.Length == 4 && parts[0] == "S")
				{
					string originalPath = TransactionJournal.Unescape(parts[3]);
					if (!IsInsideInstall(originalPath))
					{
						Console.WriteLine($"SECURITY: Ignoring a journal entry that names '{originalPath}', outside the install root.");
						continue;
					}

					UnixFileMode? mode = null;
					if (parts[2] != "-" && int.TryParse(parts[2], out int rawMode))
					{
						mode = (UnixFileMode)(rawMode & 0x1FF);
					}

					displaced.Add(new DisplacedFile
					{
						OriginalPath = originalPath,
						StagedPath = Path.Combine(stagingRoot, "orig", TransactionJournal.Unescape(parts[1])),
						Mode = mode,
					});
				}
				else if (parts.Length == 2 && parts[0] == "C")
				{
					string createdPath = TransactionJournal.Unescape(parts[1]);
					if (!IsInsideInstall(createdPath))
					{
						Console.WriteLine($"SECURITY: Ignoring a journal entry that names '{createdPath}', outside the install root.");
						continue;
					}
					created.Add(createdPath);
				}
				else
				{
					// A truncated final line is expected when the process was killed mid-write.
					Console.WriteLine($"Ignoring an incomplete journal line: '{line}'");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ignoring an unreadable journal line ('{ex.Message}'): {line}");
			}
		}

		Console.WriteLine($"Recovering {displaced.Count} displaced file(s) and removing {created.Count} file(s) the interrupted patch created.");

		foreach (string createdPath in created)
		{
			if (File.Exists(createdPath))
			{
				TryDeleteFile(createdPath, MaxFileOperationRetries);
			}
		}

		int unrestored = 0;
		for (int i = displaced.Count - 1; i >= 0; --i)
		{
			if (!RestoreDisplaced(displaced[i]))
			{
				unrestored += 1;
			}
		}

		if (unrestored > 0)
		{
			Console.WriteLine($"IMPORTANT: {unrestored} file(s) could not be restored and are being KEPT in '{stagingRoot}'. Do not delete that directory.");
			return false;
		}

		Console.WriteLine("Recovery complete. The install is back at its previous version.");
		try
		{
			File.Delete(journalPath);
		}
		catch (Exception)
		{
		}
		return true;
	}

	/// <summary>
	/// True when <paramref name="fullPath"/> lies inside the install root.
	/// </summary>
	/// <remarks>
	/// Used on paths read back from the recovery journal. Unlike
	/// <see cref="PathContainment.TryResolve"/> this takes an already-absolute path and asks
	/// only the containment question, because the journal stores resolved paths that were
	/// already validated when they were written — this is the re-check, not the first check.
	/// </remarks>
	private static bool IsInsideInstall(string fullPath)
	{
		if (string.IsNullOrWhiteSpace(fullPath) || fullPath.IndexOf('\0') >= 0)
		{
			return false;
		}

		try
		{
			string root = Path.GetFullPath(InstallDirectory);
			if (!root.EndsWith(Path.DirectorySeparatorChar))
			{
				root += Path.DirectorySeparatorChar;
			}

			string candidate = Path.GetFullPath(fullPath);
			return candidate.StartsWith(root, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal);
		}
		catch (Exception)
		{
			return false;
		}
	}
	/// <summary>
	/// Refuses a manifest in which two entries write the same target path.
	/// </summary>
	/// <returns>False when the patch must not be applied.</returns>
	private static bool ValidateNoDuplicateTargets(PatchManifest manifest, string stagingRoot)
	{
		HashSet<string> seen = new HashSet<string>(PathComparer);

		foreach (string relativePath in
			(manifest.NewFiles ?? new List<NewFileEntry>()).Select(f => f.RelativePath)
			.Concat((manifest.ModifiedFiles ?? new List<ModifiedFileEntry>()).Select(f => f.RelativePath)))
		{
			string fullPath;
			try
			{
				fullPath = ResolveManifestPath(relativePath, stagingRoot);
			}
			catch (PathContainmentException ex)
			{
				// Reported and refused here. The write phases would reject it too, but this
				// pass runs before anything has been staged, so refusing now costs nothing.
				Console.WriteLine($"SECURITY: {ex.Message}");
				return false;
			}

			if (!seen.Add(fullPath))
			{
				Console.WriteLine($"ERROR: The patch manifest writes '{relativePath}' more than once. Refusing to apply it.");
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Refuses an archive whose manifest describes a different upgrade than the one requested.
	/// </summary>
	/// <returns>False when the patch must not be applied.</returns>
	private static bool ManifestMatchesRequestedUpgrade(PatchManifest manifest)
	{
		if (string.IsNullOrEmpty(manifest.OldVersion) || string.IsNullOrEmpty(manifest.NewVersion))
		{
			Console.WriteLine("WARNING: The patch manifest does not declare its versions; applying it on the strength of its file name alone.");
			return true;
		}

		if (!string.IsNullOrEmpty(Version) && !string.Equals(manifest.OldVersion.Trim(), Version.Trim(), StringComparison.Ordinal))
		{
			Console.WriteLine($"ERROR: This patch upgrades from {manifest.OldVersion}, but the installed client is {Version}. Refusing to apply it.");
			return false;
		}

		if (!string.IsNullOrEmpty(LatestVersion) && !string.Equals(manifest.NewVersion.Trim(), LatestVersion.Trim(), StringComparison.Ordinal))
		{
			Console.WriteLine($"ERROR: This patch upgrades to {manifest.NewVersion}, but {LatestVersion} was requested. Refusing to apply it.");
			return false;
		}

		return true;
	}

	/// <summary>
	/// Moves <paramref name="fullPath"/> into the staging directory, recording what is needed
	/// to put it back.
	/// </summary>
	/// <returns>False when the file could not be moved aside, in which case the patch fails.</returns>
	private static bool TryStageAside(string fullPath, string stagedOriginals, [NotNullWhen(true)] out DisplacedFile? record, [NotNullWhen(false)] out string? error)
	{
		record = null;
		error = null;

		try
		{
			// Flat, serial names. Mirroring the install's tree inside staging would mean
			// re-deriving relative paths against a root that may itself be reached through a
			// symlink; the mapping is held in the record instead.
			string stagedPath = Path.Combine(stagedOriginals, Interlocked.Increment(ref stagingCounter).ToString() + ".staged");
			UnixFileMode? mode = TryGetUnixFileMode(fullPath);

			if (File.Exists(stagedPath) && !TryDeleteFile(stagedPath, MaxFileOperationRetries))
			{
				error = $"a stale staged file at '{stagedPath}' could not be removed";
				return false;
			}

			if (!TryMoveFile(fullPath, stagedPath, MaxFileOperationRetries))
			{
				// A rename can fail for reasons a copy will not — a bind mount inside the
				// install root puts the two paths on different devices, for one.
				try
				{
					File.Copy(fullPath, stagedPath, true);
				}
				catch (Exception copyEx)
				{
					error = copyEx.Message;
					return false;
				}

				if (!TryDeleteFile(fullPath, MaxFileOperationRetries))
				{
					error = $"'{fullPath}' could not be removed after being copied aside";
					return false;
				}
			}

			record = new DisplacedFile
			{
				OriginalPath = fullPath,
				StagedPath = stagedPath,
				Mode = mode,
			};
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	/// <summary>
	/// Puts one staged original back where it came from.
	/// </summary>
	/// <returns>
	/// False when the original is still sitting in staging, which means the staging directory
	/// holds the ONLY copy of it and must not be cleared.
	/// </returns>
	private static bool RestoreDisplaced(DisplacedFile? record)
	{
		if (record == null || !File.Exists(record.StagedPath))
		{
			return true;
		}

		Console.WriteLine($"\tRestoring '{record.OriginalPath}'...");

		if (File.Exists(record.OriginalPath))
		{
			// Best effort: whatever is there now came from the failed patch.
			TryDeleteFile(record.OriginalPath, MaxFileOperationRetries);
		}

		try
		{
			string parent = Path.GetDirectoryName(record.OriginalPath);
			if (!string.IsNullOrEmpty(parent))
			{
				Directory.CreateDirectory(parent);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"\tCRITICAL: Could not recreate the directory for '{record.OriginalPath}': {ex.Message}");
			return false;
		}

		if (TryMoveFile(record.StagedPath, record.OriginalPath, MaxFileOperationRetries))
		{
			TryApplyUnixFileMode(record.OriginalPath, record.Mode);
			Console.WriteLine($"\tSuccessfully restored '{record.OriginalPath}'.");
			return true;
		}

		Console.WriteLine($"\tCRITICAL: Failed to restore '{record.OriginalPath}' from '{record.StagedPath}'. The file is intact at that path and can be moved back manually.");
		return false;
	}

	/// <summary>
	/// Recursive directory delete that never throws, and that removes as much as it can when
	/// it cannot remove everything.
	/// </summary>
	/// <remarks>
	/// The per-file sweep is there for one specific case: the updater patching ITSELF.
	/// <c>Updater.exe</c> ships inside the install and is therefore diffed like any other
	/// file, and Windows will happily RENAME a running executable — which is what staging it
	/// aside does — but will not unlink it. So the self-update commits correctly and exactly
	/// one file in staging, the running updater's own previous image, cannot be deleted until
	/// the process ends. <see cref="System.IO.Directory.Delete(string,bool)"/> is all-or-nothing
	/// and would leave the entire staging tree (a full copy of every patched file) behind over
	/// that one entry. Sweeping first leaves only the locked file, and the next run clears it.
	/// </remarks>
	private static bool TryDeleteDirectory(string path)
	{
		if (!Directory.Exists(path))
		{
			return true;
		}

		try
		{
			foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
			{
				try
				{
					File.Delete(file);
				}
				catch (Exception)
				{
					// Swept, not required. The recursive delete below reports the real outcome.
				}
			}
		}
		catch (Exception)
		{
		}

		for (int i = 0; i < MaxFileOperationRetries; ++i)
		{
			try
			{
				if (!Directory.Exists(path))
				{
					return true;
				}
				Directory.Delete(path, true);
				return true;
			}
			catch (Exception ex)
			{
				if (i == MaxFileOperationRetries - 1)
				{
					Console.WriteLine($"WARNING: Could not fully remove directory '{path}': {ex.Message}");
					return false;
				}
				Thread.Sleep(FileOperationRetryDelayMs);
			}
		}
		return false;
	}

	/// <summary>
	/// Reads a file's POSIX permission bits, or null on Windows and on any failure.
	/// </summary>
	private static UnixFileMode? TryGetUnixFileMode(string path)
	{
		if (OperatingSystem.IsWindows())
		{
			return null;
		}

		try
		{
			return File.Exists(path) ? File.GetUnixFileMode(path) : (UnixFileMode?)null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>
	/// Applies POSIX permission bits, if there are any to apply and the platform has them.
	/// </summary>
	private static void TryApplyUnixFileMode(string path, UnixFileMode? mode)
	{
		if (mode == null || OperatingSystem.IsWindows())
		{
			return;
		}

		try
		{
			File.SetUnixFileMode(path, mode.Value);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"WARNING: Could not set permissions on '{path}': {ex.Message}");
		}
	}

	/// <summary>
	/// Converts a manifest-supplied octal permission value to a <see cref="UnixFileMode"/>.
	/// </summary>
	/// <remarks>
	/// Masked to the low twelve bits, so a manifest cannot ask for anything outside the
	/// permission and setuid/setgid/sticky range — and setuid is stripped outright below.
	/// </remarks>
	private static UnixFileMode ToUnixFileMode(int rawMode)
	{
		// 0o777 only. A patch has no business setting setuid, setgid or the sticky bit on a
		// player's install, and honouring one from a manifest would be a privilege-escalation
		// primitive handed to whoever wrote the archive.
		return (UnixFileMode)(rawMode & 0x1FF);
	}

	/// <summary>
	/// Gives a newly written file its permissions.
	/// </summary>
	/// <remarks>
	/// The manifest's value wins when it has one. Otherwise the file's first bytes decide:
	/// an executable image or a script that lands without its execute bit is unusable, and
	/// nothing else in the pipeline records the bit. The check reads four bytes and is only
	/// ever additive — it grants execute alongside read, and never takes a permission away.
	/// </remarks>
	private static void ApplyModeForNewFile(string path, int? manifestMode)
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		if (manifestMode.HasValue)
		{
			TryApplyUnixFileMode(path, ToUnixFileMode(manifestMode.Value));
			return;
		}

		if (!LooksLikeExecutableImage(path))
		{
			return;
		}

		try
		{
			UnixFileMode mode = File.GetUnixFileMode(path);
			if ((mode & UnixFileMode.UserRead) != 0) mode |= UnixFileMode.UserExecute;
			if ((mode & UnixFileMode.GroupRead) != 0) mode |= UnixFileMode.GroupExecute;
			if ((mode & UnixFileMode.OtherRead) != 0) mode |= UnixFileMode.OtherExecute;
			File.SetUnixFileMode(path, mode);
			Console.WriteLine($"\tMarked '{Path.GetFileName(path)}' executable (it is an executable image or script).");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"WARNING: Could not mark '{path}' executable: {ex.Message}");
		}
	}

	/// <summary>
	/// True when the file begins with an ELF, Mach-O or shebang signature.
	/// </summary>
	private static bool LooksLikeExecutableImage(string path)
	{
		try
		{
			using (FileStream stream = File.OpenRead(path))
			{
				byte[] magic = new byte[4];
				int read = stream.Read(magic, 0, magic.Length);
				if (read < 2)
				{
					return false;
				}

				// "#!" — a script.
				if (magic[0] == 0x23 && magic[1] == 0x21)
				{
					return true;
				}

				if (read < 4)
				{
					return false;
				}

				// ELF.
				if (magic[0] == 0x7F && magic[1] == 0x45 && magic[2] == 0x4C && magic[3] == 0x46)
				{
					return true;
				}

				uint word = (uint)(magic[0] << 24 | magic[1] << 16 | magic[2] << 8 | magic[3]);
				// Mach-O 32/64, either endianness, and the universal ("fat") wrapper.
				return word == 0xFEEDFACE || word == 0xFEEDFACF ||
					   word == 0xCEFAEDFE || word == 0xCFFAEDFE ||
					   word == 0xCAFEBABE || word == 0xBEBAFECA;
			}
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>
	/// Gathers and creates all unique directories required for new and modified files.
	/// </summary>
	/// <param name="manifest">The patch manifest containing file entries.</param>
	/// <param name="stagingRoot">The updater's staging directory, which manifests may not name.</param>
	/// <param name="parallelExceptions">A ConcurrentBag to collect exceptions from parallel operations.</param>
	private static void PreCreateDirectories(PatchManifest manifest, string stagingRoot, ConcurrentBag<Exception> parallelExceptions)
	{
		HashSet<string> directoriesToCreate = new HashSet<string>();

		/* CONTAINMENT, and it has to happen HERE as well as in the phases that write.
		 *
		 * This pass runs FIRST and calls Directory.CreateDirectory on whatever it derives, so
		 * an escaping entry creates directories outside the install before any later check
		 * could refuse the file itself — enough on its own to litter a system, and enough to
		 * pre-create the parent a subsequent entry needs.
		 *
		 * A rejection is recorded rather than thrown because the caller already treats a
		 * non-empty exception bag as a fatal, rollback-triggering failure; adding to it keeps
		 * one failure convention for the whole apply. */
		if (manifest.NewFiles != null)
		{
			foreach (var newFile in manifest.NewFiles)
			{
				if (!TryQueueDirectory(newFile.RelativePath, stagingRoot, directoriesToCreate, parallelExceptions))
				{
					return;
				}
			}
		}

		if (manifest.ModifiedFiles != null)
		{
			foreach (var modifiedFile in manifest.ModifiedFiles)
			{
				if (!TryQueueDirectory(modifiedFile.RelativePath, stagingRoot, directoriesToCreate, parallelExceptions))
				{
					return;
				}
			}
		}

		if (directoriesToCreate.Count > 0)
		{
			Console.WriteLine($"Pre-creating {directoriesToCreate.Count} directories...");
			foreach (string dirPath in directoriesToCreate)
			{
				try
				{
					Directory.CreateDirectory(dirPath);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"\tError creating directory {dirPath}: {ex.Message}");
					parallelExceptions.Add(ex);
					return;
				}
			}
		}
	}

	/// <summary>
	/// Resolves <paramref name="relativePath"/> under the install root and queues its parent
	/// directory for creation.
	/// </summary>
	/// <param name="relativePath">The manifest-supplied path, which is untrusted.</param>
	/// <param name="stagingRoot">The updater's staging directory, which manifests may not name.</param>
	/// <param name="directoriesToCreate">Accumulator of directories to create.</param>
	/// <param name="parallelExceptions">Failure bag; a rejection is added here.</param>
	/// <returns>False when the path was refused, in which case the caller must stop.</returns>
	private static bool TryQueueDirectory(string relativePath, string stagingRoot, HashSet<string> directoriesToCreate, ConcurrentBag<Exception> parallelExceptions)
	{
		string fullPath;
		try
		{
			fullPath = ResolveManifestPath(relativePath, stagingRoot);
		}
		catch (PathContainmentException ex)
		{
			Console.WriteLine($"SECURITY: {ex.Message}");
			parallelExceptions.Add(ex);
			return false;
		}

		string directoryPath = Path.GetDirectoryName(fullPath);
		if (!string.IsNullOrEmpty(directoryPath))
		{
			directoriesToCreate.Add(directoryPath);
		}
		return true;
	}


	/// <summary>
	/// Computes the XxHash128 hash of a file.
	/// </summary>
	/// <param name="filePath">The path to the file.</param>
	/// <returns>The XxHash128 hash as a lowercase hexadecimal string (32 characters for 128-bit hash).</returns>
	private static string ComputeFileHash(string filePath)
	{
		using (var stream = File.OpenRead(filePath))
		{
			XxHash128 xxHash128 = new XxHash128();

			byte[] buffer = new byte[65536]; // 64KB buffer
			int bytesRead;
			while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
			{
				ReadOnlySpan<byte> dataSpan = new ReadOnlySpan<byte>(buffer, 0, bytesRead);
				xxHash128.Append(dataSpan);
			}

			byte[] hashBytes = xxHash128.GetCurrentHash();
			return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
		}
	}

	/// <summary>
	/// Requests a graceful shutdown of <paramref name="process"/> in a platform-appropriate
	/// way. Returns true if the request was delivered; false if it could not be (in which
	/// case the caller should fall back to a forced kill).
	/// </summary>
	/// <remarks>
	/// <see cref="Process.CloseMainWindow"/> is Windows-only — on Linux and macOS .NET
	/// throws <see cref="PlatformNotSupportedException"/>. The previous implementation
	/// called it unconditionally and swallowed that exception in a broad catch, so on those
	/// platforms the client was never asked to exit and never killed: the updater went on
	/// to patch files underneath a live process and then started a second client instance
	/// alongside the first.
	/// </remarks>
	private static bool TryRequestGracefulExit(Process process)
	{
		try
		{
			if (OperatingSystem.IsWindows())
			{
				return process.CloseMainWindow();
			}

			int result = SysKill(process.Id, SIGTERM);
			if (result != 0)
			{
				Console.WriteLine($"WARNING: kill(SIGTERM) on PID {process.Id} failed with errno {Marshal.GetLastWin32Error()}.");
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"WARNING: Could not request graceful exit for PID {process.Id}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Attempts to gracefully close and then forcefully kill a process by its PID.
	/// Includes robust waiting for process exit.
	/// </summary>
	/// <param name="pidToKill">The process ID of the launcher process to manage.</param>
	private static void KillLauncherProcess(int pidToKill)
	{
		if (pidToKill <= 0)
		{
			return;
		}

		if (pidToKill == Environment.ProcessId)
		{
			Console.WriteLine($"Refusing to terminate PID {pidToKill}: that is this process.");
			return;
		}

		try
		{
			Process launcherProcess = Process.GetProcessById(pidToKill);
			if (launcherProcess.HasExited)
			{
				Console.WriteLine($"Launcher process with PID {pidToKill} was already exited.");
				return;
			}

			/* PID reuse check.
			 *
			 * The launcher hands us its PID and then may exit on its own — it crashed, the
			 * player closed it, the window manager killed it. The kernel is free to hand that
			 * number to something else immediately afterwards, and on Linux under load it
			 * does. Without this the updater sends SIGTERM and then SIGKILL to whatever
			 * happens to hold the number now, which is an unrelated program on the player's
			 * machine being killed by a game updater.
			 *
			 * The launcher started this process, so the launcher is necessarily older than it.
			 * Anything holding that PID and younger than us cannot be the launcher. */
			try
			{
				DateTime ourStart = Process.GetCurrentProcess().StartTime;
				if (launcherProcess.StartTime > ourStart)
				{
					Console.WriteLine($"PID {pidToKill} belongs to a process started after this updater; the launcher has already exited and the PID was reused. Leaving it alone.");
					return;
				}
			}
			catch (Exception ex)
			{
				// Start times are not readable on every platform/permission combination. The
				// original behaviour (kill it) is restored rather than skipping the kill:
				// patching a live install is the worse of the two failures.
				Console.WriteLine($"WARNING: Could not verify the identity of PID {pidToKill} ({ex.Message}); proceeding.");
			}

			Console.WriteLine($"Attempting to close launcher process with PID {pidToKill} gracefully...");

			if (TryRequestGracefulExit(launcherProcess))
			{
				if (launcherProcess.WaitForExit(GracefulExitTimeoutMs))
				{
					Console.WriteLine($"Launcher process with PID {pidToKill} exited gracefully.");
					Thread.Sleep(PostKillSettleMs);
					return;
				}
				Console.WriteLine($"Launcher process with PID {pidToKill} did not exit within {GracefulExitTimeoutMs}ms, forcing kill...");
			}
			else
			{
				Console.WriteLine($"Graceful exit request could not be delivered to PID {pidToKill}, forcing kill...");
			}

			// Fall through to a forced kill on every path where the graceful request
			// either failed to deliver or was ignored. Patching a live install is far
			// worse than an ungraceful client shutdown.
			launcherProcess.Kill();
			if (launcherProcess.WaitForExit(ForceKillTimeoutMs))
			{
				Console.WriteLine($"Killed launcher process with PID: {pidToKill}");
			}
			else
			{
				Console.WriteLine($"ERROR: Launcher process with PID {pidToKill} is still running after a forced kill. Patching may fail on locked files.");
			}
		}
		catch (ArgumentException)
		{
			Console.WriteLine($"Launcher process with PID {pidToKill} not found (already exited or never started).");
		}
		catch (InvalidOperationException)
		{
			Console.WriteLine($"Launcher process with PID {pidToKill} exited while it was being shut down.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error managing launcher process {pidToKill}: {ex.Message}");
		}

		Thread.Sleep(PostKillSettleMs); // Small delay after attempting to kill the process.
	}

	/// <summary>
	/// Attempts to start the client executable and then exits the patcher.
	/// </summary>
	/// <param name="executable">The name or relative path of the executable to launch.</param>
	/// <param name="pidToKill">The process ID of the launcher process to kill before starting the executable.</param>
	static void TryStartExecutableAndExit(string executable, int pidToKill, int exitCode = ExitSuccess)
	{
		KillLauncherProcess(pidToKill);

		if (!string.IsNullOrEmpty(executable))
		{
			try
			{
				/* Contained as well. -exe is supplied on the command line rather than by the
				 * manifest, so it is a rung below the archive entries in trust — but it is the
				 * one argument that ends in Process.Start, and "the launcher passes a constant"
				 * is a property of today's caller, not of this program. Restricting it to the
				 * install root costs nothing (the client executable is always there) and takes
				 * away the process-launch primitive. */
				if (!PathContainment.TryResolve(WorkingDirectory, executable, out string fullExecutablePath, out string reason))
				{
					Console.WriteLine($"SECURITY: Refusing to start '{executable}': {reason}");
					Environment.Exit(ExitRefused);
					return;
				}

				ProcessStartInfo startInfo = new ProcessStartInfo(fullExecutablePath)
				{
					WorkingDirectory = Path.GetDirectoryName(fullExecutablePath),
					UseShellExecute = false,
				};
				Process.Start(startInfo);
				Console.WriteLine($"Started executable: {fullExecutablePath}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error starting executable '{executable}': {ex.Message}");
			}
		}
		Environment.Exit(exitCode);
	}
}