using System.Diagnostics;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FishMMO.Shared;

namespace FishMMO.Patcher.Tests;

/// <summary>
/// End-to-end checks of the Updater applying patch archives: each scenario builds a small install
/// around a copy of the real Updater build, writes an archive the way the generator does, runs the
/// Updater as its own process and inspects the install afterwards.
/// </summary>
/// <remarks>
/// Covers patch format 2 (zstd deltas, renamed files, zstd-compressed new files) and that format 1
/// archives still apply; that every failure leaves the install exactly as it was; and that a
/// manifest this Updater does not fully understand is refused before anything is touched.
/// </remarks>
internal static class PatchApplyTests
{
	private const string OldVersion = "1.0.0";
	private const string NewVersion = "1.1.0";
	private const int ExitSuccess = 0;
	private const int ExitPatchFailed = 2;

	private static int passed;
	private static int failed;
	private static readonly List<string> failures = new List<string>();

	public static (int passed, int failed, List<string> failures) Run()
	{
		string updaterBuild = FindUpdaterBuild();
		Console.WriteLine();
		Console.WriteLine($"-- patch application (Updater build: {updaterBuild}) --");

		FormatTwoAppliesEverything(updaterBuild);
		CorruptDeltaRollsBack(updaterBuild);
		DriftedInstallIsRefused(updaterBuild);
		FutureFormatIsRefused(updaterBuild);
		FormatOneWithFormatTwoFieldsIsRefused(updaterBuild);
		EncodedNewFileWithoutSizeIsRefused(updaterBuild);
		FormatOneStillApplies(updaterBuild);
		LargeFileDelta(updaterBuild);

		return (passed, failed, failures);
	}

	// ── Scenarios ───────────────────────────────────────────────────

