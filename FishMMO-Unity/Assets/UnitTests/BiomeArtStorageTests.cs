using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Where generated and licensed biome art may live: nothing committed references Assets/LOCAL;
	/// everything generated (payload and wrappers) is gitignored and addressed by GUIDs derived from
	/// its paths; LOCAL art found on a committed slot moves to the biome's sidecar; and the WorldEditor
	/// photo table names only things that exist. (Determinism itself: <see cref="ProceduralArtDeterminismTests"/>.)
	/// </summary>
	[TestFixture]
	public class BiomeArtStorageTests
	{
		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
		}

		private Texture2D MakeTexture(string name)
		{
			var texture = new Texture2D(4, 4) { name = name };
			created.Add(texture);
			return texture;
		}

		// ── The LOCAL rule ────────────────────────────────────────────

		/// <summary>
		/// The guard: no file that will be committed — everything under Assets except Assets/LOCAL
		/// (any capitalisation, as the gitignore matches it) and the gitignored generated art — names the GUID
		/// of anything under Assets/LOCAL. Read from disk as text, which is what git carries.
		/// </summary>
		[Test]
		public void NoCommittedAssetReferencesAnythingUnderAssetsLocal()
		{
			var localGuids = new HashSet<string>(StringComparer.Ordinal);
			foreach (string dir in Directory.GetDirectories("Assets"))
			{
				string path = dir.Replace('\\', '/');
				if (BiomeLocalArtIndex.IsLocalPath(path))
				{
					localGuids.UnionWith(AssetGuidScan.GuidsUnder(path));
				}
			}
			foreach (string file in Directory.GetFiles("Assets", "*.meta"))
			{
				string path = file.Replace('\\', '/');
				if (BiomeLocalArtIndex.IsLocalPath(path))
				{
					Match m = Regex.Match(File.ReadAllText(path), @"^guid:\s*([0-9a-fA-F]{32})", RegexOptions.Multiline);
					if (m.Success)
					{
						localGuids.Add(m.Groups[1].Value.ToLowerInvariant());
					}
				}
			}
			if (localGuids.Count == 0)
			{
				Assert.Pass("No Assets/LOCAL on this machine; nothing can reference it.");
			}

			SortedDictionary<string, SortedSet<string>> offenders = AssetGuidScan.FilesReferencing(localGuids, AssetGuidScan.IsMachineLocal);
			if (offenders.Count > 0)
			{
				var sb = new StringBuilder();
				sb.AppendLine($"{offenders.Count} committed file(s) reference assets under Assets/LOCAL, which other clones do not have. Move the reference to the biome's BiomeLocalArt sidecar (Author biome art does this for biome slots):");
				foreach (KeyValuePair<string, SortedSet<string>> file in offenders)
				{
					var targets = new List<string>();
					foreach (string guid in file.Value)
					{
						string target = AssetDatabase.GUIDToAssetPath(guid);
						targets.Add(string.IsNullOrEmpty(target) ? guid : target);
					}
					sb.AppendLine($"  {file.Key} → {string.Join(", ", targets)}");
				}
				Assert.Fail(sb.ToString());
			}
		}

		[Test]
		public void TheScanFindsAReferenceAndIgnoresAFilesOwnMeta()
		{
			// Outside Assets, so the editor never imports the fixture files.
			string root = Path.Combine(Path.GetTempPath(), "BiomeArtStorageTests_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
			try
			{
				Directory.CreateDirectory(root);
				const string target = "0123456789abcdef0123456789abcdef";
				File.WriteAllText(root + "/Refers.asset", "%YAML 1.1\n--- !u!114 &11400000\nMonoBehaviour:\n  tex: {fileID: 2800000, guid: " + target + ", type: 3}\n");
				File.WriteAllText(root + "/Own.png.meta", "fileFormatVersion: 2\nguid: " + target + "\n");
				File.WriteAllText(root + "/Json.shadergraph", "{\"m_Texture\": \"{\\\"guid\\\":\\\"" + target + "\\\"}\"}");
				SortedDictionary<string, SortedSet<string>> found = AssetGuidScan.FilesReferencing(new[] { target }, null, root);
				Assert.That(found.Keys, Does.Contain(root + "/Refers.asset"));
				Assert.That(found.Keys, Does.Contain(root + "/Json.shadergraph"), "JSON references, escaped or not, count too");
				Assert.That(found.Keys, Does.Not.Contain(root + "/Own.png.meta"), "a .meta declaring the GUID is not a reference to it");
			}
			finally
			{
				if (Directory.Exists(root))
				{
					Directory.Delete(root, true);
				}
			}
		}

		// ── The payload ───────────────────────────────────────────────

		[Test]
		public void PayloadGuids_AreAFunctionOfThePath()
		{
			string a = ProceduralArtCatalogue.GroundTexture("Grass", "Albedo");
			Assert.That(ProceduralArtPayload.GuidFor(a), Is.EqualTo(ProceduralArtPayload.GuidFor(a)));
			Assert.That(ProceduralArtPayload.GuidFor(a), Is.EqualTo(ProceduralArtPayload.GuidFor(a.Replace('/', '\\'))), "separators do not matter");
			Assert.That(ProceduralArtPayload.GuidFor(a), Does.Match("^[0-9a-f]{32}$"));
			// Pinned: a change here re-addresses every payload file and breaks every committed wrapper.
			Assert.That(ProceduralArtPayload.GuidFor("Assets/x.png"), Is.EqualTo(Md5Hex("FishMMO.ProceduralArtPayload:Assets/x.png")));

			var seen = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (string path in ProceduralArtCatalogue.PayloadPaths())
			{
				string guid = ProceduralArtPayload.GuidFor(path);
				Assert.That(seen.ContainsKey(guid), Is.False, $"{path} and {(seen.TryGetValue(guid, out string other) ? other : "?")} share a GUID");
				seen[guid] = path;
			}
		}

		private static string Md5Hex(string text)
		{
			using (var md5 = System.Security.Cryptography.MD5.Create())
			{
				var sb = new StringBuilder();
				foreach (byte b in md5.ComputeHash(Encoding.UTF8.GetBytes(text)))
				{
					sb.Append(b.ToString("x2"));
				}
				return sb.ToString();
			}
		}

		[Test]
		public void PayloadIsOnlySingleMainObjectFiles_AndEverythingGeneratedIsTheGenerators()
		{
			var payload = new HashSet<string>(ProceduralArtCatalogue.PayloadPaths());
			Assert.That(payload.Count, Is.GreaterThan(0));
			foreach (string path in payload)
			{
				Assert.That(ProceduralArtPayload.IsPayload(path), Is.True, path);
				string ext = Path.GetExtension(path);
				// Only these have a fixed main file ID (2800000 for an imported image, 4300000 for a mesh
				// saved alone in a .asset); prefabs get theirs from ProceduralArtFileIds, outside the payload.
				Assert.That(ext == ".png" || (ext == ".asset" && path.StartsWith(ProceduralArtCatalogue.MeshesFolder + "/", StringComparison.Ordinal)), Is.True,
					$"{path}: only images and single meshes may be payload");
				Assert.That(ProceduralArtLedger.IsGeneratorOwned(path), Is.True);
			}
			foreach (string path in ProceduralArtCatalogue.WrapperPaths())
			{
				Assert.That(ProceduralArtPayload.IsPayload(path), Is.False, $"{path} is a wrapper, not payload");
				Assert.That(payload.Contains(path), Is.False, path);
				Assert.That(ProceduralArtPayload.IsGenerated(path), Is.True, $"{path}: wrappers are generated build output now");
				Assert.That(ProceduralArtLedger.IsGeneratorOwned(path), Is.True, $"{path}: no 'kept because edited' rule survives for wrappers");
				string ext = Path.GetExtension(path);
				Assert.That(ext == ".terrainlayer" || ext == ".mat" || ext == ".prefab", Is.True, path);
			}
			Assert.That(ProceduralArtPayload.IsGenerated(ProceduralArtPayload.LedgerPath), Is.True, "the generator's record is gitignored with what it records");
			Assert.That(ProceduralArtPayload.IsGenerated(BiomeTerrainLayers.BuiltFolder + "/X 0000.terrainlayer"), Is.True, "built layers are generated too");
			Assert.That(ProceduralArtPayload.IsGenerated("Assets/Prefabs/Shared/Biomes/Textures/Grass.jpg"), Is.False, "committed biome textures are not");
			Assert.That(ProceduralArtPayload.IsGenerated("Assets/Prefabs/Shared/Biomes/GeneratedX/a.mat"), Is.False, "a sibling folder with a longer name is not");
		}

		[Test]
		public void EverythingGeneratedIsGitignored()
		{
			string gitignore = Path.Combine(Path.GetDirectoryName(Application.dataPath), ".gitignore");
			Assume.That(File.Exists(gitignore), Is.True);
			string[] lines = File.ReadAllLines(gitignore);
			foreach (string root in ProceduralArtPayload.GeneratedRoots)
			{
				string folder = root.Substring("Assets/".Length);
				Assert.That(lines, Does.Contain("/[Aa]ssets/" + folder + "/"), $"the generated folder {root}");
				Assert.That(lines, Does.Contain("/[Aa]ssets/" + folder + ".meta"), $"and its .meta");
			}
		}

		[Test]
		public void PayloadMeta_PinsTheGuid_AndANativeAssetsMainObject()
		{
			string guid = ProceduralArtPayload.GuidFor(ProceduralArtCatalogue.MeshPath("Pebbles"));
			string mesh = ProceduralArtPayload.MetaText(guid, ProceduralArtPayload.MeshFileId);
			Assert.That(mesh, Does.Contain("guid: " + guid + "\n"));
			Assert.That(mesh, Does.Contain("NativeFormatImporter:"));
			Assert.That(mesh, Does.Contain("mainObjectFileID: 4300000"));
			string image = ProceduralArtPayload.MetaText(guid, 0);
			Assert.That(image, Is.EqualTo("fileFormatVersion: 2\nguid: " + guid + "\nlabels:\n- FishMMOProceduralArt\n"), "an image's importer settings come from the postprocessor on every import; the label is written up front");
			Assert.That(ProceduralArtPayload.TextureFileId, Is.EqualTo(2800000));
			Assert.That(ProceduralArtPayload.MeshFileId, Is.EqualTo(43 * 100000));
		}

		[Test]
		public void TheFingerprint_IsStable_AndFollowsTheSeed()
		{
			string a = ProceduralArtPayload.CurrentFingerprint(ProceduralArtCatalogue.DefaultSeed);
			Assert.That(ProceduralArtPayload.CurrentFingerprint(ProceduralArtCatalogue.DefaultSeed), Is.EqualTo(a));
			Assert.That(ProceduralArtPayload.CurrentFingerprint(ProceduralArtCatalogue.DefaultSeed + 1), Is.Not.EqualTo(a));
			string folder = ProceduralArtPayload.SourceFolder();
			Assert.That(folder, Is.Not.Null, "the generator's sources are found by its script");
			foreach (string file in ProceduralArtPayload.SourceFiles)
			{
				Assert.That(File.Exists(Path.Combine(folder, file)), Is.True, $"{file} is hashed but does not exist; update SourceFiles");
			}
		}

		// ── LOCAL references on committed slots ──────────────────────

		private static bool NamedLocal(Object o) => o != null && o.name.StartsWith("LOCAL", StringComparison.Ordinal);

		[Test]
		public void LocalArtOnACommittedSlot_MovesToTheSidecar_AndTheSlotIsEmptied()
		{
			Texture2D local = MakeTexture("LOCAL photo");
			Texture2D committedNormal = MakeTexture("Ground_Grass_Normal");
			var slot = new TerrainTextureLayer { albedoTexture = local, normalTexture = committedNormal };
			var target = new BiomeLocalArt.SlotOverride { Slot = "main" };

			Assert.That(BiomeArtAuthoring.MoveLocalReferences(slot, target, NamedLocal, out string conflict), Is.True);
			Assert.That(conflict, Is.Null);
			Assert.That(target.Albedo, Is.SameAs(local));
			Assert.That(slot.albedoTexture, Is.Null, "a committed slot never points into LOCAL");
			Assert.That(slot.normalTexture, Is.SameAs(committedNormal), "committed art stays");
			Assert.That(target.Normal, Is.Null);
			Assert.That(BiomeArtAuthoring.IsEmptySlot(slot), Is.True, "so the authoring pass fills it");

			Assert.That(BiomeArtAuthoring.MoveLocalReferences(slot, target, NamedLocal, out _), Is.False, "a second run finds nothing");
		}

		[Test]
		public void AnExistingSidecarOverrideWins_ButTheSlotIsStillCleared()
		{
			Texture2D mine = MakeTexture("LOCAL mine");
			Texture2D stray = MakeTexture("LOCAL stray");
			var slot = new TerrainTextureLayer { albedoTexture = stray };
			var target = new BiomeLocalArt.SlotOverride { Slot = "main", Albedo = mine };

			Assert.That(BiomeArtAuthoring.MoveLocalReferences(slot, target, NamedLocal, out string conflict), Is.True);
			Assert.That(target.Albedo, Is.SameAs(mine));
			Assert.That(conflict, Does.Contain("LOCAL stray"));
			Assert.That(slot.albedoTexture, Is.Null);
		}

		[Test]
		public void APreviewMovesNothing()
		{
			Texture2D local = MakeTexture("LOCAL photo");
			var slot = new TerrainTextureLayer { albedoTexture = local };
			Assert.That(BiomeArtAuthoring.MoveLocalReferences(slot, null, NamedLocal, out _), Is.True);
			Assert.That(slot.albedoTexture, Is.SameAs(local));
		}

		// ── The WorldEditor photo table ───────────────────────────────

		[Test]
		public void TheWorldEditorTable_NamesRealBiomesSlotsAndPhotos()
		{
			var biomes = new HashSet<string>(StringComparer.Ordinal);
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate)))
			{
				biomes.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
			}
			var photos = new HashSet<string>(WorldEditorPhotoImport.Photos);
			Assert.That(photos.Count, Is.EqualTo(15));
			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (WorldEditorPhotoImport.Assignment a in WorldEditorPhotoImport.Assignments)
			{
				Assert.That(biomes, Does.Contain(a.Biome), $"no FishMMO biome named '{a.Biome}'");
				Assert.That(a.Slot, Does.Match(@"^(main|riverbed|lakebed|road|path|detail/\d+|cliff/\d+)$"));
				Assert.That(photos, Does.Contain(a.Photo));
				Assert.That(a.TileMetres, Is.GreaterThan(0f));
				Assert.That(keys.Add(a.Biome + "|" + a.Slot), Is.True, $"{a.Biome} {a.Slot} is assigned twice");
			}
			foreach (string committed in WorldEditorPhotoImport.CommittedPhotos)
			{
				Assert.That(photos, Does.Contain(committed), $"the committed photo '{committed}' must have a WorldEditor copy");
			}
			foreach (KeyValuePair<string, string> copy in WorldEditorPhotoImport.NameCopies)
			{
				Assert.That(photos, Does.Contain(copy.Key));
				bool namesAFamily = false;
				foreach (SurfaceRecipe r in SurfaceCatalogue.GroundRecipes)
				{
					namesAFamily |= Path.GetFileNameWithoutExtension(ProceduralArtCatalogue.GroundTexture(r.Name, "Albedo")) == copy.Value;
				}
				Assert.That(namesAFamily, Is.True, $"'{copy.Value}' is not a generated ground albedo's file name, so the LOCAL name rule would never use it");
			}
		}

		[Test]
		public void LocalCopies_GetGuidsOfTheirOwn()
		{
			string path = WorldEditorPhotoImport.LocalFolder + "/Grass.jpg";
			Assert.That(WorldEditorPhotoImport.LocalGuidFor(path), Is.EqualTo(WorldEditorPhotoImport.LocalGuidFor(path)));
			Assert.That(WorldEditorPhotoImport.LocalGuidFor(path), Is.Not.EqualTo("b57d7a0ebf324414fb3f358921cc80fc"), "never the committed photo's GUID");
			Assert.That(WorldEditorPhotoImport.LocalGuidFor(path), Is.Not.EqualTo(ProceduralArtPayload.GuidFor(path)));
		}
	}
}
