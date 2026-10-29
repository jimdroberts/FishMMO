using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;
using Kind = FishMMO.Shared.WorldDesign.ProceduralArtLookArrays.Kind;
using Layout = FishMMO.Shared.WorldDesign.ProceduralArtLookArrays.Layout;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The look arrays the art generator writes (2026-10-28, ProceduralArtLookArrays): the props' surface maps as Texture2DArray
	/// flipbooks, the materials naming slices. Pure parts — the sets, their chunks and grids, every map's one slice, the names the
	/// importer reads, the wrapper check's array slots — and source pins on the generator, importer and payload.
	/// </summary>
	[TestFixture]
	public class ProceduralArtLookArraysTests
	{
		private const string Art = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/ProceduralArt/";

		private static void Ordered(string text, params string[] anchors) => Assert.That(SourceScanPins.InOrder(text, anchors), Is.Null);

		[Test]
		public void Grid_HoldsEveryMap_WastesFewest_AndStaysUnderTheLimits()
		{
			Assert.That(ProceduralArtLookArrays.Grid(76, 1024, out int c, out int r), Is.True);
			Assert.That(c * r, Is.EqualTo(77), "76 = 4 × 19, but 19 rows of 1024 pass 16384: one spare cell");
			Assert.That(c * 1024, Is.LessThanOrEqualTo(ProceduralArtLookArrays.MaxSourceSide));
			Assert.That(r * 1024, Is.LessThanOrEqualTo(ProceduralArtLookArrays.MaxSourceSide));
			Assert.That(ProceduralArtLookArrays.Grid(99, 512, out c, out r), Is.True);
			Assert.That(c * r, Is.EqualTo(99), "11 × 9, none spare");
			Assert.That(System.Math.Abs(c - r), Is.EqualTo(2));
			Assert.That(ProceduralArtLookArrays.Grid(9, 512, out c, out r), Is.True);
			Assert.That((c, r), Is.EqualTo((3, 3)), "the squarer grid on a tie");
			Assert.That(ProceduralArtLookArrays.Grid(256, 1024, out c, out r), Is.True);
			Assert.That((c, r), Is.EqualTo((16, 16)));
			Assert.That(ProceduralArtLookArrays.Grid(257, 512, out _, out _), Is.False, "past WebGPU's layer limit");
			Assert.That(ProceduralArtLookArrays.Grid(0, 512, out _, out _), Is.False);
			Assert.That(ProceduralArtLookArrays.ChunkCapacity(1024), Is.EqualTo(256));
			Assert.That(ProceduralArtLookArrays.ChunkCapacity(4096), Is.EqualTo(16), "a 4096² set: a 16384 flipbook holds 4 × 4");
		}

		[Test]
		public void Chunks_KeepASurfaceWhole_AndNoArrayPassesTheLimit()
		{
			// A made-up set of 100 surfaces with four colour maps each: 400 colour maps, two chunks.
			var surfaces = new List<ProceduralArtLookArrays.Surface>();
			for (int i = 0; i < 100; i++)
			{
				surfaces.Add(new ProceduralArtLookArrays.Surface { Name = "S" + i, Colour = new[] { $"S{i}_a", $"S{i}_b", $"S{i}_c", $"S{i}_d" }, Normal = $"S{i}_n", Mask = $"S{i}_m" });
			}
			List<Layout> layouts = ProceduralArtLookArrays.LayoutsOf(new ProceduralArtLookArrays.Set { Name = "Test", Size = 512, Surfaces = surfaces.ToArray() });
			Assert.That(layouts.Select(l => (l.Chunk, l.Kind)), Is.EqualTo(new[] { (0, Kind.Colour), (0, Kind.Normal), (0, Kind.Mask), (1, Kind.Colour), (1, Kind.Normal), (1, Kind.Mask) }));
			Assert.That(layouts[0].Members.Count, Is.EqualTo(256), "64 whole surfaces");
			Assert.That(layouts[1].Members.Count, Is.EqualTo(64));
			Assert.That(layouts[3].Members.Count, Is.EqualTo(144));
			Assert.That(layouts[0].Members[4], Is.EqualTo("S1_a"), "a surface's colour maps are consecutive");
			Assert.That(layouts[3].Members[0], Is.EqualTo("S64_a"));
			Assert.That(layouts[4].Members[0], Is.EqualTo("S64_n"), "a surface's maps share their chunk");
			Assert.That(layouts[0].Path, Does.EndWith("/LookArray_Test_Colour.png"));
			Assert.That(layouts[3].Path, Does.EndWith("/LookArray_Test_Colour_1.png"));
			foreach (Layout l in layouts)
			{
				Assert.That(l.Layers, Is.GreaterThanOrEqualTo(l.Members.Count));
				Assert.That(l.Layers, Is.LessThanOrEqualTo(ProceduralArtLookArrays.MaxLayers));
			}
		}

		[Test]
		public void EverySurfaceMap_HasExactlyOneSlice_InAnArrayOfItsKind()
		{
			var seen = new HashSet<string>();
			foreach (ProceduralArtLookArrays.Set set in ProceduralArtLookArrays.Sets)
			{
				foreach (ProceduralArtLookArrays.Surface surface in set.Surfaces)
				{
					foreach (Kind kind in ProceduralArtLookArrays.Kinds)
					{
						for (int i = 0; i < surface.CountOf(kind); i++)
						{
							string map = surface.MapOf(kind, i);
							Assert.That(seen.Add(map), Is.True, $"{map} is in two arrays");
							Assert.That(ProceduralArtLookArrays.TryGetSlice(map, out ProceduralArtLookArrays.Slice slice), Is.True, map);
							Assert.That(slice.Kind, Is.EqualTo(kind), map);
							Layout layout = ProceduralArtLookArrays.Layouts.Single(l => l.Path == slice.Array);
							Assert.That(layout.Members[slice.Index], Is.EqualTo(map));
							Assert.That(layout.Size, Is.EqualTo(set.Size));
						}
					}
				}
			}
			// The four families the props wear, at the sizes their single maps had.
			Assert.That(ProceduralArtLookArrays.SizeOf(ProceduralArtLookArrays.StructureSetName), Is.EqualTo(StructureSurfaces.Size));
			Assert.That(ProceduralArtLookArrays.SizeOf(ProceduralArtLookArrays.RockSetName), Is.EqualTo(ProceduralArtCatalogue.RockSurfaceSize));
			Assert.That(ProceduralArtLookArrays.SizeOf(ProceduralArtLookArrays.IceSetName), Is.EqualTo(ProceduralArtCatalogue.RockSurfaceSize));
			Assert.That(ProceduralArtLookArrays.SizeOf(ProceduralArtLookArrays.CaveGlowSetName), Is.EqualTo(ProceduralArtCatalogue.CaveGlowTextureSize));
			Assert.That(ProceduralArtLookArrays.TryGetSlice(ProceduralArtCatalogue.StructureTexture("Timber", "Albedo", StructureFinish.Mossy), out _), Is.True);
			Assert.That(ProceduralArtLookArrays.TryGetSlice(RockArtNames.RockSurfaceTexture(RockTypes.All[0].Name, "Mask"), out _), Is.True);
			Assert.That(ProceduralArtLookArrays.TryGetSlice(ProceduralArtCatalogue.CaveGlowEmissionPath, out _), Is.True);
			Assert.That(ProceduralArtLookArrays.TryGetSlice(ProceduralArtCatalogue.GroundTexture("Rock", "Albedo"), out _), Is.False, "the ground keeps its 2D maps");
			foreach (Layout layout in ProceduralArtLookArrays.Layouts)
			{
				Assert.That(layout.Layers, Is.LessThanOrEqualTo(ProceduralArtLookArrays.MaxLayers), layout.Path);
				Assert.That(layout.Width, Is.LessThanOrEqualTo(ProceduralArtLookArrays.MaxSourceSide), layout.Path);
				Assert.That(layout.Height, Is.LessThanOrEqualTo(ProceduralArtLookArrays.MaxSourceSide), layout.Path);
				Assert.That(layout.Layers - layout.Members.Count, Is.LessThan(layout.Columns), $"{layout.Path}: less than a row spare");
			}
		}

		[Test]
		public void Names_ReadBackToTheirSetAndKind_ForTheImporter()
		{
			foreach (Layout layout in ProceduralArtLookArrays.Layouts)
			{
				string file = System.IO.Path.GetFileNameWithoutExtension(layout.Path);
				Assert.That(ProceduralArtLookArrays.TryParseArrayName(file, out string set, out Kind kind), Is.True, file);
				Assert.That(set, Is.EqualTo(layout.Set));
				Assert.That(kind, Is.EqualTo(layout.Kind));
			}
			Assert.That(ProceduralArtLookArrays.TryParseArrayName("LookArray_Structure_Normal_2", out string s2, out Kind k2), Is.True);
			Assert.That((s2, k2), Is.EqualTo(("Structure", Kind.Normal)));
			Assert.That(ProceduralArtLookArrays.TryParseArrayName("Structure_Timber_Albedo", out _, out _), Is.False);
			Assert.That(ProceduralArtLookArrays.TryParseArrayName("LookArray_Structure_Albedo", out _, out _), Is.False, "no such kind");
		}

		[Test]
		public void Cells_RunLeftToRightTopToBottom_InABottomRowFirstImage()
		{
			var layout = new Layout { Size = 4, Columns = 3, Rows = 2 };
			ProceduralArtLookArrays.CellOrigin(layout, 0, out int x, out int y);
			Assert.That((x, y), Is.EqualTo((0, 4)), "slice 0: the top-left cell, the image's top row");
			ProceduralArtLookArrays.CellOrigin(layout, 2, out x, out y);
			Assert.That((x, y), Is.EqualTo((8, 4)));
			ProceduralArtLookArrays.CellOrigin(layout, 3, out x, out y);
			Assert.That((x, y), Is.EqualTo((0, 0)), "slice 3: the second row from the top, the image's bottom");
		}

		[Test]
		public void Slots_MapToTheirArraysAndSliceComponents()
		{
			Assert.That(ProceduralArtLookArrays.SlotOf("_BaseMap"), Is.EqualTo(0));
			Assert.That(ProceduralArtLookArrays.SlotOf("_BumpMap"), Is.EqualTo(1));
			Assert.That(ProceduralArtLookArrays.SlotOf("_MetallicGlossMap"), Is.EqualTo(2));
			Assert.That(ProceduralArtLookArrays.SlotOf("_SpecGlossMap"), Is.EqualTo(2));
			Assert.That(ProceduralArtLookArrays.SlotOf("_OcclusionMap"), Is.EqualTo(3));
			Assert.That(ProceduralArtLookArrays.SlotOf("_EmissionMap"), Is.EqualTo(4));
			Assert.That(ProceduralArtLookArrays.SlotOf("_MainTex"), Is.EqualTo(-1));
			Assert.That(ProceduralArtLookArrays.KindOfSlot(0), Is.EqualTo(Kind.Colour));
			Assert.That(ProceduralArtLookArrays.KindOfSlot(4), Is.EqualTo(Kind.Colour), "emission is sRGB, as albedo");
			Assert.That(ProceduralArtLookArrays.KindOfSlot(1), Is.EqualTo(Kind.Normal));
			Assert.That(ProceduralArtLookArrays.KindOfSlot(2), Is.EqualTo(Kind.Mask));
			Assert.That(ProceduralArtLookArrays.KindOfSlot(3), Is.EqualTo(Kind.Mask));
			Assert.That(ProceduralArtLookArrays.SlotFilled(0, true, new Vector4(3f, -1f, -1f, -1f), -1f), Is.True);
			Assert.That(ProceduralArtLookArrays.SlotFilled(1, true, new Vector4(3f, -1f, -1f, -1f), -1f), Is.False);
			Assert.That(ProceduralArtLookArrays.SlotFilled(0, false, new Vector4(3f, -1f, -1f, -1f), -1f), Is.False, "no array bound");
			Assert.That(ProceduralArtLookArrays.SlotFilled(4, true, Vector4.zero, 2f), Is.True);
		}

		private const string ArrayBackedMaterial = @"%YAML 1.1
--- !u!21 &2100000
Material:
  m_Name: Structure_Timber_8A5A34FF
  m_ValidKeywords:
  - _FISH_LOOK_ARRAY
  - _METALLICSPECGLOSSMAP
  - _NORMALMAP
  - _OCCLUSIONMAP
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _BaseMap:
        m_Texture: {fileID: 0}
        m_Scale: {x: 0.33333334, y: 0.33333334}
        m_Offset: {x: 0, y: 0}
    - _BumpMap:
        m_Texture: {fileID: 0}
    - _FishLookColour:
        m_Texture: {fileID: 18700000, guid: 0123456789abcdef0123456789abcdef, type: 3}
    - _FishLookMask:
        m_Texture: {fileID: 18700000, guid: 1123456789abcdef0123456789abcdef, type: 3}
    - _FishLookNormal:
        m_Texture: {fileID: 18700000, guid: 2123456789abcdef0123456789abcdef, type: 3}
    - _MetallicGlossMap:
        m_Texture: {fileID: 0}
    - _OcclusionMap:
        m_Texture: {fileID: 0}
    m_Floats:
    - _FishLookEmissionSlice: -1
    m_Colors:
    - _BaseColor: {r: 0.5, g: 0.4, b: 0.3, a: 1}
    - _FishLookSlices: {r: 12, g: 3, b: 3, a: 3}
";

		[Test]
		public void WrapperCheck_CountsAnArraySliceAsAFilledSlot_AndAnEmptyOneAsMissing()
		{
			Assert.That(ProceduralArtWrapperCheck.MissingInMaterialYaml(ArrayBackedMaterial), Is.Empty);
			// Its normal slice lost: the normal map would be the shader's flat default.
			string noNormal = ArrayBackedMaterial.Replace("{r: 12, g: 3, b: 3, a: 3}", "{r: 12, g: -1, b: 3, a: 3}");
			Assert.That(ProceduralArtWrapperCheck.MissingInMaterialYaml(noNormal), Is.EqualTo(new[] { "_BumpMap" }));
			// Its colour array unbound: the albedo slice names nothing.
			string noArray = ArrayBackedMaterial.Replace("_FishLookColour:\n        m_Texture: {fileID: 18700000, guid: 0123456789abcdef0123456789abcdef, type: 3}", "_FishLookColour:\n        m_Texture: {fileID: 0}");
			Assert.That(noArray, Is.Not.EqualTo(ArrayBackedMaterial), "control: the mutation applies");
			Assert.That(ProceduralArtWrapperCheck.MissingInMaterialYaml(noArray), Is.EqualTo(new[] { "_BaseMap" }));
			// The keyword off: the slices mean nothing and the empty 2D slots are missing, as before.
			string noKeyword = ArrayBackedMaterial.Replace("  - _FISH_LOOK_ARRAY\n", string.Empty);
			Assert.That(ProceduralArtWrapperCheck.MissingInMaterialYaml(noKeyword), Is.EqualTo(new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" }));
		}

		[Test]
		public void Generator_WritesArraysNotSingles_AndBindsSlices()
		{
			string surfaces = SourceScanPins.ReadCode(Art + "BiomeArtGenerator.Structures.cs");
			Ordered(SourceScanPins.Body(surfaces, "private static void WriteStructureSurfaces(Context c)"),
				"bool due = LookArraysDue(c, ProceduralArtLookArrays.StructureSetName);", "PutSurfaceMap(c, albedo, maps.Albedo, maps.Size);", "WriteLookArrays(c, ProceduralArtLookArrays.StructureSetName);");
			StringAssert.DoesNotContain("WritePng(", SourceScanPins.Body(surfaces, "private static void WriteStructureSurfaces(Context c)"));
			string rocks = SourceScanPins.ReadCode(Art + "BiomeArtGenerator.Rocks.cs");
			StringAssert.DoesNotContain("WritePng(", SourceScanPins.Body(rocks, "private static void WriteRockSurface(Context c, SurfaceRecipe recipe, Func<string, string> pathOf, bool arraysDue)"));
			Ordered(SourceScanPins.Body(rocks, "private static void RockLit(Context c, Material m, string albedo, string normal, string mask, float scale, float moss = 0f)"),
				"SetSurfaceMap(c, m, \"_BaseMap\", albedo);", "SetSurfaceMap(c, m, \"_BumpMap\", normal);", "SetSurfaceMap(c, m, \"_MetallicGlossMap\", mask);", "SetSurfaceMap(c, m, \"_OcclusionMap\", mask);");
			string glow = SourceScanPins.ReadCode(Art + "BiomeArtGenerator.CaveGlow.cs");
			StringAssert.DoesNotContain("WritePng(", SourceScanPins.Body(glow, "private static void WriteCaveGlowMaps(Context c)"));
			// No material writer sets a surface map's 2D texture itself any more.
			foreach (string file in new[] { "BiomeArtGenerator.Structures.cs", "BiomeArtGenerator.Rocks.cs", "BiomeArtGenerator.CaveGlow.cs" })
			{
				string code = SourceScanPins.ReadCode(Art + file);
				foreach (string slot in ProceduralArtLookArrays.Slots)
				{
					StringAssert.DoesNotContain($".SetTexture(\"{slot}\"", code, $"{file}: {slot}");
				}
			}
			// Materials outside the generator that name single maps are pointed at the slices, as the last step.
			string driver = SourceScanPins.ReadCode(Art + "BiomeArtGenerator.cs");
			Ordered(SourceScanPins.Body(driver, "private static IEnumerable<string> StepsOf(Context c)"), "yield return \"Built terrain layers\";", "yield return \"Look array users\";", "RebindLookArrayUsers(c);");
			StringAssert.Contains("ExpectedTextureType(path)", SourceScanPins.Body(driver, "private static void VerifyTextures(Context c)"));
		}

		[Test]
		public void Payload_ListsTheArrays_AndNoSingleMapOfAnArrayBackedFamily()
		{
			var payload = new HashSet<string>(ProceduralArtCatalogue.PayloadPaths());
			foreach (Layout layout in ProceduralArtLookArrays.Layouts)
			{
				Assert.That(payload, Does.Contain(layout.Path));
				foreach (string member in layout.Members)
				{
					Assert.That(payload, Does.Not.Contain(member));
				}
			}
			// New generator sources are hashed and belong to the families whose art they decide.
			Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain("ProceduralArtLookArrays.cs"));
			Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain("BiomeArtGenerator.LookArrays.cs"));
			Assert.That(ProceduralArtFamilies.FamiliesOfFile("ProceduralArtLookArrays.cs"), Is.EquivalentTo(new[] { ArtFamily.Rocks, ArtFamily.Structures }));
			Assert.That(ProceduralArtFamilies.FamiliesOfFile("BiomeArtGenerator.LookArrays.cs"), Is.EquivalentTo(new[] { ArtFamily.Rocks, ArtFamily.Structures }));
		}

		[Test]
		public void Importer_MakesAnArrayOfTheFlipbook_WithTheSinglesSettings()
		{
			string code = SourceScanPins.ReadCode(Art + "ProceduralArtTextureImport.cs");
			Ordered(SourceScanPins.Body(code, "private void OnPreprocessTexture()"), "ProceduralArtLookArrays.TryParseArrayName(file, out string set, out ProceduralArtLookArrays.Kind kind)", "LookArray(importer, set, kind);",
				"importer.textureShape = TextureImporterShape.Texture2D;");
			string array = SourceScanPins.Body(code, "private void LookArray(TextureImporter importer, string set, ProceduralArtLookArrays.Kind kind)");
			Ordered(array, "importer.mipmapEnabled = true;", "importer.anisoLevel = 8;", "importer.filterMode = FilterMode.Trilinear;", "importer.textureCompression = TextureImporterCompression.CompressedHQ;",
				"importer.wrapMode = TextureWrapMode.Repeat;", "importer.npotScale = TextureImporterNPOTScale.None;", "importer.maxTextureSize = ProceduralArtLookArrays.MaxSourceSide;",
				"importer.textureType = TextureImporterType.NormalMap;", "importer.sRGBTexture = false;", "importer.sRGBTexture = true;",
				"settings.textureShape = TextureImporterShape.Texture2DArray;", "settings.flipbookColumns = width / size;", "settings.flipbookRows = height / size;", "importer.SetTextureSettings(settings);");
		}

		[Test]
		public void CaveRock_WallOverride_LetsGoOfTheShellsSlices()
		{
			string code = SourceScanPins.ReadCode("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Shapers/CaveRockLayers.cs");
			Ordered(SourceScanPins.Body(code, "public static void Apply(Material material, in CaveRockLook look)"),
				"material.SetTexture(\"_BaseMap\", wallAlbedo);", "EmptyLookSlice(material, 0);", "material.SetTexture(\"_BumpMap\", wallNormal);", "EmptyLookSlice(material, 1);");
		}
	}
}
