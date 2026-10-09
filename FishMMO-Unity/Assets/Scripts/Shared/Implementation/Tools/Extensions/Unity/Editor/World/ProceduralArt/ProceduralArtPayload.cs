#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Every generated biome asset — the procedural textures and meshes (the payload), and the
	/// terrain layers, materials and prefabs built on them (the wrappers) — as files whose GUIDs are a
	/// function of their paths, so anything committed that references them resolves on any machine
	/// once they have been generated there.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Nothing generated is committed (Jim, 2026-10-02).</b> Thirty-odd 1024² ground families in
	/// three maps, bark, the foliage atlas and a billboard per tree come to a few hundred megabytes of
	/// PNG, every byte of which a deterministic function reproduces from the seed, and the wrappers are
	/// as reproducible as the payload. So all of it lives under <see cref="ProceduralArtCatalogue.Root"/>
	/// (and the layers built from loose biome textures under <see cref="BiomeTerrainLayers.BuiltFolder"/>),
	/// gitignored with their .meta files, and each machine generates its own — on editor load
	/// (<see cref="ProceduralArtAutoGenerate"/>) and before every build.
	/// </para>
	/// <para>
	/// <b>Why deterministic GUIDs.</b> Committed scenes, terrain data and biome templates point at
	/// generated assets by GUID and file ID. A file Unity imports for the first time gets a random
	/// GUID, so a clone's regenerated assets would have different GUIDs from the ones its committed
	/// assets name, and every reference would be broken. Writing the .meta, with <see cref="GuidFor"/>,
	/// BEFORE the file is imported pins the GUID; Unity fills in the rest of the importer settings
	/// around it. (This is deliberately unlike the world maps, where deterministic GUIDs were rejected:
	/// nothing committed references a map, so automation alone was enough there.)
	/// </para>
	/// <para>
	/// <b>File IDs.</b> A GUID names the file; the file ID names the object inside it, and it must be
	/// as stable. A texture imported from a PNG is always <see cref="TextureFileId"/>; a native asset
	/// saved alone is numbered by Unity from its class — <see cref="MeshFileId"/>, <see cref="MaterialFileId"/>
	/// and <see cref="TerrainLayerFileId"/> (the last read from Unity's own .terrainlayer files, not
	/// computed: TerrainLayer's class ID is a hash, and Unity gives every one the same fixed ID).
	/// <see cref="CreateNative"/> checks the ID Unity actually wrote. A prefab's objects get random file
	/// IDs when it is saved, so the generator rewrites them afterwards from each object's place in the
	/// hierarchy (<see cref="ProceduralArtFileIds"/>) and writes the result with <see cref="WriteFile"/>.
	/// </para>
	/// <para>
	/// <b>Generated assets belong to the generator outright.</b> None is ever hand-edited — real art
	/// goes in <c>Assets/LOCAL</c> by the LOCAL override contract, or a biome slot or scatter rule is
	/// pointed at a non-generated asset — so each is rewritten whenever it is stale, but only where the
	/// bytes actually change, so a regeneration that produces the same file does not reimport it.
	/// </para>
	/// </remarks>
	public static class ProceduralArtPayload
	{
		/// <summary>The main object of a texture imported from an image file.</summary>
		public const long TextureFileId = 2800000;

		/// <summary>The main object of a .asset holding one Mesh (class 43).</summary>
		public const long MeshFileId = 4300000;

		/// <summary>The main object of a .mat (class 21).</summary>
		public const long MaterialFileId = 2100000;

		/// <summary>
		/// The main object of a .terrainlayer. Not class × 100000: TerrainLayer's class ID (1953259897)
		/// is a hash, and Unity writes this one fixed ID for every layer it creates — read from the
		/// project's own layers (all 102 on 2026-10-02 carried it), and checked on every create.
		/// </summary>
		public const long TerrainLayerFileId = 8574412962073106934;

		/// <summary>
		/// The main object of a .shadervariants (class 200 × 100000): what Unity wrote in the one it
		/// created for a package (com.unity.dt.app-ui's App UI Shaders.shadervariants, object and .meta
		/// mainObjectFileID alike). <see cref="ProceduralArtVariants"/> writes the file itself and checks
		/// the ID Unity reads back after importing it.
		/// </summary>
		public const long ShaderVariantCollectionFileId = 20000000;

		/// <summary>
		/// Raised by hand when the generator's OUTPUT changes in a way the source hash cannot see
		/// (a Unity upgrade that encodes PNGs differently, say). Every machine then regenerates.
		/// </summary>
		public const int FormatVersion = 4;

		/// <summary>The generator's own record: what generated it, and the files it wrote. Gitignored with everything else generated.</summary>
		public const string LedgerPath = ProceduralArtCatalogue.PayloadRoot + "/PayloadLedger.json";

		/// <summary>Where a native asset is first saved before being copied to its path with a pinned GUID.</summary>
		public const string StagingFolder = ProceduralArtCatalogue.PayloadRoot + "/_Staging";

		/// <summary>
		/// The generator sources whose text decides what is generated — payload and wrappers alike. A
		/// change to any of them makes every machine's generated art stale. The authoring pass, the
		/// ledger and the import settings are left out on purpose: none changes a single generated byte
		/// (import settings reimport on their own through the postprocessor's version). The spec table
		/// was too until 2026-10-02, when the detail materials began to carry its scatter rules' ground
		/// sink (BiomeArtGenerator.DetailSinkRange); a spec edit now regenerates, which rewrites only the
		/// files whose bytes change.
		/// </summary>
		public static readonly string[] SourceFiles =
		{
			"BiomeArtGenerator.cs",
			"BiomeArtSpec.cs",
			"BillboardImpostor.cs",
			"FoliageAtlas.cs",
			"MeshBuilder.cs",
			"PlantParts.cs",
			"ProceduralArtCatalogue.cs",
			"ProceduralArtFileIds.cs",
			"ProceduralNoise.cs",
			"RockMeshes.cs",
			"SurfaceCatalogue.cs",
			"SurfaceSynth.cs",
			"TreeMeshes.cs",
			"VegetationMeshes.cs",
			"SeaFloorMeshes.cs",
			// Shrubs.
			"BiomeArtGenerator.Bushes.cs",
			"BushMeshes.cs",
			"ProceduralArtCatalogue.Bushes.cs",
			// Rock formations, ice, cliff rocks and cliff sections.
			"BiomeArtGenerator.Rocks.cs",
			"CliffRocks.cs",
			"CliffSections.cs",
			"IceMeshes.cs",
			"IceSurfaces.cs",
			"MeshDecimator.cs",
			"ProceduralArtCatalogue.Rocks.cs",
			"ProceduralSurfaceNets.cs",
			"RockArtNames.cs",
			"RockFormations.cs",
			"RockTypes.cs",
		};

		private static readonly Regex MetaGuid = new Regex(@"^guid:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

		// ── Paths and GUIDs ───────────────────────────────────────────

		/// <summary>True for a path under the gitignored payload folder.</summary>
		public static bool IsPayload(string path)
		{
			return !string.IsNullOrEmpty(path) && Normalise(path).StartsWith(ProceduralArtCatalogue.PayloadRoot + "/", StringComparison.Ordinal);
		}

		/// <summary>
		/// The folders everything generated lives in, all gitignored: the procedural art (payload and
		/// wrappers) and the terrain layers built from loose biome textures.
		/// </summary>
		public static readonly string[] GeneratedRoots = { ProceduralArtCatalogue.Root, BiomeTerrainLayers.BuiltFolder };

		/// <summary>
		/// True for a generated file (or one of the generated folders themselves): build output with a
		/// path-derived GUID, never committed, never "authored", rewritten by the generator at will.
		/// </summary>
		public static bool IsGenerated(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return false;
			}
			path = Normalise(path);
			foreach (string root in GeneratedRoots)
			{
				if (path == root || path == root + ".meta" || path.StartsWith(root + "/", StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The GUID a generated file has on every machine: the first 128 bits of the MD5 of its
		/// project path (forward slashes, case kept), as 32 lowercase hex digits. One function for every
		/// generated asset — payload, wrappers and built layers.
		/// </summary>
		/// <remarks>
		/// MD5 is not here for security; it is a fixed, well-distributed 128-bit function that every
		/// .NET runtime computes identically. The salt keeps these GUIDs apart from any other scheme
		/// that might hash the same path. It still says "Payload" because the payload was addressed this
		/// way first, and changing it would re-address every file.
		/// </remarks>
		public static string GuidFor(string path)
		{
			using (MD5 md5 = MD5.Create())
			{
				byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("FishMMO.ProceduralArtPayload:" + Normalise(path)));
				var sb = new StringBuilder(32);
				foreach (byte b in hash)
				{
					sb.Append(b.ToString("x2"));
				}
				return sb.ToString();
			}
		}

		/// <summary>The GUID a .meta file on disk declares, or null when there is no readable one.</summary>
		public static string GuidOnDisk(string path)
		{
			string meta = path + ".meta";
			if (!File.Exists(meta))
			{
				return null;
			}
			Match m = MetaGuid.Match(File.ReadAllText(meta));
			return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
		}

		/// <summary>True when a file and its .meta exist and the .meta carries the file's deterministic GUID.</summary>
		public static bool HasExpectedGuid(string path)
		{
			return File.Exists(path) && string.Equals(GuidOnDisk(path), GuidFor(path), StringComparison.Ordinal);
		}

		private static string Normalise(string path) => path.Replace('\\', '/');

		// ── Writing ───────────────────────────────────────────────────

		/// <summary>
		/// Gets a path ready to hold a generated file with its deterministic GUID. A file already there
		/// under any other GUID is deleted first (a GUID cannot be changed in place without Unity
		/// seeing a different asset), and the .meta is then written with the right GUID before the
		/// file exists, so whichever import comes first — ours or an automatic refresh — keeps it.
		/// </summary>
		/// <param name="nativeMainFileId">For a native .asset, its main object's file ID; 0 for an image.</param>
		/// <param name="metaChanged">True when the .meta had to be (re)written, which needs an import even if the bytes do not change.</param>
		/// <returns>False when the GUID belongs to another file: that file is left alone and the generated file is not written.</returns>
		public static bool Prepare(string path, long nativeMainFileId, List<string> problems, out bool metaChanged)
		{
			return Prepare(path, guid => MetaText(guid, nativeMainFileId), problems, out metaChanged);
		}

		/// <summary><see cref="Prepare(string, long, List{string}, out bool)"/> with the .meta's text chosen by the caller (a prefab's importer, say).</summary>
		public static bool Prepare(string path, Func<string, string> metaText, List<string> problems, out bool metaChanged)
		{
			metaChanged = false;
			path = Normalise(path);
			string guid = GuidFor(path);

			// Somebody copied or moved a generated file: its GUID now names another path.
			string owner = AssetDatabase.GUIDToAssetPath(guid);
			if (!string.IsNullOrEmpty(owner) && !string.Equals(owner, path, StringComparison.Ordinal) && File.Exists(owner))
			{
				problems?.Add($"{path}: its GUID {guid} is taken by '{owner}' (a copy of a generated file?). Delete that file and generate again.");
				return false;
			}

			if (File.Exists(path) && !string.Equals(GuidOnDisk(path), guid, StringComparison.Ordinal))
			{
				Delete(path);
			}

			if (!string.Equals(GuidOnDisk(path), guid, StringComparison.Ordinal))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path + ".meta", metaText(guid));
				metaChanged = true;
			}
			return true;
		}

		/// <summary>
		/// The .meta written before a payload file's first import: the GUID, the generated-art label,
		/// and for a native asset its importer with the main object ID. Unity completes the importer
		/// settings on import; the texture settings themselves — the shape above all, which a bare
		/// .meta would otherwise get wrong — are applied on every import by <see cref="ProceduralArtTextureImport"/>.
		/// </summary>
		/// <remarks>
		/// The label is written here rather than with <c>AssetDatabase.SetLabels</c> after the import:
		/// SetLabels rewrites the .meta, which is one more asset-database refresh per file, and a full
		/// generation writes some two thousand files.
		/// </remarks>
		public static string MetaText(string guid, long nativeMainFileId)
		{
			var sb = new StringBuilder();
			sb.Append("fileFormatVersion: 2\n");
			sb.Append("guid: ").Append(guid).Append('\n');
			sb.Append(LabelsText);
			if (nativeMainFileId != 0)
			{
				sb.Append("NativeFormatImporter:\n");
				sb.Append("  externalObjects: {}\n");
				sb.Append("  mainObjectFileID: ").Append(nativeMainFileId).Append('\n');
				sb.Append("  userData: \n");
				sb.Append("  assetBundleName: \n");
				sb.Append("  assetBundleVariant: \n");
			}
			return sb.ToString();
		}

		/// <summary>The .meta lines that give a generated asset its label, as Unity writes them.</summary>
		public const string LabelsText = "labels:\n- " + ProceduralArtLedger.Label + "\n";

		/// <summary>The .meta written before a generated prefab's first import: the GUID, the label and the prefab importer.</summary>
		public static string PrefabMetaText(string guid)
		{
			return "fileFormatVersion: 2\n" +
				"guid: " + guid + "\n" +
				LabelsText +
				"PrefabImporter:\n" +
				"  externalObjects: {}\n" +
				"  userData: \n" +
				"  assetBundleName: \n" +
				"  assetBundleVariant: \n";
		}

		/// <summary>
		/// Writes a payload image's bytes when they differ from what is on disk. Returns true when the
		/// file must be imported: the bytes changed, or the .meta was just written.
		/// </summary>
		public static bool WriteBytes(string path, byte[] bytes, List<string> problems)
		{
			return WriteFile(path, bytes, guid => MetaText(guid, 0), problems);
		}

		/// <summary>
		/// Writes any generated file's bytes, beside a .meta of the caller's choosing carrying the
		/// deterministic GUID, when they differ from what is on disk. Returns true when the file must
		/// be imported: the bytes changed, or the .meta was just written. Used for prefabs, whose
		/// bytes are produced by <see cref="ProceduralArtFileIds.TryRewrite"/> rather than by Unity.
		/// </summary>
		public static bool WriteFile(string path, byte[] bytes, Func<string, string> metaText, List<string> problems)
		{
			if (!Prepare(path, metaText, problems, out bool metaChanged))
			{
				return false;
			}
			if (File.Exists(path) && SameBytes(path, bytes))
			{
				return metaChanged;
			}
			File.WriteAllBytes(path, bytes);
			return true;
		}

		private static bool SameBytes(string path, byte[] bytes)
		{
			var info = new FileInfo(path);
			if (info.Length != bytes.LongLength)
			{
				return false;
			}
			byte[] existing = File.ReadAllBytes(path);
			for (int i = 0; i < existing.Length; i++)
			{
				if (existing[i] != bytes[i])
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Saves a new native object (a Mesh, Material or TerrainLayer) as a generated asset with its
		/// deterministic GUID, and returns the asset loaded from its path. <paramref name="obj"/> is consumed.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>AssetDatabase.CreateAsset</c> always picks a random GUID, so the object is created in
		/// <see cref="StagingFolder"/> first, its file copied to the real path beside a .meta carrying
		/// the deterministic GUID, and the staged asset deleted. The serialized file holds no path or
		/// GUID of its own — its objects are numbered locally — so the copy is the same asset.
		/// </para>
		/// <para>
		/// The main object's file ID is what Unity wrote into the staged file, and it is checked against
		/// <paramref name="mainFileId"/>: a mismatch would mean Unity numbers that class differently on
		/// this version, and every reference to the asset from a committed file would break on the
		/// next machine — so it is reported as a problem rather than silently used.
		/// </para>
		/// </remarks>
		public static T CreateNative<T>(T obj, string path, long mainFileId, List<string> problems) where T : Object
		{
			path = Normalise(path);
			if (!Prepare(path, mainFileId, problems, out _))
			{
				Object.DestroyImmediate(obj);
				return null;
			}
			WorldEditorAssets.EnsureFolder(StagingFolder);
			string staged = $"{StagingFolder}/{Path.GetFileName(path)}";
			if (File.Exists(staged))
			{
				Delete(staged);
			}
			AssetDatabase.CreateAsset(obj, staged);
			// Only this asset: SaveAssets would also write whatever else the person has unsaved.
			AssetDatabase.SaveAssetIfDirty(obj);
			bool copied = CopyStaged(staged, path, mainFileId, problems);
			Delete(staged);
			if (!copied)
			{
				return null;
			}
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
			T loaded = AssetDatabase.LoadAssetAtPath<T>(path);
			if (loaded == null)
			{
				problems?.Add($"{path}: written but not loadable after import");
			}
			return loaded;
		}

		/// <summary>
		/// Saves a prefab built outside the generator's own pass (the cliff placer's rocks) the way the generator saves
		/// its own: by Unity to the staging folder, its object IDs rewritten from its hierarchy
		/// (<see cref="ProceduralArtFileIds"/>), then written beside a .meta carrying its path's GUID and imported. So a
		/// scene's reference to it is the same on every machine that generates the art. Returns the prefab, or null
		/// with a reason in <paramref name="problems"/>.
		/// </summary>
		public static GameObject SavePrefab(GameObject root, string path, List<string> problems)
		{
			path = Normalise(path);
			WorldEditorAssets.EnsureFolder(StagingFolder);
			string staged = $"{StagingFolder}/{Path.GetFileName(path)}";
			if (File.Exists(staged))
			{
				Delete(staged);
			}
			PrefabUtility.SaveAsPrefabAsset(root, staged, out bool saved);
			if (!saved || !File.Exists(staged))
			{
				problems?.Add($"{path}: Unity could not save it");
				return null;
			}
			string yaml = File.ReadAllText(staged);
			Delete(staged);
			if (!ProceduralArtFileIds.TryRewrite(path, yaml, out string rewritten, out _, out string error))
			{
				problems?.Add($"{path}: its object IDs could not be made deterministic ({error}); not written");
				return null;
			}
			if (WriteFile(path, Encoding.UTF8.GetBytes(rewritten), PrefabMetaText, problems))
			{
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
			}
			return AssetDatabase.LoadAssetAtPath<GameObject>(path);
		}

		/// <summary>
		/// The second half of creating a native generated asset: the staged file (saved by Unity with a
		/// random GUID) copied to its real path beside the .meta carrying the path's GUID, after its
		/// main object ID is checked against <paramref name="mainFileId"/>. Does not import the copy or
		/// delete the staged file — the caller batches both (see BiomeArtGenerator's flush).
		/// </summary>
		/// <returns>False when the staged file is not on disk.</returns>
		public static bool CopyStaged(string staged, string path, long mainFileId, List<string> problems)
		{
			path = Normalise(path);
			if (!File.Exists(staged))
			{
				problems?.Add($"{path}: Unity did not save it (no staged file at {staged})");
				return false;
			}
			long written = MainFileIdOf(staged);
			if (written != 0 && written != mainFileId)
			{
				problems?.Add($"{path}: Unity numbered its main object {written}, not the expected {mainFileId}; references to it will differ between machines. Update the constant in ProceduralArtPayload.");
				mainFileId = written;
			}
			File.Copy(staged, path, true);
			// Prepare wrote the .meta; the copy may have raced an automatic refresh that made one of its own.
			if (!string.Equals(GuidOnDisk(path), GuidFor(path), StringComparison.Ordinal))
			{
				File.WriteAllText(path + ".meta", MetaText(GuidFor(path), mainFileId));
			}
			return true;
		}

		private static readonly Regex FirstObject = new Regex(@"^--- !u!\d+ &(-?\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

		/// <summary>The file ID of the first object in a text-serialized native asset, or 0.</summary>
		public static long MainFileIdOf(string path)
		{
			if (!File.Exists(path))
			{
				return 0;
			}
			string text;
			using (var reader = new StreamReader(path))
			{
				char[] head = new char[512];
				int n = reader.Read(head, 0, head.Length);
				text = new string(head, 0, n);
			}
			Match m = FirstObject.Match(text);
			return m.Success && long.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long id) ? id : 0;
		}

		/// <summary>
		/// Deletes an asset and its .meta: through the asset database, then from disk, because
		/// <c>DeleteAsset</c> reports success while the file is still there when the editor has not
		/// flushed the deletion yet (see WorldMapBaker.CleanBakedMaps).
		/// </summary>
		public static void Delete(string path)
		{
			AssetDatabase.DeleteAsset(path);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			if (File.Exists(path + ".meta"))
			{
				File.Delete(path + ".meta");
			}
		}

		/// <summary>Removes the staging folder once a generation is over.</summary>
		public static void ClearStaging()
		{
			if (AssetDatabase.IsValidFolder(StagingFolder))
			{
				AssetDatabase.DeleteAsset(StagingFolder);
			}
			if (Directory.Exists(StagingFolder))
			{
				Directory.Delete(StagingFolder, true);
			}
			if (File.Exists(StagingFolder + ".meta"))
			{
				File.Delete(StagingFolder + ".meta");
			}
		}

		// ── Staleness ─────────────────────────────────────────────────

		[Serializable]
		private sealed class LedgerDocument
		{
			public int version = 2;
			public string fingerprint;
			public int seed;
			public List<string> files = new List<string>();
			/// <summary>The layers <see cref="BiomeTerrainLayers"/> built from loose biome textures in that run.</summary>
			public List<string> built = new List<string>();
		}

		private static LedgerDocument ReadLedger()
		{
			if (!File.Exists(LedgerPath))
			{
				return null;
			}
			try
			{
				return JsonUtility.FromJson<LedgerDocument>(File.ReadAllText(LedgerPath));
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>The fingerprint and seed the generated art on disk was last completely generated with, or null.</summary>
		public static string RecordedFingerprint(out int seed)
		{
			seed = 0;
			LedgerDocument doc = ReadLedger();
			if (doc == null)
			{
				return null;
			}
			seed = doc.seed;
			return string.IsNullOrEmpty(doc.fingerprint) ? null : doc.fingerprint;
		}

		/// <summary>The built terrain layers the last complete generation recorded; empty when none.</summary>
		public static List<string> RecordedBuiltLayers()
		{
			LedgerDocument doc = ReadLedger();
			return doc?.built != null ? new List<string>(doc.built) : new List<string>();
		}

		/// <summary>Records a COMPLETE generation. Never called for a cancelled or failed one, so that stays stale.</summary>
		public static void RecordComplete(string fingerprint, int seed) => RecordComplete(fingerprint, seed, null);

		/// <summary>Records a COMPLETE generation, with the built terrain layers it ensured. Never called for a cancelled or failed one.</summary>
		public static void RecordComplete(string fingerprint, int seed, IEnumerable<string> builtLayers)
		{
			var doc = new LedgerDocument { fingerprint = fingerprint, seed = seed };
			doc.files.AddRange(ProceduralArtCatalogue.PayloadPaths());
			doc.files.AddRange(ProceduralArtCatalogue.WrapperPaths());
			doc.files.Sort(StringComparer.Ordinal);
			if (builtLayers != null)
			{
				doc.built.AddRange(new SortedSet<string>(builtLayers, StringComparer.Ordinal));
			}
			Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath));
			File.WriteAllText(LedgerPath, JsonUtility.ToJson(doc, true));
		}

		/// <summary>
		/// Replaces only the record's list of built terrain layers, keeping its fingerprint: a
		/// missing-only run ensures the built layers too, but vouches for nothing else.
		/// </summary>
		public static void RecordBuilt(IEnumerable<string> builtLayers)
		{
			LedgerDocument doc = ReadLedger() ?? new LedgerDocument();
			doc.built = new List<string>(new SortedSet<string>(builtLayers ?? Array.Empty<string>(), StringComparer.Ordinal));
			Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath));
			File.WriteAllText(LedgerPath, JsonUtility.ToJson(doc, true));
		}

		/// <summary>
		/// What the payload would be generated from now: the format version, the seed, the texture
		/// sizes and the text of every <see cref="SourceFiles"/> entry (line endings normalised, so a
		/// Windows checkout agrees with a Linux one).
		/// </summary>
		/// <remarks>
		/// A source hash rather than a version number somebody must remember to raise: any change to
		/// how a byte is generated reaches every machine at its next editor load. The cost is that a
		/// comment edit in those files also regenerates — which, the generator being deterministic,
		/// rewrites nothing (bytes are compared before writing) and only spends the time.
		/// </remarks>
		public static string CurrentFingerprint(int seed)
		{
			var sb = new StringBuilder();
			sb.Append("format=").Append(FormatVersion).Append(";seed=").Append(seed)
				.Append(";ground=").Append(ProceduralArtCatalogue.GroundSize)
				.Append(";bark=").Append(ProceduralArtCatalogue.BarkSize)
				.Append(";atlas=").Append(ProceduralArtCatalogue.AtlasSize)
				.Append(";billboard=").Append(ProceduralArtCatalogue.BillboardHeight).Append('\n');
			string folder = SourceFolder();
			foreach (string file in SourceFiles)
			{
				string path = folder != null ? Path.Combine(folder, file) : null;
				sb.Append(file).Append('\n');
				if (path != null && File.Exists(path))
				{
					sb.Append(File.ReadAllText(path).Replace("\r\n", "\n"));
				}
				else
				{
					sb.Append("<missing>");
				}
				sb.Append('\n');
			}
			using (SHA1 sha = SHA1.Create())
			{
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
				return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
			}
		}

		/// <summary>The folder the generator's sources live in, found by its own script so a move does not break the hash.</summary>
		public static string SourceFolder()
		{
			foreach (string guid in AssetDatabase.FindAssets("BiomeArtGenerator t:MonoScript"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (Path.GetFileName(path) == "BiomeArtGenerator.cs")
				{
					return Path.GetDirectoryName(path).Replace('\\', '/');
				}
			}
			return null;
		}

		/// <summary>What a look at the disk found.</summary>
		public sealed class State
		{
			/// <summary>True when anything must be generated or rewritten.</summary>
			public bool Stale => StaleArt || StaleVariants;
			/// <summary>True when art must be generated: everything <see cref="Stale"/> covers but the variant collection.</summary>
			public bool StaleArt => GeneratorChanged || NeedsMigration || MissingPayload.Count > 0 || WrongGuid.Count > 0 || MissingWrappers.Count > 0 || MissingBuilt.Count > 0 || UnboundWrappers.Count > 0;
			/// <summary>
			/// The indirect shaders' variant collection (<see cref="ProceduralArtVariants"/>) is missing or no
			/// longer matches the materials on disk. Needs no generation: <see cref="ProceduralArtVariants.Ensure()"/>
			/// rewrites it alone, and every generation ends by doing so.
			/// </summary>
			public bool StaleVariants;
			/// <summary>No complete generation is recorded, or it was made by other code, sizes or seed.</summary>
			public bool GeneratorChanged;
			/// <summary>True when there was no generation record at all: a fresh clone, or a wiped Generated folder.</summary>
			public bool NeverGenerated;
			public string Fingerprint;
			public readonly List<string> MissingPayload = new List<string>();
			/// <summary>Generated files on disk whose .meta carries another GUID than their path's.</summary>
			public readonly List<string> WrongGuid = new List<string>();
			public readonly List<string> MissingWrappers = new List<string>();
			/// <summary>Built terrain layers the last generation recorded that are no longer on disk.</summary>
			public readonly List<string> MissingBuilt = new List<string>();
			/// <summary>
			/// Generated materials and terrain layers on disk with an empty required texture slot
			/// (<see cref="ProceduralArtWrapperCheck"/>): written while a texture was unloadable. Only a
			/// full generation rewrites them, so they make the art stale and the next run a full one.
			/// </summary>
			public readonly List<string> UnboundWrappers = new List<string>();
			/// <summary>True when a full generation is needed rather than a missing-only one.</summary>
			public bool NeedsFull => GeneratorChanged || NeedsMigration || UnboundWrappers.Count > 0;
			/// <summary>
			/// True when wrappers or built layers made before generated IDs were deterministic are on
			/// disk: <see cref="ProceduralArtMigration"/> must re-address them (and every committed
			/// reference to them) before anything regenerates over them.
			/// </summary>
			public bool NeedsMigration;

			public override string ToString()
			{
				if (!Stale)
				{
					return "procedural art is current";
				}
				var parts = new List<string>();
				if (NeverGenerated) parts.Add("no procedural art has been generated on this machine");
				else if (GeneratorChanged) parts.Add("the generator changed since the art was made");
				if (NeedsMigration) parts.Add("generated assets with random GUIDs from before deterministic IDs are on disk (they are migrated first)");
				if (MissingPayload.Count > 0) parts.Add($"{MissingPayload.Count} payload file(s) missing (first: {MissingPayload[0]})");
				if (WrongGuid.Count > 0) parts.Add($"{WrongGuid.Count} generated file(s) with the wrong GUID (first: {WrongGuid[0]})");
				if (MissingWrappers.Count > 0) parts.Add($"{MissingWrappers.Count} wrapper(s) missing (first: {MissingWrappers[0]})");
				if (MissingBuilt.Count > 0) parts.Add($"{MissingBuilt.Count} built terrain layer(s) missing (first: {MissingBuilt[0]})");
				if (UnboundWrappers.Count > 0) parts.Add($"{UnboundWrappers.Count} generated material(s)/terrain layer(s) with an empty texture slot (first: {UnboundWrappers[0]})");
				if (StaleVariants) parts.Add($"the indirect shader variant collection {ProceduralArtVariants.AssetPath} is missing or out of date");
				return string.Join("; ", parts);
			}
		}

		/// <summary>
		/// Checks the payload, wrappers, recorded built layers and the indirect variant collection against
		/// what they should be without generating anything: one file-exists and one small .meta read per generated file, cheap
		/// enough for every script reload.
		/// </summary>
		public static State Check(int seed)
		{
			var state = new State { Fingerprint = CurrentFingerprint(seed) };
			string recorded = RecordedFingerprint(out int recordedSeed);
			state.NeverGenerated = recorded == null;
			state.GeneratorChanged = recorded == null || recordedSeed != seed || !string.Equals(recorded, state.Fingerprint, StringComparison.Ordinal);
			foreach (string path in ProceduralArtCatalogue.PayloadPaths())
			{
				if (!File.Exists(path))
				{
					state.MissingPayload.Add(path);
				}
				else if (!HasExpectedGuid(path))
				{
					state.WrongGuid.Add(path);
				}
			}
			foreach (string path in ProceduralArtCatalogue.WrapperPaths())
			{
				if (!File.Exists(path))
				{
					state.MissingWrappers.Add(path);
				}
				else if (!HasExpectedGuid(path))
				{
					state.WrongGuid.Add(path);
				}
			}
			foreach (string path in RecordedBuiltLayers())
			{
				if (!File.Exists(path))
				{
					state.MissingBuilt.Add(path);
				}
				else if (!HasExpectedGuid(path))
				{
					state.WrongGuid.Add(path);
				}
			}
			// A hundred-odd small text files: cheap enough for every reload, like the .meta reads above.
			state.UnboundWrappers.AddRange(ProceduralArtWrapperCheck.Scan(ProceduralArtCatalogue.WrapperPaths()));
			state.NeedsMigration = ProceduralArtMigration.NeedsMigration();
			// One material search and two shaders' pass tables: a new authored keyword set is caught here, on the next reload.
			state.StaleVariants = ProceduralArtVariants.IsStale();
			return state;
		}
	}
}
#endif
