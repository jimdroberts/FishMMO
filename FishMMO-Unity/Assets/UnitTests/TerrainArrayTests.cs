using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The pure parts of the texture-array terrain: the LOCAL override order and its conflict rule,
	/// the per-layer number packing the shader reads, the provenance a scene records from its palette,
	/// and the flipbook arithmetic the baker and its importer must agree on. No files, no GPU.
	/// </summary>
	[TestFixture]
	public class TerrainArrayTests
	{
		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (Object asset in created)
			{
				if (asset != null)
				{
					Object.DestroyImmediate(asset);
				}
			}
			created.Clear();
		}

		private T Track<T>(T asset) where T : Object
		{
			created.Add(asset);
			return asset;
		}

		private Texture2D Texture(string name, int size = 4)
		{
			var texture = Track(new Texture2D(size, size));
			texture.name = name;
			return texture;
		}

		private TerrainLayer Layer(string name, Texture2D albedo, Texture2D normal = null, Texture2D mask = null)
		{
			var layer = Track(new TerrainLayer { name = name, diffuseTexture = albedo, normalMapTexture = normal, maskMapTexture = mask });
			return layer;
		}

		private BiomeTemplate Biome(string name)
		{
			var biome = Track(ScriptableObject.CreateInstance<BiomeTemplate>());
			biome.name = name;
			return biome;
		}

		/// <summary>A LOCAL folder in memory: sidecars by (biome, slot), layers and textures by the committed one's name.</summary>
		private sealed class FakeLookup : ITerrainArrayLocalLookup
		{
			public readonly Dictionary<(BiomeTemplate, string), BiomeLocalArt.SlotOverride> Sidecars = new Dictionary<(BiomeTemplate, string), BiomeLocalArt.SlotOverride>();
			public readonly Dictionary<string, TerrainLayer> Layers = new Dictionary<string, TerrainLayer>();
			public readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

			public BiomeLocalArt.SlotOverride Sidecar(BiomeTemplate biome, string slot) => Sidecars.TryGetValue((biome, slot), out var o) ? o : null;
			public TerrainLayer LayerOverride(TerrainLayer committed) => committed != null && Layers.TryGetValue(committed.name, out var l) ? l : null;
			public Texture2D TextureOverride(Texture2D committed) => committed != null && Textures.TryGetValue(committed.name, out var t) ? t : null;
		}

		private static TerrainArrayLayerSource Source(TerrainLayer layer, params (BiomeTemplate biome, string slot)[] uses)
		{
			var source = new TerrainArrayLayerSource { Layer = layer };
			foreach ((BiomeTemplate biome, string slot) in uses)
			{
				source.Uses.Add(new TerrainArrayLayerUse { Biome = biome, Slot = slot });
			}
			return source;
		}

		// ── LOCAL resolution ──────────────────────────────────────────

		[Test]
		public void WithNothingLocalTheCommittedArtIsBaked()
		{
			Texture2D albedo = Texture("grass_albedo"), normal = Texture("grass_normal");
			TerrainLayer grass = Layer("Grass", albedo, normal);
			var conflicts = new List<string>();

			ResolvedTerrainArrayLayer resolved = TerrainArrayResolver.Resolve(0, Source(grass, (Biome("Meadow"), "main")), new FakeLookup(), conflicts);

			Assert.That(resolved.ParamsLayer, Is.SameAs(grass));
			Assert.That(resolved.Albedo, Is.SameAs(albedo));
			Assert.That(resolved.Normal, Is.SameAs(normal));
			Assert.That(resolved.Mask, Is.Null);
			Assert.That(resolved.AlbedoSource, Is.EqualTo(TerrainArraySource.Committed));
			Assert.That(resolved.MaskSource, Is.EqualTo(TerrainArraySource.None));
			Assert.That(conflicts, Is.Empty);
		}

		[Test]
		public void ASidecarLayerBeatsEveryNameMatch()
		{
			BiomeTemplate meadow = Biome("Meadow");
			Texture2D albedo = Texture("grass_albedo");
			TerrainLayer grass = Layer("Grass", albedo);
			TerrainLayer licensed = Layer("Licensed Grass", Texture("licensed_albedo"), Texture("licensed_normal"));
			var lookup = new FakeLookup();
			lookup.Sidecars[(meadow, "main")] = new BiomeLocalArt.SlotOverride { Slot = "main", TerrainLayer = licensed };
			lookup.Layers["Grass"] = Layer("Grass", Texture("named_layer_albedo"));
			lookup.Textures["grass_albedo"] = Texture("named_texture");

			ResolvedTerrainArrayLayer resolved = TerrainArrayResolver.Resolve(0, Source(grass, (meadow, "main")), lookup, null);

			Assert.That(resolved.ParamsLayer, Is.SameAs(licensed), "the sidecar layer's tiling and remaps");
			Assert.That(resolved.Albedo, Is.SameAs(licensed.diffuseTexture));
			Assert.That(resolved.Normal, Is.SameAs(licensed.normalMapTexture));
			Assert.That(resolved.ParamsSource, Is.EqualTo(TerrainArraySource.SidecarLayer));
		}

		[Test]
		public void ANamedLayerIsUsedWholeAndItsMissingMapsStayMissing()
		{
			Texture2D committedNormal = Texture("grass_normal");
			TerrainLayer grass = Layer("Grass", Texture("grass_albedo"), committedNormal);
			TerrainLayer local = Layer("Grass", Texture("local_albedo"));
			var lookup = new FakeLookup();
			lookup.Layers["Grass"] = local;
			lookup.Textures["grass_normal"] = Texture("should_not_be_used");

			ResolvedTerrainArrayLayer resolved = TerrainArrayResolver.Resolve(0, Source(grass, (Biome("Meadow"), "main")), lookup, null);

			Assert.That(resolved.ParamsLayer, Is.SameAs(local));
			Assert.That(resolved.Albedo, Is.SameAs(local.diffuseTexture));
			Assert.That(resolved.Normal, Is.Null, "a LOCAL layer without a normal map means flat, not the committed map");
			Assert.That(resolved.NormalSource, Is.EqualTo(TerrainArraySource.None));
		}

		[Test]
		public void SidecarTexturesOverrideSingleMapsAndNameMatchesFillTheRest()
		{
			BiomeTemplate meadow = Biome("Meadow");
			Texture2D committedMask = Texture("grass_mask");
			TerrainLayer grass = Layer("Grass", Texture("grass_albedo"), Texture("grass_normal"), committedMask);
			Texture2D sidecarAlbedo = Texture("sidecar_albedo"), namedNormal = Texture("named_normal");
			var lookup = new FakeLookup();
			lookup.Sidecars[(meadow, "main")] = new BiomeLocalArt.SlotOverride { Slot = "main", Albedo = sidecarAlbedo };
			lookup.Textures["grass_normal"] = namedNormal;

			ResolvedTerrainArrayLayer resolved = TerrainArrayResolver.Resolve(0, Source(grass, (meadow, "main")), lookup, null);

			Assert.That(resolved.ParamsLayer, Is.SameAs(grass), "no whole-layer override: the committed numbers");
			Assert.That(resolved.Albedo, Is.SameAs(sidecarAlbedo));
			Assert.That(resolved.AlbedoSource, Is.EqualTo(TerrainArraySource.SidecarTexture));
			Assert.That(resolved.Normal, Is.SameAs(namedNormal));
			Assert.That(resolved.NormalSource, Is.EqualTo(TerrainArraySource.NamedTexture));
			Assert.That(resolved.Mask, Is.SameAs(committedMask));
			Assert.That(resolved.MaskSource, Is.EqualTo(TerrainArraySource.Committed));
		}

		[Test]
		public void OnASharedLayerTheBiggestBiomesOverrideWinsAndADisagreementIsReported()
		{
			BiomeTemplate big = Biome("Big"), small = Biome("Small");
			TerrainLayer grass = Layer("Grass", Texture("grass_albedo"));
			TerrainLayer bigArt = Layer("Big Art", Texture("big"));
			TerrainLayer smallArt = Layer("Small Art", Texture("small"));
			var lookup = new FakeLookup();
			lookup.Sidecars[(big, "main")] = new BiomeLocalArt.SlotOverride { Slot = "main", TerrainLayer = bigArt };
			lookup.Sidecars[(small, "detail/0")] = new BiomeLocalArt.SlotOverride { Slot = "detail/0", TerrainLayer = smallArt };
			var conflicts = new List<string>();

			// Uses are recorded biggest biome first.
			ResolvedTerrainArrayLayer resolved = TerrainArrayResolver.Resolve(3, Source(grass, (big, "main"), (small, "detail/0")), lookup, conflicts);

			Assert.That(resolved.ParamsLayer, Is.SameAs(bigArt));
			Assert.That(resolved.SidecarWinner.Biome, Is.SameAs(big));
			Assert.That(conflicts.Count, Is.EqualTo(1));
			StringAssert.Contains("Small", conflicts[0]);
			StringAssert.Contains("Big", conflicts[0]);
		}

		[Test]
		public void AnExplicitOverrideBeatsABiggerBiomeWithNone()
		{
			BiomeTemplate big = Biome("Big"), small = Biome("Small");
			TerrainLayer grass = Layer("Grass", Texture("grass_albedo"));
			TerrainLayer smallArt = Layer("Small Art", Texture("small"));
			var lookup = new FakeLookup();
			lookup.Sidecars[(small, "main")] = new BiomeLocalArt.SlotOverride { Slot = "main", TerrainLayer = smallArt };
			// An entry with nothing in it is no override at all.
			lookup.Sidecars[(big, "main")] = new BiomeLocalArt.SlotOverride { Slot = "main" };
			var conflicts = new List<string>();

			ResolvedTerrainArrayLayer resolved = TerrainArrayResolver.Resolve(0, Source(grass, (big, "main"), (small, "main")), lookup, conflicts);

			Assert.That(resolved.ParamsLayer, Is.SameAs(smallArt));
			Assert.That(conflicts, Is.Empty);
		}

		[Test]
		public void TwoBiomesOverridingWithTheSameArtIsNoConflict()
		{
			BiomeTemplate a = Biome("A"), b = Biome("B");
			TerrainLayer grass = Layer("Grass", Texture("grass_albedo"));
			TerrainLayer shared = Layer("Shared", Texture("shared"));
			var lookup = new FakeLookup();
			lookup.Sidecars[(a, "main")] = new BiomeLocalArt.SlotOverride { Slot = "main", TerrainLayer = shared };
			lookup.Sidecars[(b, "main")] = new BiomeLocalArt.SlotOverride { Slot = "main", TerrainLayer = shared };
			var conflicts = new List<string>();

			TerrainArrayResolver.Resolve(0, Source(grass, (a, "main"), (b, "main")), lookup, conflicts);

			Assert.That(conflicts, Is.Empty);
		}

		[Test]
		public void AnOverrideWithoutAnAlbedoFallsBackToTheCommittedOneAndSaysSo()
		{
			BiomeTemplate meadow = Biome("Meadow");
			Texture2D albedo = Texture("grass_albedo");
			TerrainLayer grass = Layer("Grass", albedo);
			var lookup = new FakeLookup();
			lookup.Sidecars[(meadow, "main")] = new BiomeLocalArt.SlotOverride { Slot = "main", TerrainLayer = Layer("Broken", null, Texture("n")) };
			var conflicts = new List<string>();

			ResolvedTerrainArrayLayer resolved = TerrainArrayResolver.Resolve(0, Source(grass, (meadow, "main")), lookup, conflicts);

			Assert.That(resolved.Albedo, Is.SameAs(albedo));
			Assert.That(conflicts.Count, Is.EqualTo(1));
		}

		// ── Parameter packing ─────────────────────────────────────────

		[Test]
		public void PackedParamsFollowTerrainLitsConventionsAndRoundTrip()
		{
			var layer = new TerrainArrayLayerParams
			{
				TileSize = new Vector2(8f, 4f),
				TileOffset = new Vector2(2f, 1f),
				NormalScale = 0.7f,
				Metallic = 0.25f,
				Smoothness = 0.4f,
				DiffuseRemapMax = new Vector4(0.9f, 0.8f, 0.7f, 1f),
				MaskRemapMin = new Vector4(0.1f, 0.2f, 0.3f, 0.4f),
				MaskRemapMax = new Vector4(0.6f, 0.7f, 0.8f, 0.9f),
				HasMask = true,
			};
			TerrainArrayLayerParams.Allocate(out Vector4[] st, out Vector4[] tint, out Vector4[] offset, out Vector4[] scale, out Vector4[] surface);
			layer.Pack(5, st, tint, offset, scale, surface);

			Assert.That(st[5], Is.EqualTo(new Vector4(1f / 8f, 1f / 4f, 2f / 8f, 1f / 4f)));
			Assert.That(tint[5], Is.EqualTo(new Vector4(0.9f, 0.8f, 0.7f, 0.7f)), "diffuse max is the tint; normal scale rides in w");
			Assert.That(offset[5], Is.EqualTo(layer.MaskRemapMin));
			Assert.That(scale[5].x, Is.EqualTo(0.5f).Within(1e-6f), "scale is max minus min");
			Assert.That(surface[5], Is.EqualTo(new Vector4(0.25f, 0.4f, 1f, 0f)));

			TerrainArrayLayerParams back = TerrainArrayLayerParams.Unpack(5, st, tint, offset, scale, surface);
			Assert.That(back.TileSize.x, Is.EqualTo(8f).Within(1e-4f));
			Assert.That(back.TileSize.y, Is.EqualTo(4f).Within(1e-4f));
			Assert.That(back.TileOffset.x, Is.EqualTo(2f).Within(1e-4f));
			Assert.That(back.NormalScale, Is.EqualTo(0.7f).Within(1e-6f));
			Assert.That(back.MaskRemapMax.w, Is.EqualTo(0.9f).Within(1e-6f));
			Assert.That(back.HasMask, Is.True);

			Assert.That(st.Length, Is.EqualTo(TerrainArrayLayerParams.MaximumLayers), "always the shader's full length");
			Assert.That(st[0], Is.EqualTo(new Vector4(1f / 15f, 1f / 15f, 0f, 0f)), "unused slots hold a neutral layer");
		}

		[Test]
		public void SmoothnessInTheAlbedoAlphaUsesAMultiplierOfOne()
		{
			var layer = TerrainArrayLayerParams.Default;
			layer.Smoothness = 0.3f;
			layer.AlbedoHasAlpha = true;
			TerrainArrayLayerParams.Allocate(out Vector4[] st, out Vector4[] tint, out Vector4[] offset, out Vector4[] scale, out Vector4[] surface);
			layer.Pack(0, st, tint, offset, scale, surface);
			Assert.That(surface[0].y, Is.EqualTo(1f));

			layer.AlbedoHasAlpha = false;
			layer.Pack(0, st, tint, offset, scale, surface);
			Assert.That(surface[0].y, Is.EqualTo(0.3f).Within(1e-6f), "no alpha: the slider");
		}

		[Test]
		public void AZeroTileSizeCannotProduceInfinity()
		{
			var layer = TerrainArrayLayerParams.Default;
			layer.TileSize = Vector2.zero;
			TerrainArrayLayerParams.Allocate(out Vector4[] st, out Vector4[] tint, out Vector4[] offset, out Vector4[] scale, out Vector4[] surface);
			layer.Pack(0, st, tint, offset, scale, surface);
			Assert.That(float.IsInfinity(st[0].x) || float.IsNaN(st[0].x), Is.False);
		}

		[Test]
		public void FromATerrainLayerCopiesWhatUrpsTerrainReads()
		{
			TerrainLayer source = Layer("L", Texture("a"));
			source.tileSize = new Vector2(12f, 6f);
			source.tileOffset = new Vector2(3f, 0f);
			source.normalScale = 0.5f;
			source.metallic = 0.1f;
			source.smoothness = 0.6f;
			source.maskMapRemapMin = new Vector4(0f, 0.1f, 0f, 0f);
			source.maskMapRemapMax = new Vector4(1f, 0.9f, 1f, 1f);

			TerrainArrayLayerParams p = TerrainArrayLayerParams.From(source, hasMask: false, albedoHasAlpha: false);

			Assert.That(p.TileSize, Is.EqualTo(new Vector2(12f, 6f)));
			Assert.That(p.TileOffset, Is.EqualTo(new Vector2(3f, 0f)));
			Assert.That(p.NormalScale, Is.EqualTo(0.5f));
			Assert.That(p.Smoothness, Is.EqualTo(0.6f));
			Assert.That(p.MaskRemapMin.y, Is.EqualTo(0.1f).Within(1e-6f));
			Assert.That(p.HasMask, Is.False);
		}

		// ── Provenance ────────────────────────────────────────────────

		private static SceneBiomeField HalfAndHalf(BiomeTemplate west, BiomeTemplate east, int westColumns = 4)
		{
			var cells = new byte[64];
			for (int z = 0; z < 8; z++)
			{
				for (int x = 0; x < 8; x++)
				{
					cells[z * 8 + x] = (byte)(x < westColumns ? 0 : 1);
				}
			}
			return SceneBiomeField.FromCells(8, 8, 16f, Vector2.zero, new[] { west, east }, cells, 16f);
		}

		[Test]
		public void ProvenanceKeepsTheChannelOrderAndPutsTheBiggestBiomeFirst()
		{
			TerrainLayer grass = Layer("Grass", Texture("grass"));
			TerrainLayer rock = Layer("Rock", Texture("rock"));
			BiomeTemplate meadow = Biome("Meadow"), steppe = Biome("Steppe");
			meadow.MainTextureLayer.terrainLayer = grass;
			steppe.MainTextureLayer.terrainLayer = rock;
			// Grass is also the steppe's detail: one palette channel, two biome uses.
			steppe.DetailTextureLayers.Add(new TerrainTextureLayer { terrainLayer = grass });

			// The steppe covers six columns of eight.
			SceneBiomeField field = HalfAndHalf(meadow, steppe, westColumns: 2);
			SceneTerrainPalette palette = SceneTerrainPalette.Build(field, source => source != null ? source.terrainLayer : null, null);
			float[] coverage = field.Coverage();
			Assert.That(coverage[1], Is.GreaterThan(coverage[0]), "the steppe is the bigger biome");

			List<TerrainArrayLayerSource> provenance = TerrainArraySetup.ProvenanceFrom(palette, coverage);

			Assert.That(provenance.Count, Is.EqualTo(palette.Layers.Count));
			for (int i = 0; i < provenance.Count; i++)
			{
				Assert.That(provenance[i].Layer, Is.SameAs(palette.Layers[i]), $"slice {i} is channel {i}");
			}
			TerrainArrayLayerSource grassSource = provenance.Find(s => s.Layer == grass);
			Assert.That(grassSource.Uses.Count, Is.EqualTo(2));
			Assert.That(grassSource.Uses[0].Biome, Is.SameAs(steppe), "the biggest biome first: its LOCAL override wins");
			Assert.That(grassSource.Uses[0].Slot, Is.EqualTo("detail/0"));
			Assert.That(grassSource.Uses[1].Biome, Is.SameAs(meadow));
			Assert.That(grassSource.Uses[1].Slot, Is.EqualTo("main"));

			// The slot keys recorded are the ones the palette resolves back to the same texture layer.
			foreach (TerrainArrayLayerSource source in provenance)
			{
				foreach (TerrainArrayLayerUse use in source.Uses)
				{
					Assert.That(SceneTerrainPalette.SlotLayer(use.Biome, use.Slot).terrainLayer, Is.SameAs(source.Layer));
					Assert.That(BiomeLocalArt.CommittedLayer(use.Biome, use.Slot), Is.SameAs(SceneTerrainPalette.SlotLayer(use.Biome, use.Slot)));
				}
			}
		}

		[Test]
		public void TheSidecarsSlotKeysAreThePalettes()
		{
			Assert.That(BiomeLocalArt.SlotMain, Is.EqualTo(SceneTerrainPalette.SlotMain));
			Assert.That(BiomeLocalArt.SlotDetail, Is.EqualTo(SceneTerrainPalette.SlotDetail));
			Assert.That(BiomeLocalArt.SlotCliff, Is.EqualTo(SceneTerrainPalette.SlotCliff));
			Assert.That(BiomeLocalArt.SlotLakebed, Is.EqualTo(SceneTerrainPalette.SlotLakebed));
			Assert.That(BiomeLocalArt.SlotRiverbed, Is.EqualTo(SceneTerrainPalette.SlotRiverbed));
			Assert.That(TerrainArrayLayerParams.MaximumLayers, Is.EqualTo(SceneTerrainPalette.MaximumLayers));
		}

		[Test]
		public void ASidecarFindsItsSlotsAndAnEmptyEntryIsNone()
		{
			var art = Track(ScriptableObject.CreateInstance<BiomeLocalArt>());
			BiomeLocalArt.SlotOverride entry = art.GetOrAdd("cliff/1");
			Assert.That(art.Find("cliff/1"), Is.Null, "nothing in it yet");
			entry.Albedo = Texture("x");
			Assert.That(art.Find("cliff/1"), Is.SameAs(entry));
			Assert.That(art.GetOrAdd("cliff/1"), Is.SameAs(entry), "one entry per slot");
			Assert.That(art.Remove("cliff/1"), Is.True);
			Assert.That(art.Find("cliff/1"), Is.Null);
		}

		// ── Flipbook arithmetic ───────────────────────────────────────

		[Test]
		public void TheLayoutFitsTheImageLimitWithLessThanARowOfPadding()
		{
			for (int count = 1; count <= TerrainArrayLayerParams.MaximumLayers; count++)
			{
				foreach (int size in new[] { 128, 256, 512, 1024 })
				{
					TerrainArrayBaker.Layout(count, size, out int columns, out int rows);
					Assert.That(columns * rows, Is.GreaterThanOrEqualTo(count));
					Assert.That(columns * rows - count, Is.LessThan(columns), $"{count} at {size}");
					Assert.That(columns * size, Is.LessThanOrEqualTo(TerrainArrayBaker.MaximumImageEdge));
					Assert.That(rows * size, Is.LessThanOrEqualTo(TerrainArrayBaker.MaximumImageEdge));
				}
			}
			TerrainArrayBaker.Layout(13, 1024, out int c13, out int r13);
			Assert.That((c13, r13), Is.EqualTo((13, 1)), "one row whenever it fits");
			TerrainArrayBaker.Layout(17, 1024, out int c17, out int r17);
			Assert.That((c17, r17), Is.EqualTo((9, 2)));
		}

		[Test]
		public void SliceZeroIsTheTopLeftCell()
		{
			// Texture rows run bottom-up; a flipbook is read from the top row down.
			Assert.That(TerrainArrayBaker.SliceOrigin(0, 9, 2, 1024), Is.EqualTo(new Vector2Int(0, 1024)));
			Assert.That(TerrainArrayBaker.SliceOrigin(8, 9, 2, 1024), Is.EqualTo(new Vector2Int(8 * 1024, 1024)));
			Assert.That(TerrainArrayBaker.SliceOrigin(9, 9, 2, 1024), Is.EqualTo(new Vector2Int(0, 0)));
			Assert.That(TerrainArrayBaker.SliceOrigin(4, 13, 1, 256), Is.EqualTo(new Vector2Int(1024, 0)));
		}

		[Test]
		public void TheImporterReadsBackTheGridTheBakerWrote()
		{
			string path = TerrainArraySet.BakedFolder("Cove Viaduct") + "/" + TerrainArrayBaker.FlipbookFileName(TerrainArrayKind.Normal, 9, 2);
			Assert.That(TerrainArrayBaker.TryParseFlipbook(path, out TerrainArrayKind kind, out int columns, out int rows), Is.True);
			Assert.That(kind, Is.EqualTo(TerrainArrayKind.Normal));
			Assert.That((columns, rows), Is.EqualTo((9, 2)));

			Assert.That(TerrainArrayBaker.TryParseFlipbook("Assets/Textures/Normal 9x2.png", out _, out _, out _), Is.False, "only the baked folder");
			Assert.That(TerrainArrayBaker.TryParseFlipbook(TerrainArraySet.BakedDirectory + "/X/Albedo.png", out _, out _, out _), Is.False);
		}

		[Test]
		public void SliceSizesArePowersOfTwoWithinTheLimits()
		{
			Assert.That(TerrainArrayBaker.SliceSizeFor(8), Is.EqualTo(TerrainArrayBaker.MinimumSliceSize));
			Assert.That(TerrainArrayBaker.SliceSizeFor(600), Is.EqualTo(1024));
			Assert.That(TerrainArrayBaker.SliceSizeFor(512), Is.EqualTo(512));
			Assert.That(TerrainArrayBaker.SliceSizeFor(4096), Is.EqualTo(TerrainArrayBaker.MaximumSliceSize));
		}

		[Test]
		public void EveryNormalLayoutDecodesToTheSamePlainRgb()
		{
			// x = 0.6, y = -0.3 in each of Unity's layouts.
			byte x = (byte)Mathf.RoundToInt((0.6f * 0.5f + 0.5f) * 255f);
			byte y = (byte)Mathf.RoundToInt((-0.3f * 0.5f + 0.5f) * 255f);
			Color32 rgb = TerrainArraySliceReader.EncodeNormal(new Color32(x, y, 200, 255));
			Color32 dxt5nm = TerrainArraySliceReader.EncodeNormal(new Color32(255, y, 0, x));
			Color32 bc5 = TerrainArraySliceReader.EncodeNormal(new Color32(x, y, 0, 255));

			Assert.That(dxt5nm, Is.EqualTo(rgb));
			Assert.That(bc5, Is.EqualTo(rgb));
			float z = rgb.b / 255f * 2f - 1f;
			Assert.That(z, Is.EqualTo(Mathf.Sqrt(1f - 0.36f - 0.09f)).Within(0.02f), "z rebuilt from x and y");
		}

		[Test]
		public void TheHashIsStableAndFollowsTheArt()
		{
			TerrainLayer grass = Layer("Grass", Texture("grass", 64));
			var layers = new List<TerrainArrayLayerSource> { Source(grass, (Biome("Meadow"), "main")) };
			var lookup = new FakeLookup();

			TerrainArrayPlan first = TerrainArrayBaker.Plan("Scene", layers, lookup);
			TerrainArrayPlan again = TerrainArrayBaker.Plan("Scene", layers, lookup);
			Assert.That(again.Hash, Is.EqualTo(first.Hash));
			Assert.That(first.SliceSize, Is.EqualTo(TerrainArrayBaker.MinimumSliceSize));

			grass.tileSize = new Vector2(3f, 3f);
			TerrainArrayPlan retiled = TerrainArrayBaker.Plan("Scene", layers, lookup);
			Assert.That(retiled.Hash, Is.Not.EqualTo(first.Hash), "a layer's numbers are in the hash");

			lookup.Textures["grass"] = Texture("licensed", 64);
			TerrainArrayPlan local = TerrainArrayBaker.Plan("Scene", layers, lookup);
			Assert.That(local.Layers[0].AlbedoSource, Is.EqualTo(TerrainArraySource.NamedTexture));
			Assert.That(local.Hash, Is.Not.EqualTo(retiled.Hash), "a LOCAL texture appearing changes what resolves, and so the hash");
		}
	}
}
