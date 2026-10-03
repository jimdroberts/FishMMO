#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Brings the WorldEditor project's terrain photos into <c>Assets/LOCAL</c>, restores
	/// WorldEditor's own per-biome use of them as LOCAL overrides, and takes the six that were
	/// committed out of the repository.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> Six of WorldEditor's photos (Forest, Glacier, Grass, Ocean, Snow, Woodland) came
	/// over with the biome data and were committed under <c>Assets/Prefabs/Shared/Biomes/Textures</c>,
	/// referenced straight from biome slots. Their licence is unknown, so by the LOCAL rule they may
	/// not be committed: real art lives in the gitignored <c>Assets/LOCAL</c>, and reaches the ground
	/// through a biome's <see cref="BiomeLocalArt"/> sidecar, while the committed slot carries
	/// procedural art every clone can draw.
	/// </para>
	/// <para>
	/// <b>What it does, idempotently</b> — a second run changes nothing:
	/// </para>
	/// <list type="number">
	/// <item>Copies WorldEditor's fifteen photos into <see cref="LocalFolder"/>, skipping any already
	/// there. Each copy keeps WorldEditor's import settings but gets a GUID of its own: WorldEditor's
	/// GUIDs are the committed photos' GUIDs, and two files with one GUID make Unity give one of them a
	/// new one — possibly the one everything references.</item>
	/// <item>Copies the two generic photos under the procedural texture names they stand in for
	/// (<see cref="NameCopies"/>), so the terrain array baker's name rule picks them up everywhere that
	/// family is drawn.</item>
	/// <item>Writes the sidecar overrides of <see cref="Assignments"/> — WorldEditor's own slot
	/// assignments — as whole LOCAL terrain layers with WorldEditor's tiling, plus one for any other
	/// committed slot that still references a committed photo. A sidecar slot already set to something
	/// else is left alone and reported.</item>
	/// <item>Clears every committed slot field that references a committed photo, so
	/// <see cref="BiomeArtAuthoring"/> fills it with procedural art.</item>
	/// <item>Deletes the six committed photos — only once a text scan of every committed file finds no
	/// reference to any of them left.</item>
	/// </list>
	/// <para>
	/// <b>The table, and where it came from.</b> Read from WorldEditor's biome assets
	/// (<c>Assets/Prefabs/Heightmap/Biomes/BiomeTemplate_*.asset</c>, BiomeTemplate script GUID
	/// e4734f4d38d798545b1bb25062eb1aa0) on 2026-10-02 by resolving each slot's albedo GUID against
	/// the .meta files of <c>Assets/Prefabs/*.jpg</c>; the slot index is the item's position in its
	/// <c>detailTextureLayers</c> list. Every other WorldEditor slot holds a flat
	/// <c>BiomeTexture_*</c> swatch. Dirt and Riverbed appear in no biome (only WorldEditor's sample
	/// scene), hence the name copies. Metallic and smoothness were 0 in every photo slot.
	/// </para>
	/// </remarks>
	public static class WorldEditorPhotoImport
	{
		/// <summary>Where the photos are copied.</summary>
		public const string LocalFolder = BiomeLocalArt.TexturesFolder + "/WorldEditor";

		/// <summary>The LOCAL terrain layers built from them. A subfolder, so no name can match a committed layer's by accident.</summary>
		public const string LocalLayersFolder = BiomeLocalArt.TerrainLayersFolder + "/WorldEditor";

		/// <summary>Where the six committed photos were.</summary>
		public const string CommittedFolder = "Assets/Prefabs/Shared/Biomes/Textures";

		/// <summary>The committed photos this removes (file names without extension; all .jpg).</summary>
		public static readonly string[] CommittedPhotos = { "Forest", "Glacier", "Grass", "Ocean", "Snow", "Woodland" };

		/// <summary>WorldEditor's photos (file names without extension; all .jpg).</summary>
		public static readonly string[] Photos =
		{
			"Beach Sand", "Coastal Water", "Deep Ocean", "Dirt", "Forest 2", "Forest", "Glacier", "Grass 2",
			"Grass", "Ocean", "Riverbed", "Rocky Terrain", "Snow", "Woodland 2", "Woodland",
		};

		/// <summary>The WorldEditor project's photo folder, relative to this project's root, as the repositories sit side by side.</summary>
		public const string DefaultSourceRelative = "../../FishMMO-WorldBuilding/WorldEditor/Assets/Prefabs";

		private const string SourcePrefKey = "FishMMO.WorldEditorPhotoImport.Source";

		/// <summary>One of WorldEditor's slot assignments, on the FishMMO biome of the same name.</summary>
		public readonly struct Assignment
		{
			/// <summary>The FishMMO biome asset's name.</summary>
			public readonly string Biome;
			/// <summary>The slot key (<see cref="BiomeLocalArt"/>'s).</summary>
			public readonly string Slot;
			public readonly string Photo;
			/// <summary>WorldEditor's tile size for the slot, in metres.</summary>
			public readonly float TileMetres;

			public Assignment(string biome, string slot, string photo, float tileMetres)
			{
				Biome = biome;
				Slot = slot;
				Photo = photo;
				TileMetres = tileMetres;
			}
		}

		/// <summary>WorldEditor's photo slots (see the remarks for how this was derived).</summary>
		public static readonly Assignment[] Assignments =
		{
			// WorldEditor asset                      FishMMO biome     slot        photo            tile (m)
			/* BiomeTemplate_Beach_E6CC99        */ new Assignment("Beach", "main", "Beach Sand", 10f),
			/* BiomeTemplate_CoastalWater_66B2E6 */ new Assignment("Coastal Water", "main", "Coastal Water", 15f),
			/* BiomeTemplate_DeepOcean_0D1A4C    */ new Assignment("Deep Ocean", "main", "Deep Ocean", 15f),
			/* BiomeTemplate_Forest_338033       */ new Assignment("Forest", "main", "Grass 2", 15f),
			/* BiomeTemplate_Forest_338033       */ new Assignment("Forest", "detail/0", "Forest", 8f),
			/* BiomeTemplate_Forest_338033       */ new Assignment("Forest", "detail/1", "Forest 2", 8f),
			/* BiomeTemplate_Glacier_CCE6FF      */ new Assignment("Glacier", "main", "Glacier", 15f),
			/* BiomeTemplate_Grassland_66B24C    */ new Assignment("Grassland", "main", "Grass", 8f),
			/* BiomeTemplate_Grassland_66B24C    */ new Assignment("Grassland", "detail/0", "Grass 2", 16f),
			/* BiomeTemplate_Ocean_1A4C99        */ new Assignment("Ocean", "main", "Ocean", 15f),
			/* BiomeTemplate_RockyTerrain_666666 */ new Assignment("Rocky Terrain", "main", "Rocky Terrain", 15f),
			/* BiomeTemplate_Snow_E6F2FF         */ new Assignment("Snow", "main", "Snow", 15f),
			/* BiomeTemplate_Woodland_73A659     */ new Assignment("Woodland", "main", "Woodland", 5f),
			/* BiomeTemplate_Woodland_73A659     */ new Assignment("Woodland", "detail/0", "Woodland 2", 15f),
		};

		/// <summary>
		/// Photos copied under a procedural texture's name, which the terrain array baker's name rule
		/// (<c>Assets/LOCAL/Biomes/Textures/&lt;committed file name&gt;.*</c>) then uses wherever that
		/// family is drawn. Ground families: <see cref="Ground.Soil"/>, <see cref="Ground.Pebbles"/>.
		/// </summary>
		public static readonly KeyValuePair<string, string>[] NameCopies =
		{
			new KeyValuePair<string, string>("Dirt", "Ground_" + Ground.Soil + "_Albedo"),
			new KeyValuePair<string, string>("Riverbed", "Ground_" + Ground.Pebbles + "_Albedo"),
		};

		private static readonly Regex MetaGuidLine = new Regex(@"^guid:\s*[0-9a-fA-F]{32}\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

		/// <summary>What a run did.</summary>
		public sealed class Report
		{
			public readonly List<string> Lines = new List<string>();
			public readonly List<string> Problems = new List<string>();

			public override string ToString()
			{
				var sb = new StringBuilder();
				sb.AppendLine($"WorldEditor photos → LOCAL: {Lines.Count} change(s), {Problems.Count} problem(s).");
				foreach (string p in Problems) sb.AppendLine("PROBLEM " + p);
				foreach (string l in Lines) sb.AppendLine(l);
				return sb.ToString();
			}
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Import WorldEditor photos to LOCAL", Section = "Authoring", Order = 5,
			Tooltip = "Copies WorldEditor's 15 terrain photos into Assets/LOCAL/Biomes/Textures/WorldEditor (gitignored), restores WorldEditor's per-biome use of them as LOCAL sidecar overrides, clears the committed biome slots that referenced the six committed photos, then deletes those six from the repository once nothing references them. Safe to run again.",
			Confirm = "Copy the WorldEditor photos into Assets/LOCAL, write LOCAL overrides for the biomes that used them, clear the committed biome slots that reference the six committed photos (Forest, Glacier, Grass, Ocean, Snow, Woodland) and delete those photos from Assets/Prefabs/Shared/Biomes/Textures? Run Author biome art afterwards to fill the cleared slots.")]
		public static void RunFromDashboard()
		{
			Report report = Run(true);
			if (report.Problems.Count > 0)
			{
				Debug.LogWarning("[WorldEditor photos] " + report);
			}
			else
			{
				Debug.Log("[WorldEditor photos] " + report);
			}
		}

		/// <summary>Runs the import. <paramref name="interactive"/> may ask for the WorldEditor folder when it is not where expected.</summary>
		public static Report Run(bool interactive)
		{
			var report = new Report();
			string source = ResolveSource(interactive);
			if (source == null)
			{
				report.Problems.Add($"WorldEditor's photo folder was not found (looked in '{DefaultSourceRelative}' from the project root); the six committed photos are used as the source for themselves.");
			}

			// 1, 2. Copies.
			WorldEditorAssets.EnsureFolder(LocalFolder);
			foreach (string photo in Photos)
			{
				CopyPhoto(source, photo, $"{LocalFolder}/{photo}.jpg", report);
			}
			foreach (KeyValuePair<string, string> copy in NameCopies)
			{
				if (AnyTextureNamed(BiomeLocalArt.TexturesFolder, copy.Value))
				{
					continue;
				}
				CopyPhoto(source, copy.Key, $"{BiomeLocalArt.TexturesFolder}/{copy.Value}.jpg", report);
			}

			// 3, 4. Overrides, then the committed slots.
			var biomes = BiomesByName();
			var wanted = new List<Wanted>();
			foreach (Assignment a in Assignments)
			{
				wanted.Add(new Wanted { Biome = a.Biome, Slot = a.Slot, AlbedoPhoto = a.Photo, TileMetres = a.TileMetres });
			}
			var clears = new List<Clear>();
			foreach (KeyValuePair<string, BiomeTemplate> pair in biomes)
			{
				foreach (string slot in BiomeLocalArt.SlotsOf(pair.Value))
				{
					TerrainTextureLayer layer = BiomeLocalArt.CommittedLayer(pair.Value, slot);
					if (layer == null)
					{
						continue;
					}
					string albedo = CommittedPhotoName(layer.albedoTexture);
					string normal = CommittedPhotoName(layer.normalTexture);
					string mask = CommittedPhotoName(layer.maskTexture);
					if (albedo == null && normal == null && mask == null)
					{
						continue;
					}
					clears.Add(new Clear { Biome = pair.Value, Slot = slot, Albedo = albedo != null, Normal = normal != null, Mask = mask != null });
					Wanted w = wanted.Find(x => x.Biome == pair.Key && x.Slot == slot);
					if (w == null)
					{
						w = new Wanted { Biome = pair.Key, Slot = slot, AlbedoPhoto = albedo, TileMetres = layer.tileSize.x };
						wanted.Add(w);
					}
					w.NormalPhoto = normal;
					w.MaskPhoto = mask;
					// A committed normal or mask beside the photo albedo is committed art, which LOCAL may reference.
					w.CommittedNormal = normal == null ? layer.normalTexture : null;
					w.CommittedMask = mask == null ? layer.maskTexture : null;
				}
			}

			bool overridesComplete = true;
			foreach (Wanted w in wanted)
			{
				if (!biomes.TryGetValue(w.Biome, out BiomeTemplate biome))
				{
					report.Problems.Add($"No biome named '{w.Biome}' for {w.Slot} ← {w.AlbedoPhoto}.");
					continue;
				}
				if (!WriteOverride(biome, w, report))
				{
					overridesComplete = false;
				}
			}

			int cleared = 0;
			foreach (Clear clear in clears)
			{
				TerrainTextureLayer layer = BiomeLocalArt.CommittedLayer(clear.Biome, clear.Slot);
				BiomeLocalArt.SlotOverride entry = BiomeLocalArtIndex.Find(clear.Biome, clear.Slot);
				if (entry == null)
				{
					// The photo's LOCAL copy is not reachable through the sidecar: clearing would lose the look.
					report.Problems.Add($"{clear.Biome.name} {clear.Slot}: no LOCAL override could be written, so its committed photo reference was kept.");
					continue;
				}
				Undo.RecordObject(clear.Biome, "Move committed photos to LOCAL");
				if (clear.Albedo) layer.albedoTexture = null;
				if (clear.Normal) layer.normalTexture = null;
				if (clear.Mask) layer.maskTexture = null;
				clear.Biome.InvalidateTextureLayerCache();
				EditorUtility.SetDirty(clear.Biome);
				cleared++;
				report.Lines.Add($"{clear.Biome.name} {clear.Slot}: committed photo reference cleared (now drawn from LOCAL on this machine; Author biome art fills the slot for everyone else)");
			}
			if (cleared > 0)
			{
				AssetDatabase.SaveAssets();
			}

			// 5. The committed photos, once nothing points at them.
			DeleteCommittedPhotos(report, overridesComplete);
			AssetDatabase.Refresh();
			BiomeLocalArtIndex.Invalidate();
			return report;
		}

		private sealed class Wanted
		{
			public string Biome;
			public string Slot;
			public string AlbedoPhoto;
			public string NormalPhoto;
			public string MaskPhoto;
			public Texture2D CommittedNormal;
			public Texture2D CommittedMask;
			public float TileMetres;
		}

		private sealed class Clear
		{
			public BiomeTemplate Biome;
			public string Slot;
			public bool Albedo, Normal, Mask;
		}

		// ── Source and copies ─────────────────────────────────────────

		/// <summary>WorldEditor's photo folder: remembered, else beside this repository, else (interactively) asked for.</summary>
		private static string ResolveSource(bool interactive)
		{
			string remembered = EditorPrefs.GetString(SourcePrefKey, string.Empty);
			if (HasPhotos(remembered))
			{
				return remembered;
			}
			string projectRoot = Path.GetDirectoryName(Application.dataPath);
			string guess = Path.GetFullPath(Path.Combine(projectRoot, DefaultSourceRelative));
			if (HasPhotos(guess))
			{
				return guess;
			}
			if (interactive && !Application.isBatchMode)
			{
				string picked = EditorUtility.OpenFolderPanel("WorldEditor's Assets/Prefabs folder (the terrain photos)", projectRoot, string.Empty);
				if (HasPhotos(picked))
				{
					EditorPrefs.SetString(SourcePrefKey, picked);
					return picked;
				}
			}
			return null;
		}

		private static bool HasPhotos(string folder) => !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "Dirt.jpg"));

		/// <summary>True when a texture with this name, in any of the extensions the LOCAL rule tries, is already in a folder.</summary>
		private static bool AnyTextureNamed(string folder, string name)
		{
			foreach (string extension in BiomeLocalArt.TextureExtensions)
			{
				if (File.Exists($"{folder}/{name}{extension}") || File.Exists($"{folder}/{name}{extension.ToUpperInvariant()}"))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Copies one photo to a LOCAL path unless something is there already: WorldEditor's file, or
		/// the committed copy when WorldEditor is not on this machine. The .meta keeps the source's
		/// import settings with a GUID of the copy's own, written before the image so the first import
		/// keeps it.
		/// </summary>
		private static void CopyPhoto(string source, string photo, string destination, Report report)
		{
			if (File.Exists(destination))
			{
				return;
			}
			string from = source != null ? Path.Combine(source, photo + ".jpg") : null;
			if (from == null || !File.Exists(from))
			{
				string committed = $"{CommittedFolder}/{photo}.jpg";
				from = File.Exists(committed) ? committed : null;
			}
			if (from == null)
			{
				report.Problems.Add($"{photo}.jpg: not found in WorldEditor or the project; {destination} not written.");
				return;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(destination));
			string guid = LocalGuidFor(destination);
			string meta = File.Exists(from + ".meta") ? File.ReadAllText(from + ".meta") : null;
			meta = meta != null && MetaGuidLine.IsMatch(meta)
				? MetaGuidLine.Replace(meta, "guid: " + guid, 1)
				: $"fileFormatVersion: 2\nguid: {guid}\n";
			File.WriteAllText(destination + ".meta", meta);
			File.Copy(from, destination, false);
			AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceUpdate);
			report.Lines.Add($"copied {from} → {destination}");
		}

		/// <summary>A copy's GUID: derived from its path (so a re-copy after a wipe is the same asset), salted apart from the payload's.</summary>
		public static string LocalGuidFor(string path)
		{
			using (MD5 md5 = MD5.Create())
			{
				byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("FishMMO.WorldEditorPhotoImport:" + path.Replace('\\', '/')));
				var sb = new StringBuilder(32);
				foreach (byte b in hash)
				{
					sb.Append(b.ToString("x2"));
				}
				return sb.ToString();
			}
		}

		private static Texture2D LocalPhoto(string photo) => photo != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{LocalFolder}/{photo}.jpg") : null;

		// ── Biomes and overrides ──────────────────────────────────────

		private static Dictionary<string, BiomeTemplate> BiomesByName()
		{
			var result = new Dictionary<string, BiomeTemplate>(StringComparer.Ordinal);
			var paths = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate)))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!BiomeLocalArtIndex.IsLocalPath(path))
				{
					paths.Add(path);
				}
			}
			paths.Sort(StringComparer.Ordinal);
			foreach (string path in paths)
			{
				var biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(path);
				if (biome != null && !result.ContainsKey(biome.name))
				{
					result.Add(biome.name, biome);
				}
			}
			return result;
		}

		/// <summary>The committed photo's name when a texture is one of the six, else null.</summary>
		private static string CommittedPhotoName(Texture2D texture)
		{
			if (texture == null)
			{
				return null;
			}
			string path = AssetDatabase.GetAssetPath(texture);
			if (!path.StartsWith(CommittedFolder + "/", StringComparison.Ordinal) || !path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			string name = Path.GetFileNameWithoutExtension(path);
			return Array.IndexOf(CommittedPhotos, name) >= 0 ? name : null;
		}

		/// <summary>
		/// The LOCAL terrain layer for one wanted override, made or brought up to date. Layers that
		/// would be identical are shared: they are named by photo and tile size, unless the slot also
		/// carries its own normal or mask.
		/// </summary>
		private static TerrainLayer EnsureLayer(Wanted w, Report report)
		{
			Texture2D albedo = LocalPhoto(w.AlbedoPhoto);
			if (albedo == null)
			{
				report.Problems.Add($"{w.Biome} {w.Slot}: the LOCAL copy of '{w.AlbedoPhoto}' is missing, so no override was written.");
				return null;
			}
			Texture2D normal = w.NormalPhoto != null ? LocalPhoto(w.NormalPhoto) : w.CommittedNormal;
			Texture2D mask = w.MaskPhoto != null ? LocalPhoto(w.MaskPhoto) : w.CommittedMask;
			string tile = w.TileMetres.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
			string name = normal == null && mask == null
				? $"{w.AlbedoPhoto} {tile}m"
				: $"{w.Biome} {w.Slot.Replace('/', '-')}";
			string path = $"{LocalLayersFolder}/{name}.terrainlayer";

			WorldEditorAssets.EnsureFolder(LocalLayersFolder);
			var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
			bool created = layer == null;
			if (created)
			{
				layer = new TerrainLayer();
			}
			layer.name = name;
			layer.diffuseTexture = albedo;
			layer.normalMapTexture = normal;
			layer.maskMapTexture = mask;
			layer.tileSize = new Vector2(w.TileMetres, w.TileMetres);
			layer.tileOffset = Vector2.zero;
			layer.metallic = 0f;
			layer.smoothness = 0f;
			layer.specular = Color.black;
			layer.normalScale = 1f;
			if (created)
			{
				AssetDatabase.CreateAsset(layer, path);
				report.Lines.Add($"made LOCAL layer {path}");
			}
			else
			{
				EditorUtility.SetDirty(layer);
				AssetDatabase.SaveAssetIfDirty(layer);
			}
			return layer;
		}

		/// <summary>
		/// Points the biome's sidecar slot at the wanted layer when the slot is empty or already holds
		/// it. True when the sidecar now draws the photo for that slot.
		/// </summary>
		private static bool WriteOverride(BiomeTemplate biome, Wanted w, Report report)
		{
			TerrainLayer layer = EnsureLayer(w, report);
			if (layer == null)
			{
				return false;
			}
			BiomeLocalArt.SlotOverride current = BiomeLocalArtIndex.Find(biome, w.Slot);
			if (current != null)
			{
				if (current.TerrainLayer == layer)
				{
					return true;
				}
				// Somebody's own override (or an earlier one of this photo by single textures) stands.
				bool drawsThePhoto = current.TerrainLayer != null ? current.TerrainLayer.diffuseTexture == layer.diffuseTexture : current.Albedo == layer.diffuseTexture;
				if (!drawsThePhoto)
				{
					report.Lines.Add($"{biome.name} {w.Slot}: the sidecar already overrides this slot with other art; kept, and '{w.AlbedoPhoto}' not assigned");
				}
				return true;
			}
			BiomeLocalArt art = BiomeLocalArtIndex.GetOrCreate(biome);
			if (art == null)
			{
				report.Problems.Add($"{biome.name}: its LOCAL sidecar could not be created.");
				return false;
			}
			Undo.RecordObject(art, "WorldEditor photo override");
			art.GetOrAdd(w.Slot).TerrainLayer = layer;
			EditorUtility.SetDirty(art);
			AssetDatabase.SaveAssetIfDirty(art);
			BiomeLocalArtIndex.Invalidate();
			bool slotExists = BiomeLocalArt.CommittedLayer(biome, w.Slot) != null;
			report.Lines.Add($"{biome.name} {w.Slot}: LOCAL override → {AssetDatabase.GetAssetPath(layer)}{(slotExists ? string.Empty : " (the biome has no such slot yet; the override waits for one)")}");
			return true;
		}

		// ── Removing the committed photos ─────────────────────────────

		private static void DeleteCommittedPhotos(Report report, bool overridesComplete)
		{
			var guids = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (string photo in CommittedPhotos)
			{
				string path = $"{CommittedFolder}/{photo}.jpg";
				if (!File.Exists(path))
				{
					continue;
				}
				if (!File.Exists($"{LocalFolder}/{photo}.jpg"))
				{
					report.Problems.Add($"{path}: its LOCAL copy is missing, so it was not deleted.");
					continue;
				}
				string guid = AssetDatabase.AssetPathToGUID(path);
				if (!string.IsNullOrEmpty(guid))
				{
					guids[guid] = path;
				}
			}
			if (guids.Count == 0)
			{
				return;
			}
			if (!overridesComplete)
			{
				report.Problems.Add("Some LOCAL overrides could not be written; the committed photos were not deleted.");
				return;
			}

			// On disk, not in memory: what git will carry.
			SortedDictionary<string, SortedSet<string>> referencing = AssetGuidScan.FilesReferencing(guids.Keys, AssetGuidScan.IsMachineLocal);
			var stillUsed = new HashSet<string>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, SortedSet<string>> file in referencing)
			{
				foreach (string guid in file.Value)
				{
					stillUsed.Add(guid);
					report.Problems.Add($"{guids[guid]} is still referenced by {file.Key}, so it was not deleted.");
				}
			}
			foreach (KeyValuePair<string, string> photo in guids)
			{
				if (stillUsed.Contains(photo.Key))
				{
					continue;
				}
				ProceduralArtPayload.Delete(photo.Value);
				report.Lines.Add($"deleted committed photo {photo.Value} (its copy is {LocalFolder}/{Path.GetFileName(photo.Value)})");
			}
		}
	}
}
#endif