	private static void FormatTwoAppliesEverything(string updaterBuild)
	{
		using Fixture f = Fixture.Create(updaterBuild);
		var rnd = new Random(11);
		byte[] modifiedOld = RandomBytes(rnd, 300_000);
		byte[] modifiedNew = Insert(modifiedOld, 1000, Encoding.ASCII.GetBytes("inserted bytes shift everything after them"));
		byte[] bundleOld = RandomBytes(rnd, 200_000);
		byte[] bundleNew = Replace(bundleOld, 5000, Encoding.ASCII.GetBytes("a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5.bundle"));
		byte[] keep = RandomBytes(rnd, 50_000);
		byte[] gone = RandomBytes(rnd, 10_000);
		byte[] added = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("a brand new file\n", 4000)));
		byte[] scriptOld = Encoding.ASCII.GetBytes("#!/bin/sh\necho old\n");
		byte[] scriptNew = Encoding.ASCII.GetBytes("#!/bin/sh\necho new\n");

		string oldBundle = "Data/aa/scene_moron2_11111111111111111111111111111111.bundle";
		string newBundle = "Data/aa/scene_moron2_22222222222222222222222222222222.bundle";
		f.Install("Data/modified.bin", modifiedOld);
		f.Install(oldBundle, bundleOld);
		f.Install("Data/keep.bin", keep);
		f.Install("Data/gone.bin", gone);
		f.Install("run.sh", scriptOld, executable: true);

		var p = new PatchBuilder(f);
		p.Delta("Data/modified.bin", modifiedOld, modifiedNew);
		p.Delta(newBundle, bundleOld, bundleNew, sourcePath: oldBundle);
		p.Delta("run.sh", scriptOld, scriptNew);
		p.NewFile("Data/sub/new.bin", added);
		p.Delete("Data/gone.bin");
		p.Delete(oldBundle);
		p.WriteV2();

		int exit = f.RunUpdater();
		Check(exit == ExitSuccess, "format 2: the Updater reports success", f);
		Check(f.Has("Data/modified.bin", modifiedNew), "format 2: a changed file is rebuilt from its delta", f);
		Check(f.Has(newBundle, bundleNew) && !f.Exists(oldBundle), "format 2: a renamed bundle is rebuilt under its new name and the old name removed", f);
		Check(f.Has("Data/sub/new.bin", added), "format 2: a zstd-compressed new file lands, in a new directory", f);
		Check(!f.Exists("Data/gone.bin"), "format 2: a deleted file is removed", f);
		Check(f.Has("Data/keep.bin", keep), "format 2: an untouched file is left alone", f);
		Check(f.Has("run.sh", scriptNew) && (OperatingSystem.IsWindows() || f.IsExecutable("run.sh")), "format 2: a patched executable keeps its execute bit", f);
		Check(f.StagingGone() && !f.PatchArchiveExists(), "format 2: staging cleaned up and the archive removed", f);
	}

	private static void CorruptDeltaRollsBack(string updaterBuild)
	{
		using Fixture f = Fixture.Create(updaterBuild);
		var rnd = new Random(12);
		byte[] aOld = RandomBytes(rnd, 100_000), aNew = Insert(aOld, 10, new byte[] { 1, 2, 3 });
		byte[] bOld = RandomBytes(rnd, 100_000), bNew = Insert(bOld, 20, new byte[] { 4, 5, 6 });
		string oldName = "Data/b_33333333333333333333333333333333.bundle";
		string newName = "Data/b_44444444444444444444444444444444.bundle";
		f.Install("Data/a.bin", aOld);
		f.Install(oldName, bOld);
		f.Install("Data/gone.bin", new byte[] { 7, 7, 7 });
		Dictionary<string, byte[]> before = f.Snapshot();

		var p = new PatchBuilder(f);
		p.Delta("Data/a.bin", aOld, aNew);
		p.Delta(newName, bOld, bNew, sourcePath: oldName, corrupt: true);
		p.NewFile("Data/new.bin", new byte[] { 9, 9 });
		p.Delete(oldName);
		p.Delete("Data/gone.bin");
		p.WriteV2();

		int exit = f.RunUpdater();
		Check(exit == ExitPatchFailed, "corrupt delta: the Updater reports failure", f);
		Check(f.SnapshotEquals(before), "corrupt delta: the install is exactly as it was (no new files, nothing deleted or changed)", f);
		Check(f.StagingGone() && f.PatchArchiveExists(), "corrupt delta: staging cleaned up, archive kept for a retry", f);
	}

	private static void DriftedInstallIsRefused(string updaterBuild)
	{
		using Fixture f = Fixture.Create(updaterBuild);
		var rnd = new Random(13);
		byte[] built = RandomBytes(rnd, 50_000);
		byte[] onDisk = (byte[])built.Clone();
		onDisk[100] ^= 0xFF;
		f.Install("Data/a.bin", onDisk);
		Dictionary<string, byte[]> before = f.Snapshot();

		var p = new PatchBuilder(f);
		p.Delta("Data/a.bin", built, Insert(built, 5, new byte[] { 1 }));
		p.WriteV2();

		int exit = f.RunUpdater();
		Check(exit == ExitPatchFailed && f.SnapshotEquals(before), "drifted install: a file not at the expected old version is refused, nothing touched", f);
	}

	private static void FutureFormatIsRefused(string updaterBuild)
	{
		using Fixture f = Fixture.Create(updaterBuild);
		f.Install("Data/a.bin", new byte[] { 1, 2, 3 });
		Dictionary<string, byte[]> before = f.Snapshot();
		var p = new PatchBuilder(f);
		p.NewFile("Data/b.bin", new byte[] { 4 });
		p.WriteV2(formatVersion: 3);
		int exit = f.RunUpdater();
		Check(exit == ExitPatchFailed && f.SnapshotEquals(before) && f.Log.Contains("format 3"), "future format: a format-3 manifest is refused before anything is touched", f);
	}

	private static void FormatOneWithFormatTwoFieldsIsRefused(string updaterBuild)
	{
		using Fixture f = Fixture.Create(updaterBuild);
		byte[] old = new byte[] { 1, 2, 3, 4 };
		f.Install("Data/a.bin", old);
		Dictionary<string, byte[]> before = f.Snapshot();
		var p = new PatchBuilder(f);
		p.Delta("Data/a.bin", old, new byte[] { 1, 2, 3, 4, 5 });
		p.WriteV1Named();
		int exit = f.RunUpdater();
		Check(exit == ExitPatchFailed && f.SnapshotEquals(before), "format 1 manifest carrying an encoding is refused", f);
	}

	private static void EncodedNewFileWithoutSizeIsRefused(string updaterBuild)
	{
		using Fixture f = Fixture.Create(updaterBuild);
		f.Install("Data/a.bin", new byte[] { 1 });
		Dictionary<string, byte[]> before = f.Snapshot();
		var p = new PatchBuilder(f);
		p.NewFile("Data/b.bin", new byte[] { 4, 5, 6 }, omitSize: true);
		p.WriteV2();
		int exit = f.RunUpdater();
		Check(exit == ExitPatchFailed && f.SnapshotEquals(before), "an encoded new file without file_size is refused", f);
	}

	private static void FormatOneStillApplies(string updaterBuild)
	{
		using Fixture f = Fixture.Create(updaterBuild);
		var rnd = new Random(14);
		byte[] old = RandomBytes(rnd, 70_000);
		byte[] updated = (byte[])old.Clone();
		for (int i = 30_000; i < 30_100; i++) updated[i] ^= 0x5A;
		byte[] added = new byte[] { 1, 2, 3, 4, 5 };
		f.Install("Data/a.bin", old);
		f.Install("Data/gone.bin", new byte[] { 8 });
		var p = new PatchBuilder(f);
		p.LegacyChunk("Data/a.bin", old, updated, 30_000, 100);
		p.LegacyNewFile("Data/new.bin", added);
		p.Delete("Data/gone.bin");
		p.WriteV1();
		int exit = f.RunUpdater();
		Check(exit == ExitSuccess && f.Has("Data/a.bin", updated) && f.Has("Data/new.bin", added) && !f.Exists("Data/gone.bin"),
			"format 1: an archive from the previous generator still applies", f);
	}

	private static void LargeFileDelta(string updaterBuild)
	{
		using Fixture f = Fixture.Create(updaterBuild);
		var rnd = new Random(15);
		byte[] old = RandomBytes(rnd, 70 * 1024 * 1024);
		byte[] updated = Insert(old, 50 * 1024 * 1024, Encoding.ASCII.GetBytes("a change deep inside a large file"));
		f.Install("Data/large.bin", old);
		var p = new PatchBuilder(f);
		p.Delta("Data/large.bin", old, updated);
		long payload = p.WriteV2();
		int exit = f.RunUpdater();
		// Random content compresses not at all, so this bounds the archive to the delta (well under
		// 1 MiB) rather than the 70 MiB the file would cost shipped whole.
		Check(exit == ExitSuccess && f.Has("Data/large.bin", updated) && payload < 1024 * 1024,
			$"a 70 MiB file with a change 50 MiB in is patched from a {payload}-byte archive", f);
	}

	// ── Helpers ─────────────────────────────────────────────────────

	private static void Check(bool ok, string what, Fixture f)
	{
		if (ok)
		{
			passed++;
			Console.WriteLine($"  PASS  {what}");
		}
		else
		{
			failed++;
			string msg = $"  FAIL  {what}";
			failures.Add(msg);
			Console.WriteLine(msg);
			Console.WriteLine("        --- updater output (last 25 lines) ---");
			foreach (string line in f.Log.Split('\n').TakeLast(25))
			{
				Console.WriteLine("        " + line.TrimEnd());
			}
		}
	}

	private static string FindUpdaterBuild()
	{
		// This project's output is <root>/<Updater.Tests>/<Configuration>/<framework> (the repo's
		// Directory.Build.props puts every project under Builds/<project>/), so the Updater's is the
		// sibling <root>/Updater/<Configuration>/<framework>. The conventional
		// <project>/bin/<Configuration>/<framework> layout is tried too.
		DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
		string framework = dir.Name;
		string configuration = dir.Parent!.Name;
		DirectoryInfo projectOutput = dir.Parent.Parent!;
		string[] candidates =
		{
			Path.Combine(projectOutput.Parent!.FullName, "Updater", configuration, framework),
			Path.Combine(projectOutput.Parent!.Parent?.FullName ?? "", "Updater", "bin", configuration, framework),
		};
		foreach (string build in candidates)
		{
			if (File.Exists(Path.Combine(build, "Updater.dll")))
			{
				return build;
			}
		}
		throw new FileNotFoundException($"Build the Updater first (dotnet build ../Updater -c {configuration}); looked in {string.Join(", ", candidates)}.");
	}

	private static byte[] RandomBytes(Random rnd, int size)
	{
		byte[] b = new byte[size];
		rnd.NextBytes(b);
		return b;
	}

	private static byte[] Insert(byte[] source, int at, byte[] insert)
	{
		return source.Take(at).Concat(insert).Concat(source.Skip(at)).ToArray();
	}

	private static byte[] Replace(byte[] source, int at, byte[] with)
	{
		byte[] copy = (byte[])source.Clone();
		Array.Copy(with, 0, copy, at, with.Length);
		return copy;
	}

	internal static string Hash(byte[] data)
	{
		return Convert.ToHexString(XxHash128.Hash(data)).ToLowerInvariant();
	}

	/// <summary>An install directory with a copy of the Updater build in it.</summary>
	private sealed class Fixture : IDisposable
	{
		public string Root = "";
		public string Log = "";
		private readonly HashSet<string> updaterFiles = new HashSet<string>(StringComparer.Ordinal);

		public static Fixture Create(string updaterBuild)
		{
			var f = new Fixture { Root = Path.Combine(Path.GetTempPath(), "fishmmo-apply-" + Guid.NewGuid().ToString("N")) };
			Directory.CreateDirectory(f.Root);
			foreach (string file in Directory.GetFiles(updaterBuild))
			{
				string name = Path.GetFileName(file);
				File.Copy(file, Path.Combine(f.Root, name));
				f.updaterFiles.Add(name);
			}
			Directory.CreateDirectory(Path.Combine(f.Root, "Patches"));
			return f;
		}

		public string Full(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

		public void Install(string relative, byte[] data, bool executable = false)
		{
			string full = Full(relative);
			Directory.CreateDirectory(Path.GetDirectoryName(full)!);
			File.WriteAllBytes(full, data);
			if (executable && !OperatingSystem.IsWindows())
			{
				File.SetUnixFileMode(full, (UnixFileMode)Convert.ToInt32("755", 8));
			}
		}

		public bool Exists(string relative) => File.Exists(Full(relative));

		public bool Has(string relative, byte[] expected) => Exists(relative) && File.ReadAllBytes(Full(relative)).AsSpan().SequenceEqual(expected);

		public bool IsExecutable(string relative) => (File.GetUnixFileMode(Full(relative)) & UnixFileMode.UserExecute) != 0;

		public bool StagingGone() => !Directory.Exists(Path.Combine(Root, ".fishmmo-update-staging"));

		public string ArchivePath => Path.Combine(Root, "Patches", $"{OldVersion}-{NewVersion}.zip");

		public bool PatchArchiveExists() => File.Exists(ArchivePath);

		/// <summary>Every file of the install that is not part of the Updater build, the archive or staging.</summary>
		public Dictionary<string, byte[]> Snapshot()
		{
			var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			foreach (string file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
			{
				string rel = Path.GetRelativePath(Root, file).Replace(Path.DirectorySeparatorChar, '/');
				if (updaterFiles.Contains(rel) || rel.StartsWith("Patches/", StringComparison.Ordinal) ||
					rel.StartsWith(".fishmmo-update", StringComparison.Ordinal))
				{
					continue;
				}
				map[rel] = File.ReadAllBytes(file);
			}
			return map;
		}

		public bool SnapshotEquals(Dictionary<string, byte[]> before)
		{
			Dictionary<string, byte[]> now = Snapshot();
			return now.Count == before.Count && before.All(kv => now.TryGetValue(kv.Key, out byte[]? v) && v.AsSpan().SequenceEqual(kv.Value));
		}

		public int RunUpdater()
		{
			var psi = new ProcessStartInfo("dotnet")
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				WorkingDirectory = Root,
			};
			psi.ArgumentList.Add(Path.Combine(Root, "Updater.dll"));
			psi.ArgumentList.Add($"-version={OldVersion}");
			psi.ArgumentList.Add($"-latestversion={NewVersion}");
			using Process process = Process.Start(psi)!;
			Task<string> stdout = process.StandardOutput.ReadToEndAsync();
			Task<string> stderr = process.StandardError.ReadToEndAsync();
			if (!process.WaitForExit(300_000))
			{
				process.Kill(true);
				Log = "TIMED OUT\n" + stdout.Result + stderr.Result;
				return -1;
			}
			Log = stdout.Result + stderr.Result;
			return process.ExitCode;
		}

		public void Dispose()
		{
			try { Directory.Delete(Root, true); } catch { }
		}
	}

	/// <summary>Writes a patch archive the way the generator does.</summary>
	private sealed class PatchBuilder
	{
		private readonly Fixture fixture;
		private readonly JsonArray modified = new JsonArray();
		private readonly JsonArray added = new JsonArray();
		private readonly JsonArray deleted = new JsonArray();
		private readonly Dictionary<string, byte[]> entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

		public PatchBuilder(Fixture fixture)
		{
			this.fixture = fixture;
		}

		private string Temp(byte[] data)
		{
			string path = Path.Combine(fixture.Root, "..", Path.GetFileName(fixture.Root) + "-" + Guid.NewGuid().ToString("N"));
			File.WriteAllBytes(path, data);
			return path;
		}

		private static byte[] Encode(string? referencePath, byte[] content)
		{
			string newPath = Path.GetTempFileName();
			try
			{
				File.WriteAllBytes(newPath, content);
				using var output = new MemoryStream();
				ZstdDelta.Encode(referencePath, newPath, output, 19);
				return output.ToArray();
			}
			finally
			{
				File.Delete(newPath);
			}
		}

		public void Delta(string path, byte[] oldContent, byte[] newContent, string? sourcePath = null, bool corrupt = false)
		{
			string reference = Temp(oldContent);
			byte[] delta;
			try
			{
				delta = Encode(reference, newContent);
			}
			finally
			{
				File.Delete(reference);
			}
			if (corrupt)
			{
				delta[delta.Length / 2] ^= 0x40;
			}
			string entry = $"patches/{path}.zst";
			entries[entry] = delta;
			var node = new JsonObject
			{
				["path"] = path,
				["old_hash"] = Hash(oldContent),
				["new_hash"] = Hash(newContent),
				["patch_data_entry_name"] = entry,
				["final_file_size"] = newContent.Length,
				["encoding"] = "zstd",
			};
			if (sourcePath != null)
			{
				node["source_path"] = sourcePath;
			}
			modified.Add(node);
		}

		public void NewFile(string path, byte[] content, bool omitSize = false)
		{
			string entry = $"new_files/{path}.zst";
			entries[entry] = Encode(null, content);
			var node = new JsonObject
			{
				["path"] = path,
				["new_hash"] = Hash(content),
				["file_data_entry_name"] = entry,
				["encoding"] = "zstd",
			};
			if (!omitSize)
			{
				node["file_size"] = content.Length;
			}
			added.Add(node);
		}

		/// <summary>A format-1 chunk patch: replace <paramref name="length"/> bytes at <paramref name="offset"/>.</summary>
		public void LegacyChunk(string path, byte[] oldContent, byte[] newContent, long offset, int length)
		{
			using var ms = new MemoryStream();
			using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
			{
				w.Write(offset);
				w.Write(length);
				w.Write(length);
				w.Write(newContent, (int)offset, length);
			}
			string entry = $"patches/{path}.bin";
			entries[entry] = ms.ToArray();
			modified.Add(new JsonObject
			{
				["path"] = path,
				["old_hash"] = Hash(oldContent),
				["new_hash"] = Hash(newContent),
				["patch_data_entry_name"] = entry,
				["final_file_size"] = newContent.Length,
			});
		}

		public void LegacyNewFile(string path, byte[] content)
		{
			string entry = $"new_files/{path}";
			entries[entry] = content;
			added.Add(new JsonObject { ["path"] = path, ["new_hash"] = Hash(content), ["file_data_entry_name"] = entry });
		}

		public void Delete(string path)
		{
			deleted.Add(new JsonObject { ["path"] = path });
		}

		public long WriteV2(int formatVersion = 2) => Write("manifest-v2.json", formatVersion);

		public long WriteV1() => Write("manifest.json", null);

		/// <summary>Format-2 entries under the format-1 manifest name: must be refused.</summary>
		public long WriteV1Named() => Write("manifest.json", null);

		private long Write(string manifestName, int? formatVersion)
		{
			var manifest = new JsonObject();
			if (formatVersion.HasValue)
			{
				manifest["format_version"] = formatVersion.Value;
			}
			manifest["old_version"] = OldVersion;
			manifest["new_version"] = NewVersion;
			manifest["deleted_files"] = deleted;
			manifest["modified_files"] = modified;
			manifest["new_files"] = added;

			using (FileStream zip = File.Create(fixture.ArchivePath))
			using (var archive = new ZipArchive(zip, ZipArchiveMode.Create))
			{
				ZipArchiveEntry m = archive.CreateEntry(manifestName);
				using (Stream s = m.Open())
				{
					byte[] json = Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
					s.Write(json, 0, json.Length);
				}
				foreach (KeyValuePair<string, byte[]> e in entries)
				{
					ZipArchiveEntry entry = archive.CreateEntry(e.Key, CompressionLevel.NoCompression);
					using Stream s = entry.Open();
					s.Write(e.Value, 0, e.Value.Length);
				}
			}
			return new FileInfo(fixture.ArchivePath).Length;
		}
	}
}
