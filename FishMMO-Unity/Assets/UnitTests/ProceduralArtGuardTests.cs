using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The guards added after every generated material and terrain layer was written with empty
	/// texture slots (2026-10-02: grey slab trees, flat ground): the empty-slot rule and its YAML scan,
	/// the payload textures imported as 2D textures, the verification step before any wrapper, every
	/// asset-editing batch closed in a finally, and the vegetation fades in every shader pass.
	/// </summary>
	[TestFixture]
	public class ProceduralArtGuardTests
	{
		private const string ArtFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/ProceduralArt/";
		private const string ShaderFolder = "Assets/Prefabs/Client/Weather/Shaders/";

		// ── The empty-slot rule ───────────────────────────────────────

		private const string MaterialHead = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n  m_Name: Rock_Grey\n";

		private static string MaterialYaml(string keywords, string baseMap, string bumpMap)
		{
			return MaterialHead + keywords +
				"  m_InvalidKeywords: []\n" +
				"  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs:\n" +
				"    - _BaseMap:\n        m_Texture: " + baseMap + "\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n" +
				"    - _BumpMap:\n        m_Texture: " + bumpMap + "\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n" +
				"    m_Floats:\n    - _UseNormalMap: 1\n    m_Colors:\n    - _BaseColor: {r: 1, g: 1, b: 1, a: 1}\n";
		}

		private const string Texture = "{fileID: 2800000, guid: ac99cea88a05f90f6a6644cd707402cf, type: 3}";

		[Test]
		public void MaterialYaml_AnEmptyBaseMap_IsMissing_WhateverTheKeywords()
		{
			Assert.That(ProceduralArtWrapperCheck.MissingInMaterialYaml(MaterialYaml("  m_ValidKeywords: []\n", "{fileID: 0}", "{fileID: 0}")),
				Is.EqualTo(new[] { "_BaseMap" }), "what 2026-10-02's materials looked like");
			Assert.That(ProceduralArtWrapperCheck.MissingInMaterialYaml(MaterialYaml("  m_ValidKeywords: []\n", Texture, "{fileID: 0}")),
				Is.Empty, "no normal-map keyword: an empty _BumpMap is not sampled");
		}

		[Test]
		public void MaterialYaml_AKeywordsMap_IsRequired()
		{
			string keywords = "  m_ValidKeywords:\n  - _METALLICSPECGLOSSMAP\n  - _NORMALMAP\n";
			Assert.That(ProceduralArtWrapperCheck.MissingInMaterialYaml(MaterialYaml(keywords, Texture, "{fileID: 0}")),
				Is.EqualTo(new[] { "_BumpMap", "_MetallicGlossMap" }), "the normal map is empty and the metallic map absent");
			Assert.That(ProceduralArtWrapperCheck.MissingInMaterialYaml(MaterialYaml("  m_ValidKeywords:\n  - _NORMALMAP\n", Texture, Texture)), Is.Empty);
		}

		[Test]
		public void TerrainLayerYaml_NeedsAllThreeMaps()
		{
			string empty = "--- !u!1953259897 &8574412962073106934\nTerrainLayer:\n  m_Name: Ground_Lichen\n  m_DiffuseTexture: {fileID: 0}\n  m_NormalMapTexture: {fileID: 0}\n  m_MaskMapTexture: {fileID: 0}\n  m_TileSize: {x: 3, y: 3}\n";
			Assert.That(ProceduralArtWrapperCheck.MissingInTerrainLayerYaml(empty), Is.EqualTo(ProceduralArtWrapperCheck.TerrainLayerSlots));
			string full = empty.Replace("{fileID: 0}", Texture);
			Assert.That(ProceduralArtWrapperCheck.MissingInTerrainLayerYaml(full), Is.Empty);
			Assert.That(ProceduralArtWrapperCheck.MissingSlots("Assets/X/Ground_Lichen.terrainlayer", empty), Has.Count.EqualTo(3), "picked by extension");
			Assert.That(ProceduralArtWrapperCheck.MissingSlots("Assets/X/Tree_Oak.prefab", empty), Is.Empty, "prefabs are not this check's");
		}

		[Test]
		public void AFreshMaterial_WithANormalMapKeywordAndNoMap_IsRefused()
		{
			Shader shader = Shader.Find("FishMMO/Vegetation");
			Assume.That(shader, Is.Not.Null, "the vegetation shader is in the project");
			var material = new Material(shader);
			try
			{
				material.EnableKeyword("_NORMALMAP");
				Assert.That(ProceduralArtWrapperCheck.MissingInMaterial(material), Is.EqualTo(new[] { "_BaseMap", "_BumpMap" }));
				material.SetTexture("_BaseMap", Texture2D.whiteTexture);
				material.SetTexture("_BumpMap", Texture2D.normalTexture);
				Assert.That(ProceduralArtWrapperCheck.MissingInMaterial(material), Is.Empty);
			}
			finally
			{
				Object.DestroyImmediate(material);
			}
		}

		[Test]
		public void TheLabel_IsWrittenIntoEveryMeta_AndReadBackWithoutLoading()
		{
			string guid = ProceduralArtPayload.GuidFor(ProceduralArtCatalogue.MeshPath("Pebbles"));
			Assert.That(ProceduralArtLedger.MetaHasLabel(ProceduralArtPayload.MetaText(guid, 0), ProceduralArtLedger.Label), Is.True);
			Assert.That(ProceduralArtLedger.MetaHasLabel(ProceduralArtPayload.PrefabMetaText(guid), ProceduralArtLedger.Label), Is.True);
			Assert.That(ProceduralArtLedger.MetaHasLabel("fileFormatVersion: 2\nguid: " + guid + "\nlabels:\n- Other\nTextureImporter:\n", ProceduralArtLedger.Label), Is.False);
			Assert.That(ProceduralArtLedger.MetaHasLabel("fileFormatVersion: 2\nguid: " + guid + "\n", ProceduralArtLedger.Label), Is.False);
		}

		// ── The import and the order of the steps ─────────────────────

		[Test]
		public void PayloadTextures_ImportAs2D_NeverInheritingACubeShape()
		{
			Assert.That(new ProceduralArtTextureImport().GetVersion(), Is.GreaterThanOrEqualTo(3u), "version 3 reimports the PNGs version 2 imported as cube maps");
			string code = SourceScanPins.ReadCode(ArtFolder + "ProceduralArtTextureImport.cs");
			SourceScanPins.HoldsAndFires("texture shape", code,
				c => c.Contains("importer.textureShape = TextureImporterShape.Texture2D;") ? null : "the importer no longer sets the 2D shape",
				SourceScanPins.Replace("importer.textureShape = TextureImporterShape.Texture2D;", string.Empty),
				"the shape left to the bare .meta's legacy upgrade, which makes a Cubemap");
		}

		[Test]
		public void TexturesAreVerified_AfterTheImport_AndBeforeAnyWrapper()
		{
			string code = SourceScanPins.ReadCode(ArtFolder + "BiomeArtGenerator.cs");
			SourceScanPins.HoldsAndFires("verification order", code,
				c => SourceScanPins.InOrder(SourceScanPins.Body(c, "private static IEnumerable<string> StepsOf(Context c)"),
					"ImportPendingTextures(c);", "VerifyTextures(c);", "WriteTerrainLayers(c);", "WriteMaterials(c);"),
				SourceScanPins.Replace("\t\t\tVerifyTextures(c);\n", string.Empty),
				"the wrappers built straight after the import, as on 2026-10-02");
		}

		[Test]
		public void EveryAssetEditingBatch_IsClosedInAFinally()
		{
			foreach (string file in new[] { "BiomeArtGenerator.cs", "ProceduralArtMigration.cs", "ProceduralArtPayload.cs" })
			{
				string code = SourceScanPins.ReadCode(ArtFolder + file);
				SourceScanPins.HoldsAndFires(file, code,
					c =>
					{
						int starts = Regex.Matches(c, @"AssetDatabase\.StartAssetEditing\(\);").Count;
						int closed = Regex.Matches(c, @"finally\s*\{\s*AssetDatabase\.StopAssetEditing\(\);").Count;
						int stops = Regex.Matches(c, @"AssetDatabase\.StopAssetEditing\(\);").Count;
						return starts == closed && stops == closed ? null : $"{starts} StartAssetEditing, {stops} StopAssetEditing, {closed} of them in a finally";
					},
					s => s + "\nclass Control { void M() { AssetDatabase.StartAssetEditing(); } }\n",
					"a batch opened and never closed: every later import in the editor waits for it");
			}
		}

		// ── The vegetation shader ─────────────────────────────────────

		[Test]
		public void EveryVegetationPass_DithersTheLodCrossFadeAndTheDistanceFade()
		{
			string shader = SourceScanPins.ReadSource(ShaderFolder + "FishVegetation.shader");
			int passes = Regex.Matches(shader, @"\n\s*Pass\s*\n").Count;
			Assert.That(passes, Is.EqualTo(4), "ForwardLit, ShadowCaster, DepthOnly, DepthNormals");
			Assert.That(Regex.Matches(shader, @"#pragma multi_compile _ LOD_FADE_CROSSFADE").Count, Is.EqualTo(passes), "every pass compiles the LOD cross-fade variant");

			string passesCode = SourceScanPins.ReadSource(ShaderFolder + "FishVegetationPasses.hlsl");
			SourceScanPins.HoldsAndFires("distance fade", passesCode,
				c =>
				{
					int fragments = Regex.Matches(c, @"LODFadeCrossFade\(input\.positionCS\);").Count;
					int fades = Regex.Matches(c, @"VegFadeClip\(input\.positionCS, input\.fade\);").Count;
					return fragments == 3 && fades == 3 ? null : $"{fragments} LOD cross-fade clips and {fades} distance-fade clips; the three fragment functions need both";
				},
				SourceScanPins.RegexReplaceFirst(@"VegFadeClip\(input\.positionCS, input\.fade\);", string.Empty),
				"a pass that does not dissolve: its shadow or depth would outlive the plant");
		}

		// ── The fade bands ────────────────────────────────────────────

		[Test]
		public void TheDetailFade_EndsAPatchDiagonalInsideTheDrawDistance()
		{
			Vector2 band = VegetationDistanceFade.DetailBand(120f, 16f);
			Assert.That(band.y, Is.EqualTo(120f - 16f * Mathf.Sqrt(2f)).Within(0.01f), "97.4 m with the generator's 120 m and 16 m patches");
			Assert.That(band.y - band.x, Is.EqualTo(VegetationDistanceFade.DetailBandMetres).Within(0.001f));
			Assert.That(VegetationDistanceFade.DetailBand(120f, 32f).y, Is.EqualTo(120f - 32f * Mathf.Sqrt(2f)).Within(0.01f), "75 m with the old 32 m patches");

			Vector2 near = VegetationDistanceFade.DetailBand(20f, 32f);
			Assert.That(near.x, Is.GreaterThanOrEqualTo(0f));
			Assert.That(near.y, Is.GreaterThan(near.x), "a short draw distance shortens the band, never inverts it");
			Assert.That(VegetationDistanceFade.DetailBand(0f, 16f), Is.EqualTo(Vector2.zero), "no details: no fade");
			Assert.That(VegetationDistanceFade.PatchMetres(1000f, 1008, 16), Is.EqualTo(1000f / 1008f * 16f).Within(1e-4f));
		}

		[Test]
		public void TheTreeFade_EndsJustInsideTheTreeDistance()
		{
			Vector2 band = VegetationDistanceFade.TreeBand(1500f);
			Assert.That(band.y, Is.EqualTo(1455f).Within(0.01f));
			Assert.That(band.x, Is.EqualTo(1305f).Within(0.01f));
			Vector2 shortBand = VegetationDistanceFade.TreeBand(200f);
			Assert.That(shortBand.y - shortBand.x, Is.EqualTo(VegetationDistanceFade.TreeBandMinMetres).Within(0.01f));
			Assert.That(VegetationDistanceFade.TreeBand(0f), Is.EqualTo(Vector2.zero));
		}

		[Test]
		public void EveryDetail_HasItsOwnMaterial_AndASink()
		{
			var materials = new HashSet<string>(ProceduralArtCatalogue.WrapperPaths());
			foreach (DetailSpec d in ProceduralArtCatalogue.Details)
			{
				DetailSpec spec = d;
				Assert.That(materials, Does.Contain(ProceduralArtCatalogue.MaterialPath(ProceduralArtCatalogue.DetailPrefab(spec.Name))),
					$"{spec.Name}: a detail fades as a detail, so it cannot share a tree's material");
				float sink = ProceduralArtCatalogue.DetailSink(in spec);
				Assert.That(sink, Is.InRange(0.01f, 0.1f), $"{spec.Name} sinks {sink} m");
			}
		}
	}
}
