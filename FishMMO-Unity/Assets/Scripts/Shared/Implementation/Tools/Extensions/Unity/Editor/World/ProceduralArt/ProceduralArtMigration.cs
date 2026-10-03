#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The one-time move from random to deterministic IDs: re-addresses every generated asset made
	/// before IDs were path-derived, and rewrites every reference to it in the project.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why it exists.</b> Until 2026-10-02 the wrappers (terrain layers, materials, prefabs) and the
	/// layers built from loose biome textures were created with <c>AssetDatabase.CreateAsset</c>, so each
	/// has the random GUID Unity gave it — and prefabs random object IDs — and committed scenes and
	/// terrain data reference them by those. Regenerating them with derived IDs would orphan every such
	/// reference. So, for each generated file whose GUID is not <see cref="ProceduralArtPayload.GuidFor"/>
	/// its path (and each prefab whose object IDs are not <see cref="ProceduralArtFileIds"/>'), the GUID
	/// in its .meta (and the prefab's IDs) are changed, and every occurrence of the old GUID — and of an
	/// old prefab object ID paired with it — in the project is replaced with the new one.
	/// </para>
	/// <para>
	/// <b>What is rewritten.</b> Every file under <c>Assets</c> except <c>Assets/LOCAL</c> (Jim's licensed
	/// art is never touched; a LOCAL file naming an old GUID is listed instead) and the payload (which
	/// only ever references other payload, already deterministic). Text-serialized assets and .meta
	/// files by text: any 32-digit GUID token, and <c>fileID: N, guid: G</c> pairs for the prefab IDs.
	/// Binary-serialized assets (TerrainData is <c>PreferBinarySerialization</c> — Cov Viaduct's thirty
	/// tiles reference six built layers that way) by bytes: a GUID is stored as 16 bytes, each the
	/// two hex digits of the text form in swapped order (<see cref="BinaryGuid"/>); a prefab object ID
	/// as a little-endian int64, replaced only in files that also name that prefab's GUID and only for
	/// IDs too large to occur by chance. The generated wrappers themselves are included, so a prefab's
	/// references to a re-addressed material stay whole even if the regeneration after this is cancelled.
	/// </para>
	/// <para>
	/// <b>Backed up first.</b> Every file is copied to <see cref="BackupRoot"/>/&lt;timestamp&gt;/ (its
	/// project path below that) before it is written. Restoring is copying the folder back over the
	/// project with the editor closed.
	/// </para>
	/// <para>
	/// <b>The asset-database sequence, and why.</b> The rewrite is done on disk, but the editor holds
	/// loaded copies of many of these files, and a loaded native asset edited on disk keeps its stale
	/// in-memory copy — which a later save writes back over the edit. So: (1) any affected scene that is
	/// open is closed (after asking to save it, or refusing when it cannot), and the scene setup
	/// restored at the end; (2) any affected asset that is loaded with unsaved changes is saved first,
	/// so the on-disk text the rewrite starts from is the current one; (3) each re-addressed generated
	/// asset is deleted through the asset database before its file and .meta are written back with the
	/// new GUID, exactly as <see cref="ProceduralArtPayload.Prepare(string, long, List{string}, out bool)"/>
	/// replaces a payload file, so Unity drops the old GUID cleanly rather than seeing a .meta change
	/// under a live asset; (4) everything written is imported with <c>ImportAssetOptions.ForceUpdate</c>
	/// inside one <c>StartAssetEditing</c>/<c>StopAssetEditing</c> batch, so the new GUIDs and every
	/// reference to them land in a single import and Unity reloads each (unmodified, therefore
	/// reloadable) asset from the rewritten file. <c>AssetDatabase.ForceReserializeAssets</c> is NOT
	/// used: it serializes the in-memory objects, which is exactly the stale copy this avoids.
	/// </para>
	/// <para>
	/// <b>Idempotent.</b> A second run finds every GUID already derived and every prefab's IDs already
	/// deterministic, and changes nothing. The generator runs it by itself before its first
	/// deterministic regeneration whenever <see cref="NeedsMigration"/> sees a random-GUID file.
	/// </para>
	/// </remarks>
	public static class ProceduralArtMigration
	{
		/// <summary>Where rewritten files are backed up, relative to the project root.</summary>
		public const string BackupRoot = "Library/FishMMO/DeterminismMigrationBackup";

		/// <summary>One generated file to re-address.</summary>
		public sealed class Asset
		{
			public string Path;
			public string OldGuid;
			public string NewGuid;
			/// <summary>The file's new bytes when its object IDs change (a prefab, or a native asset numbered oddly); otherwise null.</summary>
			public byte[] NewContent;
			public long OldMainFileId;
			public long NewMainFileId;
		}

		/// <summary>Everything a migration would change.</summary>
		public sealed class Plan
		{
			public readonly List<Asset> Assets = new List<Asset>();
			/// <summary>Old GUID → new GUID.</summary>
			public readonly Dictionary<string, string> Guids = new Dictionary<string, string>(StringComparer.Ordinal);
			/// <summary>By OLD GUID: the object IDs inside that file that change, old → new.</summary>
			public readonly Dictionary<string, Dictionary<long, long>> FileIds = new Dictionary<string, Dictionary<long, long>>(StringComparer.Ordinal);
			public readonly List<string> Problems = new List<string>();
			public bool Empty => Assets.Count == 0;
		}

		/// <summary>What a run did.</summary>
		public sealed class Result
		{
			public Plan Plan;
			public bool Applied;
			public string BackupFolder;
			public readonly List<string> Lines = new List<string>();
			public readonly List<string> Problems = new List<string>();
			/// <summary>Files under Assets/LOCAL that name an old GUID; not rewritten, by rule.</summary>
			public readonly List<string> LocalReferences = new List<string>();

			public override string ToString()
			{
				var sb = new StringBuilder();
				int assets = Plan != null ? Plan.Assets.Count : 0;
				sb.AppendLine(Applied
					? $"Generated art migration: re-addressed {assets} generated asset(s); {Lines.Count} rewrite(s); backup in {BackupFolder ?? "(none)"}."
					: $"Generated art migration: {(assets == 0 ? "nothing to do — every generated asset already has its derived IDs" : $"NOT applied ({assets} asset(s) need it)")}.");
				foreach (string p in Problems) sb.AppendLine("PROBLEM " + p);
				foreach (string l in LocalReferences) sb.AppendLine("LOCAL (not rewritten) " + l);
				foreach (string l in Lines) sb.AppendLine(l);
				return sb.ToString();
			}
		}

		/// <summary>The generated folders whose files carry references worth migrating: everything generated except the payload.</summary>
		private static IEnumerable<string> MigratedRoots()
		{
			return ProceduralArtPayload.GeneratedRoots;
		}

		private static bool IsPayloadOrLocal(string path) => ProceduralArtPayload.IsPayload(path) || ProceduralArtPayload.IsPayload(path + "/") || BiomeLocalArtIndex.IsLocalPath(path);

		private static readonly Regex MetaGuid = new Regex(@"^guid:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
		private static readonly Regex MetaMainId = new Regex(@"^(\s*mainObjectFileID:\s*)(-?\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
		private static readonly Regex HexToken = new Regex(@"(?<![0-9a-fA-F])[0-9a-fA-F]{32}(?![0-9a-fA-F])", RegexOptions.CultureInvariant);
		private static readonly Regex GuidPair = new Regex(@"fileID: (-?\d+), guid: ([0-9a-fA-F]{32})", RegexOptions.CultureInvariant);

		/// <summary>Extensions that never hold a reference (as <see cref="AssetGuidScan"/>), plus code and data Unity never serializes.</summary>
		private static readonly HashSet<string> NeverReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr", ".hdr", ".tif", ".tiff", ".bmp", ".gif", ".webp",
			".fbx", ".obj", ".blend", ".dae", ".3ds", ".wav", ".ogg", ".mp3", ".aif", ".aiff", ".mp4", ".webm",
			".dll", ".so", ".dylib", ".a", ".jar", ".aar", ".bundle", ".pdb", ".mdb", ".zip", ".pdf",
			".cs", ".hlsl", ".shader", ".cginc", ".compute", ".glsl", ".ttf", ".otf", ".bytes", ".txt", ".md",
		};

		/// <summary>Unity-serialized files that may be binary; only these are searched as bytes.</summary>
		private static readonly HashSet<string> SerializedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".asset", ".unity", ".prefab", ".mat", ".terrainlayer", ".controller", ".overrideController", ".anim", ".playable", ".lighting",
		};

		// ── Detection and planning (disk only) ───────────────────────

		/// <summary>
		/// True when any generated file outside the payload carries a GUID other than its path's: made
		/// before IDs were deterministic. One small .meta read per file; cheap enough for every reload.
		/// </summary>
		public static bool NeedsMigration(string projectRoot = null)
		{
			foreach (string root in MigratedRoots())
			{
				string folder = Combine(projectRoot, root);
				if (!Directory.Exists(folder))
				{
					continue;
				}
				foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
				{
					string rel = Relative(projectRoot, file);
					if (rel.EndsWith(".meta", StringComparison.Ordinal) || ProceduralArtPayload.IsPayload(rel))
					{
						continue;
					}
					string guid = MetaGuidOf(file + ".meta");
					if (guid != null && guid != ProceduralArtPayload.GuidFor(rel))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Works out what a migration would change, from the files on disk under
		/// <paramref name="projectRoot"/> (the current directory when null). Changes nothing.
		/// </summary>
		public static Plan BuildPlan(string projectRoot = null)
		{
			var plan = new Plan();
			foreach (string root in MigratedRoots())
			{
				string folder = Combine(projectRoot, root);
				if (!Directory.Exists(folder))
				{
					continue;
				}
				var files = new List<string>(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories));
				files.Sort(StringComparer.Ordinal);
				foreach (string file in files)
				{
					string rel = Relative(projectRoot, file);
					if (rel.EndsWith(".meta", StringComparison.Ordinal) || ProceduralArtPayload.IsPayload(rel) || !File.Exists(file + ".meta"))
					{
						continue;
					}
					string oldGuid = MetaGuidOf(file + ".meta");
					if (oldGuid == null)
					{
						plan.Problems.Add($"{rel}: its .meta has no readable GUID; left alone");
						continue;
					}
					var asset = new Asset { Path = rel, OldGuid = oldGuid, NewGuid = ProceduralArtPayload.GuidFor(rel) };
					Dictionary<long, long> ids = PlanFileIds(rel, file, asset, plan.Problems);
					if (asset.OldGuid == asset.NewGuid && asset.NewContent == null)
					{
						continue;
					}
					plan.Assets.Add(asset);
					if (asset.OldGuid != asset.NewGuid)
					{
						plan.Guids[asset.OldGuid] = asset.NewGuid;
					}
					if (ids != null && ids.Count > 0)
					{
						plan.FileIds[asset.OldGuid] = ids;
					}
				}
			}
			return plan;
		}

		/// <summary>The object IDs inside one generated file that change; fills the asset's new content when they do.</summary>
		private static Dictionary<long, long> PlanFileIds(string rel, string file, Asset asset, List<string> problems)
		{
			string ext = Path.GetExtension(rel).ToLowerInvariant();
			if (ext == ".prefab")
			{
				string text = File.ReadAllText(file);
				if (!ProceduralArtFileIds.TryRewrite(rel, text, out string rewritten, out Dictionary<long, long> remap, out string error))
				{
					problems.Add($"{rel}: its object IDs cannot be made deterministic ({error}); only its GUID is migrated");
					return null;
				}
				var changed = new Dictionary<long, long>();
				foreach (KeyValuePair<long, long> pair in remap)
				{
					if (pair.Key != pair.Value)
					{
						changed[pair.Key] = pair.Value;
					}
				}
				if (!string.Equals(rewritten, text.Replace("\r\n", "\n"), StringComparison.Ordinal))
				{
					asset.NewContent = Encoding.UTF8.GetBytes(rewritten);
				}
				return changed;
			}
			long expected = ext == ".mat" ? ProceduralArtPayload.MaterialFileId
				: ext == ".terrainlayer" ? ProceduralArtPayload.TerrainLayerFileId
				: 0;
			if (expected == 0)
			{
				return null;
			}
			long actual = ProceduralArtPayload.MainFileIdOf(file);
			if (actual == 0 || actual == expected)
			{
				return null;
			}
			string native = File.ReadAllText(file);
			string old = actual.ToString(CultureInfo.InvariantCulture), now = expected.ToString(CultureInfo.InvariantCulture);
			native = native.Replace("&" + old + "\n", "&" + now + "\n").Replace("{fileID: " + old + "}", "{fileID: " + now + "}");
			asset.NewContent = Encoding.UTF8.GetBytes(native);
			asset.OldMainFileId = actual;
			asset.NewMainFileId = expected;
			return new Dictionary<long, long> { [actual] = expected };
		}

		// ── Rewriting (pure) ──────────────────────────────────────────

		/// <summary>
		/// Rewrites text: <c>fileID: N, guid: G</c> pairs whose old GUID has an ID remap, then every
		/// 32-digit GUID token that is an old GUID. Returns the text unchanged (same instance) when
		/// nothing matched.
		/// </summary>
		public static string RemapText(string text, Plan plan, out int replaced)
		{
			int count = 0;
			if (plan.FileIds.Count > 0)
			{
				text = GuidPair.Replace(text, m =>
				{
					string guid = m.Groups[2].Value.ToLowerInvariant();
					if (plan.FileIds.TryGetValue(guid, out Dictionary<long, long> ids)
						&& long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
						&& ids.TryGetValue(id, out long now))
					{
						count++;
						return "fileID: " + now.ToString(CultureInfo.InvariantCulture) + ", guid: " + m.Groups[2].Value;
					}
					return m.Value;
				});
			}
			if (plan.Guids.Count > 0)
			{
				text = HexToken.Replace(text, m =>
				{
					if (plan.Guids.TryGetValue(m.Value.ToLowerInvariant(), out string now))
					{
						count++;
						return now;
					}
					return m.Value;
				});
			}
			replaced = count;
			return text;
		}

		/// <summary>
		/// Rewrites a binary-serialized file: every old GUID's 16 bytes, and — only in a file that names
		/// that GUID — each of its remapped object IDs as a little-endian int64, when the old ID is at
		/// least 2^32 in size (small IDs could match unrelated bytes). Returns null when nothing matched.
		/// </summary>
		/// <remarks>
		/// One pass per pattern length with a hash lookup at every offset, not one search per GUID:
		/// a terrain tile is megabytes and a plan holds a couple of hundred GUIDs.
		/// </remarks>
		public static byte[] RemapBinary(byte[] data, Plan plan, out int replaced)
		{
			replaced = 0;
			// Every GUID the plan knows — re-addressed or only renumbered — by its two 8-byte halves.
			var guids = new Dictionary<(ulong, ulong), string>();
			foreach (string old in plan.Guids.Keys)
			{
				guids[Halves(BinaryGuid(old))] = old;
			}
			foreach (string old in plan.FileIds.Keys)
			{
				guids[Halves(BinaryGuid(old))] = old;
			}
			if (guids.Count == 0 || data.Length < 16)
			{
				return null;
			}
			var firstHalves = new HashSet<ulong>();
			foreach ((ulong, ulong) key in guids.Keys)
			{
				firstHalves.Add(key.Item1);
			}

			byte[] result = null;
			var named = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i + 16 <= data.Length; i++)
			{
				ulong lo = ReadUInt64(data, i);
				if (!firstHalves.Contains(lo) || !guids.TryGetValue((lo, ReadUInt64(data, i + 8)), out string old))
				{
					continue;
				}
				named.Add(old);
				if (plan.Guids.TryGetValue(old, out string now))
				{
					result ??= (byte[])data.Clone();
					Buffer.BlockCopy(BinaryGuid(now), 0, result, i, 16);
					replaced++;
				}
				i += 15;
			}

			var ids = new Dictionary<ulong, long>();
			foreach (string old in named)
			{
				if (!plan.FileIds.TryGetValue(old, out Dictionary<long, long> map))
				{
					continue;
				}
				foreach (KeyValuePair<long, long> id in map)
				{
					if (id.Key >= 1L << 32 || id.Key <= -(1L << 32))
					{
						ids[unchecked((ulong)id.Key)] = id.Value;
					}
				}
			}
			if (ids.Count > 0)
			{
				byte[] source = result ?? data;
				for (int i = 0; i + 8 <= source.Length; i++)
				{
					if (!ids.TryGetValue(ReadUInt64(source, i), out long now))
					{
						continue;
					}
					result ??= (byte[])data.Clone();
					Buffer.BlockCopy(LittleEndian(now), 0, result, i, 8);
					replaced++;
					i += 7;
				}
			}
			return result;
		}

		private static (ulong, ulong) Halves(byte[] guid) => (ReadUInt64(guid, 0), ReadUInt64(guid, 8));

		/// <summary>Eight bytes as a little-endian unsigned integer, whatever the machine's own order.</summary>
		private static ulong ReadUInt64(byte[] data, int offset)
		{
			ulong value = 0;
			for (int k = 7; k >= 0; k--)
			{
				value = (value << 8) | data[offset + k];
			}
			return value;
		}

		/// <summary>
		/// A GUID as Unity's binary serialization stores it: 16 bytes, byte k holding hex digit 2k in its
		/// LOW nibble and digit 2k+1 in its high one (the text form prints each byte low nibble first).
		/// </summary>
		public static byte[] BinaryGuid(string hex)
		{
			var bytes = new byte[16];
			for (int k = 0; k < 16; k++)
			{
				bytes[k] = (byte)((Nibble(hex[2 * k + 1]) << 4) | Nibble(hex[2 * k]));
			}
			return bytes;
		}

		private static int Nibble(char c) => c <= '9' ? c - '0' : (char.ToLowerInvariant(c) - 'a' + 10);

		private static byte[] LittleEndian(long value)
		{
			byte[] bytes = BitConverter.GetBytes(value);
			if (!BitConverter.IsLittleEndian)
			{
				Array.Reverse(bytes);
			}
			return bytes;
		}

		/// <summary>A re-addressed asset's .meta: the new GUID, and the new main object ID when that changed too.</summary>
		public static string RemapMeta(string meta, Asset asset)
		{
			meta = MetaGuid.Replace(meta, "guid: " + asset.NewGuid, 1);
			if (asset.NewMainFileId != 0)
			{
				meta = MetaMainId.Replace(meta, m => m.Groups[1].Value + asset.NewMainFileId.ToString(CultureInfo.InvariantCulture), 1);
			}
			return meta;
		}

		// ── Applying on disk ──────────────────────────────────────────

		/// <summary>One file's new bytes.</summary>
		public sealed class Edit
		{
			public string Path;
			public byte[] Bytes;
			/// <summary>True for a re-addressed generated asset or its .meta: deleted and recreated rather than edited in place.</summary>
			public bool Readdressed;
			public string Note;
		}

		/// <summary>
		/// Every file the plan changes, with its new bytes, read from disk under
		/// <paramref name="projectRoot"/>. Files under Assets/LOCAL that name an old GUID are listed in
		/// <paramref name="localReferences"/> and not edited.
		/// </summary>
		public static List<Edit> ComputeEdits(Plan plan, string projectRoot, List<string> localReferences)
		{
			var edits = new List<Edit>();
			if (plan.Empty)
			{
				return edits;
			}
			var readdressed = new HashSet<string>(StringComparer.Ordinal);
			foreach (Asset asset in plan.Assets)
			{
				string file = Combine(projectRoot, asset.Path);
				byte[] bytes = asset.NewContent ?? File.ReadAllBytes(file);
				byte[] content = RemapFile(asset.Path, bytes, plan, out int refs) ?? bytes;
				bool guidChanged = asset.OldGuid != asset.NewGuid;
				edits.Add(new Edit
				{
					Path = asset.Path,
					Bytes = content,
					Readdressed = guidChanged,
					Note = (guidChanged ? $"re-addressed {asset.Path}: {asset.OldGuid} → {asset.NewGuid}" : $"renumbered {asset.Path}")
						+ (asset.NewContent != null ? " (object IDs made deterministic)" : string.Empty) + (refs > 0 ? $", {refs} reference(s) inside rewritten" : string.Empty),
				});
				string meta = File.ReadAllText(file + ".meta");
				edits.Add(new Edit { Path = asset.Path + ".meta", Bytes = Encoding.UTF8.GetBytes(RemapMeta(meta, asset)), Readdressed = guidChanged, Note = null });
				readdressed.Add(asset.Path);
				readdressed.Add(asset.Path + ".meta");
			}

			var pending = new Stack<string>();
			pending.Push(Combine(projectRoot, "Assets"));
			while (pending.Count > 0)
			{
				string folder = pending.Pop();
				foreach (string sub in Directory.GetDirectories(folder))
				{
					string rel = Relative(projectRoot, sub);
					if (ProceduralArtPayload.IsPayload(rel + "/") && !BiomeLocalArtIndex.IsLocalPath(rel))
					{
						continue;
					}
					pending.Push(sub);
				}
				foreach (string file in Directory.GetFiles(folder))
				{
					string rel = Relative(projectRoot, file);
					if (readdressed.Contains(rel) || NeverReferences.Contains(Path.GetExtension(rel)) || ProceduralArtPayload.IsPayload(rel))
					{
						continue;
					}
					byte[] bytes;
					try
					{
						bytes = File.ReadAllBytes(file);
					}
					catch (IOException)
					{
						continue;
					}
					byte[] content = RemapFile(rel, bytes, plan, out int refs);
					if (content == null)
					{
						continue;
					}
					if (BiomeLocalArtIndex.IsLocalPath(rel))
					{
						localReferences?.Add($"{rel} names {refs} old ID(s) of generated assets; repoint it by hand (Assets/LOCAL is never rewritten)");
						continue;
					}
					edits.Add(new Edit { Path = rel, Bytes = content, Note = $"rewrote {rel}: {refs} reference(s)" });
				}
			}
			return edits;
		}

		/// <summary>One file's remapped bytes, as text or (for a binary Unity file) as bytes; null when nothing in it changes.</summary>
		private static byte[] RemapFile(string path, byte[] bytes, Plan plan, out int replaced)
		{
			replaced = 0;
			if (IsBinary(bytes))
			{
				return SerializedExtensions.Contains(Path.GetExtension(path)) ? RemapBinary(bytes, plan, out replaced) : null;
			}
			string text = Encoding.UTF8.GetString(bytes);
			string remapped = RemapText(text, plan, out replaced);
			return replaced > 0 ? Encoding.UTF8.GetBytes(remapped) : null;
		}

		private static bool IsBinary(byte[] bytes)
		{
			int probe = Math.Min(bytes.Length, 8192);
			for (int i = 0; i < probe; i++)
			{
				if (bytes[i] == 0)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Copies every file an edit will touch to the backup folder, keeping its project path below it.</summary>
		public static void Backup(IEnumerable<Edit> edits, string projectRoot, string backupFolder)
		{
			foreach (Edit edit in edits)
			{
				string source = Combine(projectRoot, edit.Path);
				if (!File.Exists(source))
				{
					continue;
				}
				string target = Path.Combine(backupFolder, edit.Path);
				Directory.CreateDirectory(Path.GetDirectoryName(target));
				File.Copy(source, target, true);
			}
		}

		/// <summary>Writes every edit to disk. No asset-database calls: the editor sequence wraps this.</summary>
		public static void Write(IEnumerable<Edit> edits, string projectRoot)
		{
			foreach (Edit edit in edits)
			{
				string target = Combine(projectRoot, edit.Path);
				Directory.CreateDirectory(Path.GetDirectoryName(target));
				File.WriteAllBytes(target, edit.Bytes);
			}
		}

		// ── The editor tool ───────────────────────────────────────────

		[DashboardTool(DashboardToolAttribute.Biomes, "Migrate generated art to deterministic IDs", Section = "Art", Order = 3,
			Tooltip = "One-time and idempotent: gives every generated terrain layer, material, prefab and built terrain layer made before 2026-10-02 its path-derived GUID (and prefabs deterministic object IDs), and rewrites every reference to them under Assets (scenes, terrain data, biomes; never Assets/LOCAL). Every rewritten file is backed up to Library/FishMMO/DeterminismMigrationBackup first. The generator also runs this by itself when it finds such files.",
			Confirm = "Re-address generated art made before deterministic IDs and rewrite every reference to it under Assets? Open affected scenes are closed and reopened; every rewritten file is backed up to Library/FishMMO/DeterminismMigrationBackup/<timestamp>/ first.")]
		public static void MigrateFromDashboard()
		{
			Result result = Migrate(true);
			if (result.Problems.Count > 0)
			{
				Debug.LogWarning("[Biome art] " + result);
			}
			else
			{
				Debug.Log("[Biome art] " + result);
			}
		}

		/// <summary>
		/// Migrates the project in place (see the class remarks for the sequence). With
		/// <paramref name="interactive"/>, an open affected scene with unsaved changes prompts to be saved;
		/// otherwise it makes the migration refuse, and nothing is changed.
		/// </summary>
		public static Result Migrate(bool interactive)
		{
			var result = new Result();
			Plan plan = BuildPlan();
			result.Plan = plan;
			result.Problems.AddRange(plan.Problems);
			if (plan.Empty)
			{
				return result;
			}

			// First pass: which files change, so loaded copies can be saved or closed before any is read for real.
			List<Edit> first = ComputeEdits(plan, null, null);
			var scenes = new HashSet<string>(StringComparer.Ordinal);
			foreach (Edit edit in first)
			{
				if (edit.Path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
				{
					scenes.Add(edit.Path);
				}
				else if (!edit.Path.EndsWith(".meta", StringComparison.Ordinal) && AssetDatabase.IsMainAssetAtPathLoaded(edit.Path))
				{
					UnityEngine.Object loaded = AssetDatabase.LoadMainAssetAtPath(edit.Path);
					if (loaded != null && EditorUtility.IsDirty(loaded))
					{
						// Persist the editor's unsaved state, so the rewrite starts from it rather than discarding it.
						AssetDatabase.SaveAssetIfDirty(loaded);
					}
				}
			}

			SceneSetup[] setup = null;
			if (OpenScenesAmong(scenes, out bool anyDirty))
			{
				if (anyDirty && interactive && !Application.isBatchMode)
				{
					EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
					OpenScenesAmong(scenes, out anyDirty);
				}
				if (anyDirty || AnyOpenSceneDirty())
				{
					result.Problems.Add("an open scene has unsaved changes; save it (or close it) and run 'Migrate generated art to deterministic IDs' again. Nothing was changed.");
					return result;
				}
				setup = EditorSceneManager.GetSceneManagerSetup();
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
				EditorUtility.UnloadUnusedAssetsImmediate();
			}

			try
			{
				// Second pass, from the files as they now are.
				List<Edit> edits = ComputeEdits(plan, null, result.LocalReferences);
				string backup = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)).Replace('\\', '/');
				Backup(edits, null, backup);
				result.BackupFolder = backup;

				// Re-addressed assets leave the asset database under their old GUID before coming back under the new one.
				foreach (Asset asset in plan.Assets)
				{
					if (asset.OldGuid != asset.NewGuid)
					{
						AssetDatabase.DeleteAsset(asset.Path);
					}
				}
				Write(edits, null);

				var imports = new SortedSet<string>(StringComparer.Ordinal);
				foreach (Edit edit in edits)
				{
					imports.Add(edit.Path.EndsWith(".meta", StringComparison.Ordinal) ? edit.Path.Substring(0, edit.Path.Length - 5) : edit.Path);
					if (edit.Note != null)
					{
						result.Lines.Add(edit.Note);
					}
				}
				AssetDatabase.StartAssetEditing();
				try
				{
					foreach (string path in imports)
					{
						if (File.Exists(path))
						{
							AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
						}
					}
				}
				finally
				{
					AssetDatabase.StopAssetEditing();
				}
				BiomeTerrainLayers.ClearCache();
				result.Applied = true;
			}
			catch (Exception e)
			{
				result.Problems.Add($"failed part-way: {e.Message}. Restore from {result.BackupFolder ?? "(no backup was made)"} with the editor closed.");
				Debug.LogException(e);
			}
			finally
			{
				if (setup != null && setup.Length > 0)
				{
					EditorSceneManager.RestoreSceneManagerSetup(setup);
				}
			}
			return result;
		}

		private static bool OpenScenesAmong(HashSet<string> paths, out bool anyDirty)
		{
			bool any = false;
			anyDirty = false;
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (scene.isLoaded && paths.Contains(scene.path))
				{
					any = true;
					anyDirty |= scene.isDirty;
				}
			}
			return any;
		}

		/// <summary>Closing the scenes replaces EVERY open one, so none may hold unsaved work, affected or not.</summary>
		private static bool AnyOpenSceneDirty()
		{
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				if (SceneManager.GetSceneAt(i).isDirty)
				{
					return true;
				}
			}
			return false;
		}

		// ── Paths ─────────────────────────────────────────────────────

		private static string Combine(string projectRoot, string rel) => string.IsNullOrEmpty(projectRoot) ? rel : Path.Combine(projectRoot, rel);

		private static string Relative(string projectRoot, string file)
		{
			string path = file.Replace('\\', '/');
			if (!string.IsNullOrEmpty(projectRoot))
			{
				string root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
				if (path.StartsWith(root, StringComparison.Ordinal))
				{
					path = path.Substring(root.Length);
				}
			}
			return path;
		}

		private static string MetaGuidOf(string meta)
		{
			if (!File.Exists(meta))
			{
				return null;
			}
			Match m = MetaGuid.Match(File.ReadAllText(meta));
			return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
		}
	}
}
#endif
