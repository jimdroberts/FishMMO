#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What a palette entry is for within its biome, which decides how its weight is worked out.</summary>
	public enum PaletteRole
	{
		/// <summary>The biome's base ground: everywhere in the biome the others leave room.</summary>
		Main,
		/// <summary>Variation over the base, confined by noise and the layer's height and slope bands.</summary>
		Detail,
		/// <summary>Steep faces, by slope angle.</summary>
		Cliff,
		/// <summary>
		/// Ground under water: the biome's lakebed (under a lake or the sea), its riverbed (under a river) and its extra bed layers by
		/// depth (<see cref="SceneTerrainPalette.SlotBed"/>), told apart by <see cref="SceneTerrainPalette.Entry.Slot"/> (decision 35,
		/// 2026-10-18; before, the riverbed was painted only when a biome had no lakebed, so every river lay on the lakebed).
		/// </summary>
		Submerged,
		/// <summary>
		/// A river's bars and beaches: sand or gravel the water laid, each biome's own (its template's bar sand and bar
		/// gravel, <see cref="SceneTerrainPalette.SlotSand"/>, <see cref="SceneTerrainPalette.SlotGravel"/>), so a bar
		/// across two biomes takes each one's by its weight there.
		/// </summary>
		Sediment,
		/// <summary>
		/// A ground the scene's ways are surfaced with (<see cref="SceneTerrainPalette.SlotPathEarth"/> …): carried in the
		/// arrays for the path overlay to sample, never painted into the splat and never scattered on.
		/// </summary>
		Path,
	}

	/// <summary>
	/// A scene's terrain layers in one fixed order, and which biome and texture layer each came from.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One order for the whole scene.</b> <see cref="Layers"/> is assigned to every tile's
	/// <c>terrainLayers</c> as it stands, so channel <i>i</i> of every tile's alphamap means the
	/// same ground, the texture-array slice <i>i</i> is that ground's art, and a seam between two
	/// tiles never changes what a weight means. A per-tile subset would save a little memory on
	/// tiles that miss a biome and cost a remapping in the shader, the scatter and every tool a
	/// designer touches the terrain with afterwards.
	/// </para>
	/// <para>
	/// <b>Shared art is one layer.</b> Two biomes using the same terrain layer asset get one
	/// channel, not two, so a grassland running into a meadow on the same grass is one continuous
	/// ground rather than two identical textures cross-fading. Every <see cref="Entry"/> still
	/// remembers its own biome and source, which is what the scatter keys its rules by.
	/// </para>
	/// <para>
	/// <b>Capped at <see cref="MaximumLayers"/></b>, the most the terrain's index map can name (a byte per
	/// layer; TerrainIndexMap) and a texture array every target API holds. It was 32 (eight control maps of
	/// four channels, all read per pixel) until 2026-10-16. Past the cap the least-covering biomes' detail layers go
	/// first, then their cliffs; a biome's main layer is never dropped while another biome's
	/// detail layer is kept, because a biome without its base ground is unreadable and one without
	/// its variation is merely plainer. What was dropped is reported, never silent.
	/// </para>
	/// </remarks>
	public sealed class SceneTerrainPalette
	{
		/// <summary>The most layers one scene may carry: what the array terrain shader reads.</summary>
		public const int MaximumLayers = TerrainArrayLayerParams.MaximumLayers;

		/// <summary>Slot keys; detail and cliff slots append their index (<c>detail/0</c>).</summary>
		public const string SlotMain = "main";
		public const string SlotDetail = "detail/";
		public const string SlotCliff = "cliff/";
		public const string SlotLakebed = "lakebed";
		public const string SlotRiverbed = "riverbed";
		/// <summary>The prefix of a biome's extra bed layers by depth (<c>bed/0</c> …; BiomeLocalArt.BedSlot), 2026-10-18.</summary>
		public const string SlotBed = BiomeLocalArt.SlotBed;
		/// <summary>The river sediment slots: the sand and gravel a biome's bars are painted with, its Bar Sand and Bar Gravel.</summary>
		public const string SlotSand = BiomeLocalArt.SlotBarSand;
		public const string SlotGravel = BiomeLocalArt.SlotBarGravel;
		/// <summary>
		/// The path surface slots (PathSurfaces in SceneGenerator): each biome's trodden ground, road ground and cobbles,
		/// keyed as the biome's own Small Path, Road and Cobbles slots so a LOCAL sidecar dresses them.
		/// </summary>
		public const string SlotPathEarth = BiomeLocalArt.SlotPath;
		public const string SlotPathGravel = BiomeLocalArt.SlotRoad;
		public const string SlotPathStone = BiomeLocalArt.SlotCobbles;

		/// <summary>The texture layer a slot key names on a biome, or null when the biome has no such slot.</summary>
		public static TerrainTextureLayer SlotLayer(BiomeTemplate biome, string slot)
		{
			if (biome == null || string.IsNullOrEmpty(slot))
			{
				return null;
			}
			if (slot == SlotMain) return biome.MainTextureLayer;
			if (slot == SlotLakebed) return biome.LakebedTextureLayer;
			if (slot == SlotRiverbed) return biome.RiverbedTextureLayer;
			if (slot == SlotPathEarth) return biome.SmallPathTextureLayer;
			if (slot == SlotPathGravel) return biome.RoadTextureLayer;
			if (slot == SlotPathStone) return biome.CobbleTextureLayer;
			if (slot == SlotSand) return biome.BarSandTextureLayer;
			if (slot == SlotGravel) return biome.BarGravelTextureLayer;
			if (slot.StartsWith(SlotDetail, StringComparison.Ordinal) && int.TryParse(slot.Substring(SlotDetail.Length), out int d))
			{
				return biome.DetailTextureLayers != null && d >= 0 && d < biome.DetailTextureLayers.Count ? biome.DetailTextureLayers[d] : null;
			}
			if (slot.StartsWith(SlotCliff, StringComparison.Ordinal) && int.TryParse(slot.Substring(SlotCliff.Length), out int c))
			{
				return biome.CliffTextureLayers != null && c >= 0 && c < biome.CliffTextureLayers.Count ? biome.CliffTextureLayers[c] : null;
			}
			if (slot.StartsWith(BiomeLocalArt.SlotVariant, StringComparison.Ordinal) || slot.StartsWith(SlotBed, StringComparison.Ordinal))
			{
				// A climate variant's ground override (variant/frozen/main), or a bed layer by depth (bed/0), 2026-10-18.
				return BiomeLocalArt.CommittedLayer(biome, slot);
			}
			return null;
		}

		/// <summary>One biome's use of one palette layer.</summary>
		public sealed class Entry
		{
			/// <summary>This entry's own index in <see cref="Entries"/>, for per-entry scratch arrays.</summary>
			public int Index;
			/// <summary>Index into <see cref="Layers"/>, the alphamap channel and the texture-array slice.</summary>
			public int LayerIndex;
			/// <summary>Index into the field's <see cref="SceneBiomeField.Biomes"/>.</summary>
			public int BiomeIndex;
			public BiomeTemplate Biome;
			/// <summary>The biome's texture layer this entry paints: its noise, bands and spawn rules.</summary>
			public TerrainTextureLayer Source;
			public PaletteRole Role;
			/// <summary>
			/// Which of the biome's layer slots this is — <c>main</c>, <c>detail/2</c>, <c>cliff/0</c>,
			/// <c>lakebed</c>, <c>riverbed</c> — stable across runs, so a scene can record where each of
			/// its layers came from and a later step can find that slot's art again.
			/// </summary>
			public string Slot;
			/// <summary>True when the biome had no art for this role and a placeholder stands in.</summary>
			public bool Placeholder;
			/// <summary>
			/// The biome ground slot a main or detail entry paints (0 the main layer, k detail k − 1; BiomeLocalArt.GroundSlot), −1
			/// for every other role. A climate variant's override carries the slot it stands in for.
			/// </summary>
			public int Ground = -1;
			/// <summary>
			/// The climate variant whose ground override this entry is (<see cref="BiomeClimateVariant.TextureLayerOverrides"/>,
			/// 2026-10-18), or null for the biome's own layer: within the variant's envelope it takes the share of
			/// <see cref="Ground"/> the biome's own layer gives up (BiomeSplatPainter).
			/// </summary>
			public BiomeClimateVariant Variant;
			/// <summary>The variant's index in its template's list (the order variants claim a slot in), −1 for the biome's own.</summary>
			public int VariantIndex = -1;

			/// <summary>True when the climate changes this entry's weight: a variant's override, or a main or detail layer with a climate band.</summary>
			public bool Climatic => Variant != null || (Role == PaletteRole.Main || Role == PaletteRole.Detail) && Source != null && Source.HasClimateBand;
		}

		/// <summary>The layers in channel order.</summary>
		public IReadOnlyList<TerrainLayer> Layers => layers;

		/// <summary>Every entry, grouped by biome in field order, main layer first within each.</summary>
		public IReadOnlyList<Entry> Entries => entries;

		/// <summary>Why entries were left out, one line each. Empty when nothing was.</summary>
		public IReadOnlyList<string> Dropped => dropped;

		/// <summary>
		/// True when any entry's weight depends on the local climate (<see cref="Entry.Climatic"/>): the splat painter then reads
		/// the scene's placement climate; with none, it never samples it and paints exactly as before the bands existed.
		/// </summary>
		public bool NeedsClimate
		{
			get
			{
				foreach (Entry entry in entries)
				{
					if (entry.Climatic)
					{
						return true;
					}
				}
				return false;
			}
		}

		private readonly List<TerrainLayer> layers = new List<TerrainLayer>();
		private readonly List<Entry> entries = new List<Entry>();
		private readonly List<string> dropped = new List<string>();
		private readonly List<Entry>[] byBiome;

		private SceneTerrainPalette(int biomeCount)
		{
			byBiome = new List<Entry>[biomeCount];
			for (int i = 0; i < biomeCount; i++)
			{
				byBiome[i] = new List<Entry>();
			}
		}

		/// <summary>The entries of one biome, main layer first.</summary>
		public IReadOnlyList<Entry> EntriesFor(int biomeIndex)
		{
			return biomeIndex >= 0 && biomeIndex < byBiome.Length ? byBiome[biomeIndex] : (IReadOnlyList<Entry>)Array.Empty<Entry>();
		}

		/// <summary>
		/// Gathers the layers every biome in a field paints with.
		/// </summary>
		/// <param name="field">The scene's biomes and how much of it each covers.</param>
		/// <param name="resolve">
		/// The terrain layer a texture layer draws with: its own asset when it has one, else one
		/// built from its textures. Null means the layer has no art.
		/// </param>
		/// <param name="placeholder">
		/// The layer a biome with no main art falls back to, so the scene still reads. Null leaves
		/// such a biome unpainted, which renders as whatever its neighbours bleed into it.
		/// </param>
		public static SceneTerrainPalette Build(SceneBiomeField field,
			Func<TerrainTextureLayer, TerrainLayer> resolve, Func<BiomeTemplate, TerrainLayer> placeholder)
		{
			if (resolve == null)
			{
				throw new ArgumentNullException(nameof(resolve));
			}
			return Build(field, (biome, slot, layer) => resolve(layer), placeholder);
		}

		/// <summary>
		/// <see cref="Build(SceneBiomeField, Func{TerrainTextureLayer, TerrainLayer}, Func{BiomeTemplate, TerrainLayer})"/>
		/// with a resolver that is told which biome and slot it resolves for.
		/// </summary>
		/// <remarks>
		/// The slot is what a LOCAL override is keyed by (<see cref="LocalArtScope.PaletteResolver"/>):
		/// one committed texture layer can be shared by several biomes, and only one of them may have a
		/// sidecar for it. A resolver is still only asked about a layer that has committed art
		/// (<see cref="TerrainTextureLayer.HasAlbedo"/>), so an override dresses a slot; it cannot
		/// conjure one a biome lacks.
		/// </remarks>
		/// <param name="paths">
		/// A biome's way grounds, slot and texture layer: its own (<see cref="SlotPathEarth"/>, <see cref="SlotPathGravel"/>,
		/// <see cref="SlotPathStone"/>) and those its ground layers carry of their own (BiomeLocalArt.WaySlot keys:
		/// <c>main/road</c>, <c>detail/2/path</c>), which <see cref="PathLayerMap"/> prefers on their ground. Null for a scene
		/// with no ways.
		/// </param>
		/// <param name="sediment">
		/// A biome's river sediment, slot and texture layer (<see cref="SlotSand"/>, <see cref="SlotGravel"/>): its own
		/// bar sand and bar gravel, so a bar is sand or gravel by the water that laid it and the biome's art by the biome it
		/// lies in (the painter shares a bar between biomes by their weights). Null for a scene with no rivers.
		/// </param>
		/// <param name="worldAir">
		/// The air of the scene's world (BiomeAir.MaskOf its atmosphere), for the ground layers' air bands
		/// (<see cref="TerrainTextureLayer.airBand"/>, decision 44, 2026-10-18): a main, detail or cliff layer banded to other airs is
		/// left out, and a main layer so left out hands its place to the first air-banded detail layer that names the world's air
		/// (<see cref="AirStandIn"/>). None (unknown) passes every band; no layer banded, nothing changes.
		/// </param>
		public static SceneTerrainPalette Build(SceneBiomeField field,
			Func<BiomeTemplate, string, TerrainTextureLayer, TerrainLayer> resolveSlot, Func<BiomeTemplate, TerrainLayer> placeholder,
			Func<BiomeTemplate, IReadOnlyList<(string slot, TerrainTextureLayer source)>> sediment = null,
			Func<BiomeTemplate, IReadOnlyList<(string slot, TerrainTextureLayer source)>> paths = null,
			BiomeAtmosphereRequirement worldAir = BiomeAtmosphereRequirement.None)
		{
			if (field == null)
			{
				throw new ArgumentNullException(nameof(field));
			}
			if (resolveSlot == null)
			{
				throw new ArgumentNullException(nameof(resolveSlot));
			}

			var palette = new SceneTerrainPalette(field.Biomes.Count);
			float[] share = field.Coverage();

			// Every candidate first, then the cap decides; the order of the candidates is the order
			// they are kept in, so it is also the priority.
			var candidates = new List<(Entry entry, TerrainLayer layer, int priority)>();
			for (int b = 0; b < field.Biomes.Count; b++)
			{
				BiomeTemplate biome = field.Biomes[b];
				/* The main ground for the world's air (decision 44): a main layer banded to other airs gives its place to the first
				 * air-banded detail layer that names this one, painted as the main ground under its own slot's art and rules (the
				 * Dust Sea's grey moondust on an airless moon, its rust dust under a thin sky). Without such a detail, or with no art
				 * for it, the main layer stays: a biome is never left without its ground. */
				TerrainTextureLayer mainSource = biome.MainTextureLayer;
				string mainSlot = SlotMain;
				int standIn = mainSource != null && !mainSource.AdmitsAir(worldAir) ? AirStandIn(biome, worldAir) : -1;
				TerrainLayer main = null;
				if (standIn >= 0)
				{
					main = Resolve(resolveSlot, biome, SlotDetail + standIn, biome.DetailTextureLayers[standIn]);
					if (main != null)
					{
						mainSource = biome.DetailTextureLayers[standIn];
						mainSlot = SlotDetail + standIn;
					}
					else
					{
						standIn = -1;
					}
				}
				main ??= Resolve(resolveSlot, biome, SlotMain, biome.MainTextureLayer);
				bool mainPlaceholder = false;
				if (main == null && placeholder != null)
				{
					main = placeholder(biome);
					mainPlaceholder = main != null;
				}
				if (main != null)
				{
					candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = mainSource, Role = PaletteRole.Main, Slot = mainSlot, Placeholder = mainPlaceholder, Ground = 0 }, main, 0));
				}

				/* Under water (decision 35 (h), 2026-10-18): the lakebed under a lake or the sea, the riverbed under a river, each its
				 * own entry (BiomeSplatPainter shares the bed between them by the water over it). The riverbed was a candidate only when
				 * a biome had no lakebed, so it was never painted: every river lay on its biome's lakebed. Where both are one terrain
				 * layer they are one channel and the splat is what it was. */
				TerrainLayer lakebedLayer = Resolve(resolveSlot, biome, SlotLakebed, biome.LakebedTextureLayer);
				if (lakebedLayer != null)
				{
					candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = biome.LakebedTextureLayer, Role = PaletteRole.Submerged, Slot = SlotLakebed }, lakebedLayer, 1));
				}
				TerrainLayer riverbedLayer = Resolve(resolveSlot, biome, SlotRiverbed, biome.RiverbedTextureLayer);
				if (riverbedLayer != null)
				{
					candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = biome.RiverbedTextureLayer, Role = PaletteRole.Submerged, Slot = SlotRiverbed }, riverbedLayer, 1));
				}
				// The extra beds by depth (decision 35 (b)), kept or dropped with the details: a lake without its deeps is plainer, not unreadable.
				int beds = biome.BedTextureLayers != null ? Math.Min(biome.BedTextureLayers.Count, BiomeTemplate.MaximumBedLayers) : 0;
				for (int k = 0; k < beds; k++)
				{
					BedTextureLayer bedSource = biome.BedTextureLayers[k];
					string bedSlot = BiomeLocalArt.BedSlot(k);
					TerrainLayer bedLayer = Resolve(resolveSlot, biome, bedSlot, bedSource);
					if (bedLayer != null)
					{
						candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = bedSource, Role = PaletteRole.Submerged, Slot = bedSlot }, bedLayer, 3));
					}
				}

				IReadOnlyList<(string slot, TerrainTextureLayer source)> biomeSediment = sediment?.Invoke(biome);
				if (biomeSediment != null)
				{
					foreach ((string slot, TerrainTextureLayer source) in biomeSediment)
					{
						TerrainLayer layer = Resolve(resolveSlot, biome, slot, source);
						if (layer != null)
						{
							candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = source, Role = PaletteRole.Sediment, Slot = slot }, layer, 1));
						}
					}
				}

				IReadOnlyList<(string slot, TerrainTextureLayer source)> biomePaths = paths?.Invoke(biome);
				if (biomePaths != null)
				{
					foreach ((string slot, TerrainTextureLayer source) in biomePaths)
					{
						// The ways of a ground this world does not paint (decision 44) are not its ways either: no channel spent on them.
						if (BiomeLocalArt.TryParseWaySlot(slot, out string wayGround, out _) && !PaintsGround(biome, wayGround, standIn, worldAir))
						{
							continue;
						}
						TerrainLayer layer = Resolve(resolveSlot, biome, slot, source);
						if (layer != null)
						{
							// A detail layer's own way (detail/2/road) is kept or dropped with the details: it draws only on its ground.
							bool detailWay = BiomeLocalArt.TryParseWaySlot(slot, out string ground, out _) && ground != SlotMain;
							candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = source, Role = PaletteRole.Path, Slot = slot }, layer, detailWay ? 3 : 1));
						}
					}
				}

				if (biome.CliffTextureLayers != null)
				{
					for (int c = 0; c < biome.CliffTextureLayers.Count; c++)
					{
						CliffTextureLayer cliff = biome.CliffTextureLayers[c];
						// A cliff banded to other airs than the world's is not this world's rock face (decision 44).
						if (cliff != null && !cliff.AdmitsAir(worldAir))
						{
							continue;
						}
						TerrainLayer layer = Resolve(resolveSlot, biome, SlotCliff + c, cliff);
						if (layer != null)
						{
							candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = cliff, Role = PaletteRole.Cliff, Slot = SlotCliff + c }, layer, 2));
						}
					}
				}
				if (biome.DetailTextureLayers != null)
				{
					for (int d = 0; d < biome.DetailTextureLayers.Count; d++)
					{
						TerrainTextureLayer detail = biome.DetailTextureLayers[d];
						// The main ground's stand-in is painted as the main; a detail banded to other airs is not this world's (decision 44).
						if (d == standIn || detail != null && !detail.AdmitsAir(worldAir))
						{
							continue;
						}
						TerrainLayer layer = Resolve(resolveSlot, biome, SlotDetail + d, detail);
						if (layer != null)
						{
							candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = detail, Role = PaletteRole.Detail, Slot = SlotDetail + d, Ground = d + 1 }, layer, 3));
						}
					}
				}
				/* The biome's climate variants' ground overrides (decision 29, 2026-10-18): each one with art is a layer of the
				 * scene, painted in the variant's envelope in place of the slot it stands in for. Kept or dropped with the details
				 * (a biome without its frozen look is plainer, not unreadable); none listed, none added, and the palette is as it was. */
				if (biome.ClimateVariants != null)
				{
					for (int v = 0; v < biome.ClimateVariants.Count; v++)
					{
						BiomeClimateVariant variant = biome.ClimateVariants[v];
						int overrides = variant?.TextureLayerOverrides != null ? variant.TextureLayerOverrides.Count : 0;
						for (int g = 0; g < overrides; g++)
						{
							TerrainTextureLayer source = variant.TextureLayerOverrides[g];
							string slot = BiomeLocalArt.VariantSlot(BiomeLocalArt.VariantId(variant, v), g);
							TerrainLayer layer = Resolve(resolveSlot, biome, slot, source);
							if (layer != null)
							{
								candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = source, Role = g == 0 ? PaletteRole.Main : PaletteRole.Detail,
									Slot = slot, Ground = g, Variant = variant, VariantIndex = v }, layer, 3));
							}
						}
					}
				}
			}

			/* Keep order: by role priority, then by how much of the scene the biome covers. A
			 * stable sort so a biome's own layers keep their authored order within a role. */
			var order = new List<int>(candidates.Count);
			for (int i = 0; i < candidates.Count; i++)
			{
				order.Add(i);
			}
			order.Sort((x, y) =>
			{
				int byRole = candidates[x].priority.CompareTo(candidates[y].priority);
				if (byRole != 0)
				{
					return byRole;
				}
				int byShare = share[candidates[y].entry.BiomeIndex].CompareTo(share[candidates[x].entry.BiomeIndex]);
				return byShare != 0 ? byShare : x.CompareTo(y);
			});

			var channel = new Dictionary<TerrainLayer, int>();
			var kept = new bool[candidates.Count];
			foreach (int i in order)
			{
				(Entry entry, TerrainLayer layer, int _) = candidates[i];
				if (!channel.TryGetValue(layer, out int index))
				{
					if (palette.layers.Count >= MaximumLayers)
					{
						palette.dropped.Add($"{entry.Biome.ResolvedDisplayName}: {entry.Role} layer '{layer.name}' (the scene already uses {MaximumLayers} layers)");
						continue;
					}
					index = palette.layers.Count;
					palette.layers.Add(layer);
					channel[layer] = index;
				}
				entry.LayerIndex = index;
				kept[i] = true;
			}

			// Entries grouped by biome in authored order, whatever order the cap visited them in.
			for (int i = 0; i < candidates.Count; i++)
			{
				if (!kept[i])
				{
					continue;
				}
				Entry entry = candidates[i].entry;
				entry.Index = palette.entries.Count;
				palette.entries.Add(entry);
				palette.byBiome[entry.BiomeIndex].Add(entry);
			}
			return palette;
		}

		/// <summary>
		/// The detail layer that stands in for a biome's main ground on a world whose air the main layer's band does not name
		/// (decision 44, 2026-10-18): the first detail layer with an air band (<see cref="TerrainTextureLayer.airBand"/>) that names
		/// <paramref name="worldAir"/>; −1 when none does (the main layer then stays). A detail without a band never stands in: it
		/// is ground of every world, not the other world's main.
		/// </summary>
		public static int AirStandIn(BiomeTemplate biome, BiomeAtmosphereRequirement worldAir)
		{
			return biome != null ? AirStandIn(biome.DetailTextureLayers, worldAir) : -1;
		}

		/// <summary>
		/// True when a ground slot of a biome (<c>main</c>, <c>detail/k</c>) is painted on a world of <paramref name="worldAir"/>: the
		/// main only when no detail stands in for it, a detail when its air band admits the air and it is not the stand-in (its ways
		/// are then the main ground's own). Any other slot is.
		/// </summary>
		private static bool PaintsGround(BiomeTemplate biome, string groundSlot, int standIn, BiomeAtmosphereRequirement worldAir)
		{
			int ground = BiomeLocalArt.GroundIndexOf(groundSlot);
			if (ground == 0)
			{
				return standIn < 0;
			}
			if (ground < 0 || biome.DetailTextureLayers == null || ground - 1 >= biome.DetailTextureLayers.Count)
			{
				return true;
			}
			TerrainTextureLayer detail = biome.DetailTextureLayers[ground - 1];
			return ground - 1 == standIn || detail == null || detail.AdmitsAir(worldAir);
		}

		/// <summary><see cref="AirStandIn(BiomeTemplate, BiomeAtmosphereRequirement)"/> over a biome's detail layers (pure, for tests and tools).</summary>
		public static int AirStandIn(IReadOnlyList<TerrainTextureLayer> details, BiomeAtmosphereRequirement worldAir)
		{
			if (details == null || worldAir == BiomeAtmosphereRequirement.None)
			{
				return -1;
			}
			for (int d = 0; d < details.Count; d++)
			{
				TerrainTextureLayer detail = details[d];
				if (detail != null && detail.airBand != BiomeAtmosphereRequirement.None && detail.AdmitsAir(worldAir))
				{
					return d;
				}
			}
			return -1;
		}

		/// <summary>
		/// The fallback path slices: x earth, y road, z cobbles (the most-covering biome's of each), −1 for one the palette
		/// lacks. Where a pixel's ground has its own biome's earth, road or cobbles, <see cref="PathLayerMap"/> wins (the
		/// cobbles have had their own map since 2026-10-15, so each biome's streets are paved with its template's Cobbles
		/// layer; before, this one slice paved every biome's).
		/// </summary>
		public Vector4 PathLayers(float[] coverage)
		{
			int Find(string slot)
			{
				int best = -1;
				float bestShare = float.NegativeInfinity;
				foreach (Entry entry in entries)
				{
					float share = coverage != null && entry.BiomeIndex < coverage.Length ? coverage[entry.BiomeIndex] : 0f;
					if (entry.Role == PaletteRole.Path && entry.Slot == slot && share > bestShare)
					{
						best = entry.LayerIndex;
						bestShare = share;
					}
				}
				return best;
			}
			return new Vector4(Find(SlotPathEarth), Find(SlotPathGravel), Find(SlotPathStone), -1f);
		}

		/// <summary>
		/// For each layer (array slice) of the scene, the slice of the path ground of the biome that layer belongs to (the
		/// most-covering one when biomes share it), for one path slot; −1 where none. What lets the shader wear a path
		/// through a desert into packed sand and through a forest into a littered track, by the ground under each pixel.
		/// A ground layer that carries a way of its own gives that instead (<see cref="WayLayer"/>: a Taiga bog layer's
		/// corduroy road, the rest of the Taiga's gravel), 2026-10-16.
		/// </summary>
		public float[] PathLayerMap(string slot, float[] coverage)
		{
			// One entry per layer the scene has (the layer table takes −1 past it).
			var map = new float[Mathf.Max(1, layers.Count)];
			var share = new float[map.Length];
			for (int i = 0; i < map.Length; i++)
			{
				map[i] = -1f;
				share[i] = float.NegativeInfinity;
			}
			foreach (Entry entry in entries)
			{
				if (entry.Role == PaletteRole.Path || entry.LayerIndex < 0 || entry.LayerIndex >= map.Length)
				{
					continue;
				}
				float biomeShare = coverage != null && entry.BiomeIndex < coverage.Length ? coverage[entry.BiomeIndex] : 0f;
				if (biomeShare <= share[entry.LayerIndex])
				{
					continue;
				}
				// A variant's override wears the ways of the ground it stands in for (a frozen main layer, the main layer's road).
				int way = WayLayer(byBiome[entry.BiomeIndex], entry.Variant != null ? BiomeLocalArt.GroundSlot(entry.Ground) : entry.Slot, slot);
				if (way >= 0)
				{
					map[entry.LayerIndex] = way;
					share[entry.LayerIndex] = biomeShare;
				}
			}
			return map;
		}

		/// <summary>
		/// The slice one way (<paramref name="waySlot"/>: <see cref="SlotPathEarth"/>, <see cref="SlotPathGravel"/>,
		/// <see cref="SlotPathStone"/>) is drawn with across one of a biome's grounds (<paramref name="groundSlot"/>), among
		/// that biome's entries: the ground layer's own way when it carries one (<c>detail/2/road</c>,
		/// TerrainTextureLayer.ways, 2026-10-16), else the biome's (<c>road</c>); −1 when the palette has neither.
		/// </summary>
		public static int WayLayer(IReadOnlyList<Entry> biomeEntries, string groundSlot, string waySlot)
		{
			if (biomeEntries == null)
			{
				return -1;
			}
			string own = groundSlot != null ? BiomeLocalArt.WaySlot(groundSlot, waySlot) : null;
			int biomes = -1;
			foreach (Entry entry in biomeEntries)
			{
				if (entry == null || entry.Role != PaletteRole.Path)
				{
					continue;
				}
				if (own != null && entry.Slot == own)
				{
					return entry.LayerIndex;
				}
				if (biomes < 0 && entry.Slot == waySlot)
				{
					biomes = entry.LayerIndex;
				}
			}
			return biomes;
		}

		/// <summary>
		/// A palette laid by hand, for tests and tools that weigh ground without templates: the entries as given (each one's
		/// <see cref="Entry.BiomeIndex"/> and <see cref="Entry.LayerIndex"/> set by the caller; <see cref="Entry.Index"/> is set
		/// here), over <paramref name="layerCount"/> channels whose terrain layers are null.
		/// </summary>
		public static SceneTerrainPalette FromEntries(int biomeCount, int layerCount, IEnumerable<Entry> entries)
		{
			var palette = new SceneTerrainPalette(Math.Max(0, biomeCount));
			for (int i = 0; i < layerCount; i++)
			{
				palette.layers.Add(null);
			}
			if (entries != null)
			{
				foreach (Entry entry in entries)
				{
					if (entry == null || entry.BiomeIndex < 0 || entry.BiomeIndex >= biomeCount || entry.LayerIndex < 0 || entry.LayerIndex >= layerCount)
					{
						throw new ArgumentException("Every entry needs a biome and a layer inside the palette.", nameof(entries));
					}
					entry.Index = palette.entries.Count;
					palette.entries.Add(entry);
					palette.byBiome[entry.BiomeIndex].Add(entry);
				}
			}
			return palette;
		}

		private static TerrainLayer Resolve(Func<BiomeTemplate, string, TerrainTextureLayer, TerrainLayer> resolve, BiomeTemplate biome, string slot, TerrainTextureLayer layer)
		{
			return layer != null && layer.HasAlbedo ? resolve(biome, slot, layer) : null;
		}
	}
}
#endif
