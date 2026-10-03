using System;
using System.Collections.Generic;
using System.Globalization;
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
	/// Nothing generated is committed, so every generated asset must come out with the same IDs on
	/// every machine: GUIDs derived from paths, prefab object IDs derived from the hierarchy, the
	/// one-time migration that re-addresses old random-ID art and every reference to it, built terrain
	/// layers that never bake in a swatch or LOCAL art, and the guard that no committed file still
	/// names generated art by a GUID that is not its path's.
	/// </summary>
	[TestFixture]
	public class ProceduralArtDeterminismTests
	{
		private readonly List<Object> created = new List<Object>();
		private readonly List<string> tempRoots = new List<string>();

		[TearDown]
		public void CleanUp()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
			foreach (string root in tempRoots)
			{
				if (Directory.Exists(root))
				{
					Directory.Delete(root, true);
				}
			}
			tempRoots.Clear();
		}

		/// <summary>A scratch "project" outside Assets, so the editor never imports the fixture files.</summary>
		private string TempProject()
		{
			string root = Path.Combine(Path.GetTempPath(), "ProceduralArtDeterminism_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
			Directory.CreateDirectory(root);
			tempRoots.Add(root);
			return root;
		}

		private T Track<T>(T o) where T : Object
		{
			created.Add(o);
			return o;
		}

		// ── Derived GUIDs ─────────────────────────────────────────────

		[Test]
		public void EveryGeneratedAsset_HasADistinctGuidDerivedFromItsPath()
		{
			var seen = new Dictionary<string, string>(StringComparer.Ordinal);
			var all = new List<string>(ProceduralArtCatalogue.PayloadPaths());
			all.AddRange(ProceduralArtCatalogue.WrapperPaths());
			all.Add(BiomeTerrainLayers.BuiltPath("Grass", "x|y|z|4,4|0|0"));
			foreach (string path in all)
			{
				string guid = ProceduralArtPayload.GuidFor(path);
				Assert.That(guid, Does.Match("^[0-9a-f]{32}$"));
				Assert.That(ProceduralArtPayload.GuidFor(path), Is.EqualTo(guid), "stable across calls");
				Assert.That(seen.ContainsKey(guid), Is.False, $"{path} and {(seen.TryGetValue(guid, out string other) ? other : "?")} share a GUID");
				seen[guid] = path;
			}
			// Pinned: a change here re-addresses every generated asset and orphans every committed reference to one.
			string prefab = ProceduralArtCatalogue.PrefabPath("Boulder_Grey_Round");
			Assert.That(ProceduralArtPayload.GuidFor(prefab), Is.EqualTo(Md5Hex("FishMMO.ProceduralArtPayload:" + prefab)));
		}

		[Test]
		public void NativeMainFileIds_AreTheFixedOnes()
		{
			Assert.That(ProceduralArtPayload.MaterialFileId, Is.EqualTo(21 * 100000));
			Assert.That(ProceduralArtPayload.MeshFileId, Is.EqualTo(43 * 100000));
			Assert.That(ProceduralArtPayload.TerrainLayerFileId, Is.EqualTo(8574412962073106934L), "read from Unity's own .terrainlayer files");

			// Every .terrainlayer this project already has agrees, wherever it lives.
			foreach (string guid in AssetDatabase.FindAssets("t:TerrainLayer"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (path.StartsWith("Assets/", StringComparison.Ordinal) && path.EndsWith(".terrainlayer", StringComparison.Ordinal)
					&& !path.StartsWith("Assets/LOCAL", StringComparison.OrdinalIgnoreCase))
				{
					Assert.That(ProceduralArtPayload.MainFileIdOf(path), Is.EqualTo(ProceduralArtPayload.TerrainLayerFileId), path);
				}
			}

			string guid0 = ProceduralArtPayload.GuidFor(ProceduralArtCatalogue.PrefabPath("X"));
			string meta = ProceduralArtPayload.PrefabMetaText(guid0);
			Assert.That(meta, Does.StartWith("fileFormatVersion: 2\nguid: " + guid0 + "\nlabels:\n- FishMMOProceduralArt\nPrefabImporter:\n"));
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

		// ── Prefab object IDs ─────────────────────────────────────────

		private const string MeshGuid = "adc536c308fe97a1b6dd7ffc8b521562";
		private const string MaterialGuid = "1074c48c292c5821cb6c71872ef52a45";

		/// <summary>
		/// A boulder-shaped prefab as Unity writes one: a root with a LODGroup and a collider, two LOD
		/// children each with a filter and renderer. <paramref name="ids"/> are the nine object IDs in a
		/// fixed role order; <paramref name="childrenFirst"/> writes the documents in another order.
		/// </summary>
		private static string PrefabYaml(long[] ids, bool childrenFirst)
		{
			long rootGo = ids[0], rootTf = ids[1], lod = ids[2], col = ids[3];
			long c0Go = ids[4], c0Tf = ids[5], c0Mf = ids[6], c0Mr = ids[7];
			long c1Go = ids[8], c1Tf = ids[9], c1Mf = ids[10], c1Mr = ids[11];
			string Go(long id, string name, params long[] comps)
			{
				var sb = new StringBuilder();
				sb.Append($"--- !u!1 &{id}\nGameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  serializedVersion: 6\n  m_Component:\n");
				foreach (long c in comps) sb.Append($"  - component: {{fileID: {c}}}\n");
				sb.Append($"  m_Layer: 0\n  m_Name: {name}\n  m_IsActive: 1\n");
				return sb.ToString();
			}
			string Tf(long id, long go, long father, params long[] children)
			{
				var sb = new StringBuilder();
				sb.Append($"--- !u!4 &{id}\nTransform:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {go}}}\n  serializedVersion: 2\n");
				if (children.Length == 0) sb.Append("  m_Children: []\n");
				else
				{
					sb.Append("  m_Children:\n");
					foreach (long c in children) sb.Append($"  - {{fileID: {c}}}\n");
				}
				sb.Append($"  m_Father: {{fileID: {father}}}\n");
				return sb.ToString();
			}
			string Mf(long id, long go) => $"--- !u!33 &{id}\nMeshFilter:\n  m_GameObject: {{fileID: {go}}}\n  m_Mesh: {{fileID: 4300000, guid: {MeshGuid}, type: 2}}\n";
			string Mr(long id, long go) => $"--- !u!23 &{id}\nMeshRenderer:\n  m_GameObject: {{fileID: {go}}}\n  m_Materials:\n  - {{fileID: 2100000, guid: {MaterialGuid}, type: 2}}\n  m_ProbeAnchor: {{fileID: 0}}\n";
			string root = Go(rootGo, "Boulder_Test", rootTf, lod, col)
				+ Tf(rootTf, rootGo, 0, c0Tf, c1Tf)
				+ $"--- !u!205 &{lod}\nLODGroup:\n  m_GameObject: {{fileID: {rootGo}}}\n  m_LODs:\n  - screenRelativeHeight: 0.25\n    renderers:\n    - renderer: {{fileID: {c0Mr}}}\n  - screenRelativeHeight: 0.08\n    renderers:\n    - renderer: {{fileID: {c1Mr}}}\n"
				+ $"--- !u!136 &{col}\nCapsuleCollider:\n  m_GameObject: {{fileID: {rootGo}}}\n  m_Material: {{fileID: 0}}\n";
			string child0 = Go(c0Go, "LOD0", c0Tf, c0Mf, c0Mr) + Tf(c0Tf, c0Go, rootTf) + Mf(c0Mf, c0Go) + Mr(c0Mr, c0Go);
			string child1 = Go(c1Go, "LOD1", c1Tf, c1Mf, c1Mr) + Tf(c1Tf, c1Go, rootTf) + Mf(c1Mf, c1Go) + Mr(c1Mr, c1Go);
			const string preamble = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";
			return preamble + (childrenFirst ? child1 + child0 + root : root + child0 + child1);
		}

		private static readonly long[] RandomIdsA =
		{
			7012149556083126181, 9168930450829113901, 1952516420435422679, 7004195821554288612,
			4005955826365388200, 9119876477048102023, 5086769501186432902, 7535657697182593422,
			414625055326816859, 5269909905201933896, 3803687233936622642, 3844527747270689936,
		};

		private static readonly long[] RandomIdsB =
		{
			123456789012345, 2222222222222222, 3333333333333333, 4444444444444444,
			5555555555555555, 6666666666666666, 7777777777777777, 8888888888888888,
			1111111111111111, 1212121212121212, 1313131313131313, 1414141414141414,
		};

		private static readonly Regex HeaderId = new Regex(@"^--- !u!\d+ &(-?\d+)", RegexOptions.Multiline);
		private static readonly Regex Local = new Regex(@"\{fileID: (-?\d+)\}");

		[Test]
		public void PrefabFileIds_AreAFunctionOfTheHierarchy_NotOfUnitysRandomIds()
		{
			string path = ProceduralArtCatalogue.PrefabPath("Boulder_Test");
			Assert.That(ProceduralArtFileIds.TryRewrite(path, PrefabYaml(RandomIdsA, false), out string a, out Dictionary<long, long> remapA, out string errorA), Is.True, errorA);
			Assert.That(ProceduralArtFileIds.TryRewrite(path, PrefabYaml(RandomIdsB, true), out string b, out _, out string errorB), Is.True, errorB);
			Assert.That(b, Is.EqualTo(a), "the same hierarchy saved with other random IDs, in another order, gives byte-identical YAML");

			Assert.That(ProceduralArtFileIds.TryRewrite(path, a, out string again, out Dictionary<long, long> remapAgain, out _), Is.True);
			Assert.That(again, Is.EqualTo(a), "rewriting an already deterministic prefab changes nothing (re-saving unchanged art is byte-identical)");
			foreach (KeyValuePair<long, long> pair in remapAgain)
			{
				Assert.That(pair.Value, Is.EqualTo(pair.Key));
			}

			// What terrain data references: the root GameObject, by an ID computable from the path alone.
			long root = remapA[RandomIdsA[0]];
			Assert.That(root, Is.EqualTo(ProceduralArtFileIds.IdFor(path, ProceduralArtFileIds.RootKey)));
			Assert.That(ProceduralArtFileIds.RootGameObjectId(a), Is.EqualTo(root));
			Assert.That(ProceduralArtFileIds.RootGameObjectId(PrefabYaml(RandomIdsA, false)), Is.EqualTo(RandomIdsA[0]), "the root is the GameObject whose transform has no father");

			// A different prefab path numbers the same hierarchy differently.
			Assert.That(ProceduralArtFileIds.TryRewrite(ProceduralArtCatalogue.PrefabPath("Other"), PrefabYaml(RandomIdsA, false), out string other, out _, out _), Is.True);
			Assert.That(other, Is.Not.EqualTo(a));
		}

		[Test]
		public void PrefabFileIdRewrite_KeepsEveryReferenceConsistent()
		{
			string path = ProceduralArtCatalogue.PrefabPath("Boulder_Test");
			string original = PrefabYaml(RandomIdsA, false);
			Assert.That(ProceduralArtFileIds.TryRewrite(path, original, out string rewritten, out Dictionary<long, long> remap, out string error), Is.True, error);

			var declared = new HashSet<long>();
			foreach (Match m in HeaderId.Matches(rewritten))
			{
				long id = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
				Assert.That(declared.Add(id), Is.True, $"{id} declared twice");
				Assert.That(id, Is.GreaterThanOrEqualTo(1L << 62), "far above every ID Unity reserves");
			}
			Assert.That(declared.Count, Is.EqualTo(RandomIdsA.Length));
			foreach (Match m in Local.Matches(rewritten))
			{
				long id = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
				Assert.That(id == 0 || declared.Contains(id), Is.True, $"a local reference to {id} points at no object");
			}
			foreach (long old in RandomIdsA)
			{
				Assert.That(rewritten, Does.Not.Contain(old.ToString(CultureInfo.InvariantCulture)), "no old ID survives");
				Assert.That(remap.ContainsKey(old), Is.True);
			}
			// References out of the file keep their GUID and fixed main IDs.
			Assert.That(rewritten, Does.Contain($"{{fileID: 4300000, guid: {MeshGuid}, type: 2}}"));
			Assert.That(rewritten, Does.Contain($"{{fileID: 2100000, guid: {MaterialGuid}, type: 2}}"));

			// The LODGroup still names LOD0's renderer.
			long lod0Renderer = remap[RandomIdsA[7]];
			Assert.That(rewritten, Does.Contain($"- renderer: {{fileID: {lod0Renderer}}}"));
			Assert.That(rewritten, Does.Contain($"--- !u!23 &{lod0Renderer}\nMeshRenderer:"));
		}

		[Test]
		public void PrefabFileIdRewrite_RefusesWhatItCannotNumber()
		{
			string path = ProceduralArtCatalogue.PrefabPath("Nested");
			string nested = PrefabYaml(RandomIdsA, false) + "--- !u!4 &999 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: 1, guid: " + MeshGuid + ", type: 3}\n";
			Assert.That(ProceduralArtFileIds.TryRewrite(path, nested, out string rewritten, out _, out string error), Is.False);
			Assert.That(rewritten, Is.Null);
			Assert.That(error, Does.Contain("stripped"));
			Assert.That(ProceduralArtFileIds.TryRewrite(path, "%YAML 1.1\n", out _, out _, out _), Is.False);
		}

		// ── Migration ─────────────────────────────────────────────────

		[Test]
		public void BinaryGuid_SwapsEachBytesNibbles()
		{
			byte[] b = ProceduralArtMigration.BinaryGuid("0123456789abcdef0123456789abcdef");
			Assert.That(b.Length, Is.EqualTo(16));
			Assert.That(b[0], Is.EqualTo(0x10));
			Assert.That(b[1], Is.EqualTo(0x32));
			Assert.That(b[7], Is.EqualTo(0xfe));
		}

		private static void WriteFile(string root, string rel, string text)
		{
			string full = Path.Combine(root, rel);
			Directory.CreateDirectory(Path.GetDirectoryName(full));
			File.WriteAllText(full, text);
		}

		[Test]
		public void Migration_ReaddressesGeneratedArt_AndRewritesEveryReference_OnATempCopy()
		{
			string project = TempProject();
			const string oldPrefabGuid = "aaa7de4256146514d823ea79e676c2d6";
			const string oldMaterialGuid = "1074c48c292c5821cb6c71872ef52a45";
			const string oldLayerGuid = "00c7fda8fbfcf96649caa956539267ef";
			string prefabPath = ProceduralArtCatalogue.PrefabPath("Boulder_Test");
			string materialPath = ProceduralArtCatalogue.MaterialPath("Rock_Test");
			string layerPath = BiomeTerrainLayers.BuiltFolder + "/Grass fdc4e743.terrainlayer";

			// Old-era generated files: random GUIDs, and a prefab with random object IDs.
			WriteFile(project, prefabPath, PrefabYaml(RandomIdsA, false).Replace(MaterialGuid, oldMaterialGuid));
			WriteFile(project, prefabPath + ".meta", "fileFormatVersion: 2\nguid: " + oldPrefabGuid + "\nlabels:\n- FishMMOProceduralArt\nPrefabImporter:\n  externalObjects: {}\n");
			WriteFile(project, materialPath, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  m_Name: Rock_Test\n");
			WriteFile(project, materialPath + ".meta", "fileFormatVersion: 2\nguid: " + oldMaterialGuid + "\nNativeFormatImporter:\n  mainObjectFileID: 2100000\n");
			WriteFile(project, layerPath, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1953259897 &8574412962073106934\nTerrainLayer:\n  m_Name: Grass\n");
			WriteFile(project, layerPath + ".meta", "fileFormatVersion: 2\nguid: " + oldLayerGuid + "\nNativeFormatImporter:\n  mainObjectFileID: 8574412962073106934\n");

			// Committed references: a text scene, a binary terrain tile, and a LOCAL file that must not be touched.
			long oldRoot = RandomIdsA[0];
			string scene = "%YAML 1.1\n--- !u!114 &1\nMonoBehaviour:\n"
				+ $"  tree: {{fileID: {oldRoot}, guid: {oldPrefabGuid}, type: 3}}\n"
				+ $"  rock: {{fileID: 2100000, guid: {oldMaterialGuid}, type: 2}}\n"
				+ $"  - Layer: {{fileID: 8574412962073106934, guid: {oldLayerGuid}, type: 2}}\n"
				+ "  other: {fileID: 11500000, guid: 0123456789abcdef0123456789abcdef, type: 3}\n";
			WriteFile(project, "Assets/Scenes/Test.unity", scene);
			var binary = new List<byte> { 0, 0, 0, 0, 0x16, 0, 0, 0 };
			binary.AddRange(ProceduralArtMigration.BinaryGuid(oldLayerGuid));
			binary.AddRange(new byte[] { 0, 0, 0, 0 });
			binary.AddRange(ProceduralArtMigration.BinaryGuid(oldPrefabGuid));
			binary.AddRange(BitConverter.GetBytes(oldRoot));
			binary.AddRange(BitConverter.GetBytes(12345L));
			string tile = Path.Combine(project, "Assets/Scenes/Test Terrain/Tile 0_0.asset");
			Directory.CreateDirectory(Path.GetDirectoryName(tile));
			File.WriteAllBytes(tile, binary.ToArray());
			string localText = $"%YAML 1.1\n--- !u!114 &1\nMonoBehaviour:\n  layer: {{fileID: 8574412962073106934, guid: {oldLayerGuid}, type: 2}}\n";
			WriteFile(project, "Assets/LOCAL/Biomes/Overrides/Test LOCAL Art.asset", localText);

			Assert.That(ProceduralArtMigration.NeedsMigration(project), Is.True);
			ProceduralArtMigration.Plan plan = ProceduralArtMigration.BuildPlan(project);
			Assert.That(plan.Problems, Is.Empty);
			Assert.That(plan.Assets.Count, Is.EqualTo(3));

			var local = new List<string>();
			List<ProceduralArtMigration.Edit> edits = ProceduralArtMigration.ComputeEdits(plan, project, local);
			string backup = Path.Combine(project, "Library/FishMMO/DeterminismMigrationBackup/test");
			ProceduralArtMigration.Backup(edits, project, backup);
			ProceduralArtMigration.Write(edits, project);

			string newPrefab = ProceduralArtPayload.GuidFor(prefabPath), newMaterial = ProceduralArtPayload.GuidFor(materialPath), newLayer = ProceduralArtPayload.GuidFor(layerPath);
			long newRoot = ProceduralArtFileIds.IdFor(prefabPath, ProceduralArtFileIds.RootKey);
			Assert.That(File.ReadAllText(Path.Combine(project, prefabPath + ".meta")), Does.Contain("guid: " + newPrefab + "\n").And.Contain("FishMMOProceduralArt"), "the .meta keeps everything but its GUID");
			Assert.That(File.ReadAllText(Path.Combine(project, materialPath + ".meta")), Does.Contain("guid: " + newMaterial + "\n"));
			Assert.That(File.ReadAllText(Path.Combine(project, layerPath + ".meta")), Does.Contain("guid: " + newLayer + "\n"));

			string prefabNow = File.ReadAllText(Path.Combine(project, prefabPath));
			Assert.That(ProceduralArtFileIds.RootGameObjectId(prefabNow), Is.EqualTo(newRoot), "the prefab's objects were renumbered");
			Assert.That(prefabNow, Does.Contain("guid: " + newMaterial), "a wrapper's reference to a re-addressed wrapper follows it");

			string sceneNow = File.ReadAllText(Path.Combine(project, "Assets/Scenes/Test.unity"));
			Assert.That(sceneNow, Does.Contain($"tree: {{fileID: {newRoot}, guid: {newPrefab}, type: 3}}"));
			Assert.That(sceneNow, Does.Contain($"rock: {{fileID: 2100000, guid: {newMaterial}, type: 2}}"));
			Assert.That(sceneNow, Does.Contain($"Layer: {{fileID: 8574412962073106934, guid: {newLayer}, type: 2}}"));
			Assert.That(sceneNow, Does.Contain("guid: 0123456789abcdef0123456789abcdef"), "unrelated references are left alone");

			byte[] tileNow = File.ReadAllBytes(tile);
			Assert.That(Contains(tileNow, ProceduralArtMigration.BinaryGuid(newLayer)), Is.True);
			Assert.That(Contains(tileNow, ProceduralArtMigration.BinaryGuid(newPrefab)), Is.True);
			Assert.That(Contains(tileNow, BitConverter.GetBytes(newRoot)), Is.True, "a binary prototype's object ID follows its prefab");
			Assert.That(Contains(tileNow, ProceduralArtMigration.BinaryGuid(oldLayerGuid)), Is.False);
			Assert.That(Contains(tileNow, BitConverter.GetBytes(12345L)), Is.True, "small numbers are never touched");
			Assert.That(tileNow.Length, Is.EqualTo(binary.Count));

			Assert.That(File.ReadAllText(Path.Combine(project, "Assets/LOCAL/Biomes/Overrides/Test LOCAL Art.asset")), Is.EqualTo(localText), "Assets/LOCAL is never rewritten");
			Assert.That(local.Count, Is.EqualTo(1), "but it is reported");

			Assert.That(File.ReadAllText(Path.Combine(backup, "Assets/Scenes/Test.unity")), Is.EqualTo(scene), "every rewritten file was backed up first");
			Assert.That(File.Exists(Path.Combine(backup, prefabPath + ".meta")), Is.True);

			Assert.That(ProceduralArtMigration.NeedsMigration(project), Is.False);
			Assert.That(ProceduralArtMigration.BuildPlan(project).Empty, Is.True, "a second run changes nothing");
		}

		private static bool Contains(byte[] data, byte[] pattern)
		{
			for (int i = 0; i + pattern.Length <= data.Length; i++)
			{
				int j = 0;
				while (j < pattern.Length && data[i + j] == pattern[j])
				{
					j++;
				}
				if (j == pattern.Length)
				{
					return true;
				}
			}
			return false;
		}

		[Test]
		public void TheScan_FindsAGuidInABinaryFile_OnlyWhenAsked()
		{
			string root = TempProject();
			const string target = "0123456789abcdef0123456789abcdef";
			var bytes = new List<byte> { 0, 1, 2, 3 };
			bytes.AddRange(ProceduralArtMigration.BinaryGuid(target));
			File.WriteAllBytes(root + "/Tile.asset", bytes.ToArray());
			Assert.That(AssetGuidScan.FilesReferencing(new[] { target }, null, root).Count, Is.EqualTo(0));
			Assert.That(AssetGuidScan.FilesReferencing(new[] { target }, null, root, binary: true).Keys, Does.Contain(root + "/Tile.asset"));
		}

		// ── Built terrain layers ──────────────────────────────────────

		private Texture2D MakeTexture(string name) => Track(new Texture2D(4, 4) { name = name });

		[Test]
		public void SwatchesAndLocalTextures_AreNoArt_SoTheyFallThroughToThePlaceholder()
		{
			Texture2D swatch = MakeTexture("BiomeTexture_AlpineMeadow_80CC4C");
			Texture2D local = MakeTexture("LOCAL photo");
			Texture2D committed = MakeTexture("Grass");
			bool IsLocal(Object o) => o != null && o.name.StartsWith("LOCAL", StringComparison.Ordinal);

			Assert.That(BiomeTerrainLayers.IsArt(swatch, IsLocal), Is.False);
			Assert.That(BiomeTerrainLayers.IsArt(local, IsLocal), Is.False);
			Assert.That(BiomeTerrainLayers.IsArt(null, IsLocal), Is.False);
			Assert.That(BiomeTerrainLayers.IsArt(committed, IsLocal), Is.True);

			Assert.That(BiomeTerrainLayers.Resolve(new TerrainTextureLayer { albedoTexture = swatch }, IsLocal), Is.Null, "a swatch slot draws the placeholder");
			Assert.That(BiomeTerrainLayers.Resolve(new TerrainTextureLayer { albedoTexture = local }, IsLocal), Is.Null, "a LOCAL slot draws the placeholder (LOCAL reaches the arrays only)");

			// A layer whose own picture is a swatch or LOCAL is skipped the same way.
			TerrainLayer swatchLayer = Track(new TerrainLayer { diffuseTexture = swatch });
			TerrainLayer localLayer = Track(new TerrainLayer { diffuseTexture = local });
			Assert.That(BiomeTerrainLayers.Resolve(new TerrainTextureLayer { terrainLayer = swatchLayer, albedoTexture = swatch }, IsLocal), Is.Null);
			Assert.That(BiomeTerrainLayers.Resolve(new TerrainTextureLayer { terrainLayer = localLayer }, IsLocal), Is.Null);

			TerrainLayer real = Track(new TerrainLayer { diffuseTexture = committed });
			Assert.That(BiomeTerrainLayers.Resolve(new TerrainTextureLayer { terrainLayer = real, albedoTexture = swatch }, IsLocal), Is.SameAs(real));
		}

		[Test]
		public void BuiltLayerPaths_AreAFunctionOfTheirContent()
		{
			string a = BiomeTerrainLayers.BuiltPath("Grass", "g1|-|-|4,4|0|0.2");
			Assert.That(a, Does.StartWith(BiomeTerrainLayers.BuiltFolder + "/Grass "));
			Assert.That(a, Does.EndWith(".terrainlayer"));
			Assert.That(BiomeTerrainLayers.BuiltPath("Grass", "g1|-|-|4,4|0|0.2"), Is.EqualTo(a));
			Assert.That(BiomeTerrainLayers.BuiltPath("Grass", "g1|-|-|8,8|0|0.2"), Is.Not.EqualTo(a));
			Assert.That(ProceduralArtPayload.IsGenerated(a), Is.True);
		}

		// ── Authoring never counts generated art as authored ─────────

		[Test]
		public void AGeneratedLayerOfAnotherFamily_IsRefilled_ButAuthoredArtIsNot()
		{
			TerrainLayer spec = Track(new TerrainLayer { name = "Ground_Grass" });
			TerrainLayer oldGenerated = Track(new TerrainLayer { name = "Ground_Meadow" });
			TerrainLayer authored = Track(new TerrainLayer { name = "Hand painted" });
			bool IsGenerated(Object o) => o != null && o.name.StartsWith("Ground_", StringComparison.Ordinal);

			Assert.That(BiomeArtAuthoring.IsFillable(new TerrainTextureLayer(), spec, IsGenerated), Is.True, "empty");
			Assert.That(BiomeArtAuthoring.IsFillable(new TerrainTextureLayer { terrainLayer = oldGenerated }, spec, IsGenerated), Is.True, "generated, and the spec has moved on");
			Assert.That(BiomeArtAuthoring.IsFillable(new TerrainTextureLayer { terrainLayer = spec }, spec, IsGenerated), Is.False, "already the spec's: nothing to do");
			Assert.That(BiomeArtAuthoring.IsFillable(new TerrainTextureLayer { terrainLayer = authored }, spec, IsGenerated), Is.False, "authored art is never replaced");
		}

		[Test]
		public void ARuleOfGeneratedPrefabsOnly_IsTheSpecs_NotAuthored()
		{
			GameObject generated = Track(new GameObject("Tree_Oak"));
			GameObject handMade = Track(new GameObject("My tree"));
			bool IsGenerated(Object o) => o != null && o.name.StartsWith("Tree_", StringComparison.Ordinal);
			var specRule = new PrefabSpawnRule { enableSpawning = true, prefabs = new[] { generated } };
			var authoredRule = new PrefabSpawnRule { enableSpawning = true, prefabs = new[] { generated, handMade } };

			Assert.That(BiomeArtAuthoring.IsGeneratedRule(specRule, IsGenerated), Is.True);
			Assert.That(BiomeArtAuthoring.IsGeneratedRule(authoredRule, IsGenerated), Is.False);
			Assert.That(BiomeArtAuthoring.HasNoAuthoredRule(new List<PrefabSpawnRule> { specRule }, IsGenerated), Is.True);
			Assert.That(BiomeArtAuthoring.HasNoAuthoredRule(new List<PrefabSpawnRule> { specRule, authoredRule }, IsGenerated), Is.False);
			Assert.That(BiomeArtAuthoring.HasNoLiveRule(new List<PrefabSpawnRule> { specRule }), Is.False, "the old question still sees it as live");
		}

		// ── The guard ─────────────────────────────────────────────────

		/// <summary>
		/// No file that will be committed names a generated asset by a GUID other than the one derived
		/// from its path — in text or, for binary terrain data, in bytes. Such a reference resolves only on
		/// the machine that made the asset; every clone regenerates it under the derived GUID. Fails on a
		/// tree that has not yet run Dashboard → Biome Tools → "Migrate generated art to deterministic IDs"
		/// (the generator also runs it by itself), which is exactly when it should.
		/// </summary>
		[Test]
		public void NoCommittedAssetReferencesAGeneratedAssetByANonDerivedGuid()
		{
			var stale = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (string root in ProceduralArtPayload.GeneratedRoots)
			{
				if (!Directory.Exists(root))
				{
					continue;
				}
				foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
				{
					string path = file.Replace('\\', '/');
					if (path.EndsWith(".meta", StringComparison.Ordinal))
					{
						continue;
					}
					string guid = ProceduralArtPayload.GuidOnDisk(path);
					if (guid != null && guid != ProceduralArtPayload.GuidFor(path))
					{
						stale[guid] = path;
					}
				}
			}
			if (stale.Count == 0)
			{
				Assert.Pass("Every generated asset on this machine has its derived GUID.");
			}

			SortedDictionary<string, SortedSet<string>> offenders = AssetGuidScan.FilesReferencing(stale.Keys, AssetGuidScan.IsMachineLocal, binary: true);
			if (offenders.Count > 0)
			{
				var sb = new StringBuilder();
				sb.AppendLine($"{offenders.Count} committed file(s) reference generated art by a GUID that is not derived from its path, so the reference breaks on every other clone. Run Dashboard → Biome Tools → Migrate generated art to deterministic IDs:");
				foreach (KeyValuePair<string, SortedSet<string>> file in offenders)
				{
					var targets = new List<string>();
					foreach (string guid in file.Value)
					{
						targets.Add(stale[guid]);
					}
					sb.AppendLine($"  {file.Key} → {string.Join(", ", targets)}");
				}
				Assert.Fail(sb.ToString());
			}
		}
	}
}
