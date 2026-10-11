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
		/// <summary>Ground under the sea: the biome's lakebed layer, else its riverbed layer.</summary>
		Submerged,
		/// <summary>
		/// A river's bars and beaches: sand or gravel the water laid, the same art whatever biome the river
		/// runs through (<see cref="SceneTerrainPalette.SlotSand"/>, <see cref="SceneTerrainPalette.SlotGravel"/>).
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
	/// <b>Capped at <see cref="MaximumLayers"/></b>, the most the array terrain shader reads (eight
	/// control maps of four channels). Past the cap the least-covering biomes' detail layers go
	/// first, then their cliffs; a biome's main layer is never dropped while another biome's
	/// detail layer is kept, because a biome without its base ground is unreadable and one without
	/// its variation is merely plainer. What was dropped is reported, never silent.
	/// </para>
	/// </remarks>
	public sealed class SceneTerrainPalette
	{
		/// <summary>The most layers one scene may carry: what the array terrain shader reads.</summary>
		public const int MaximumLayers = 32;

		/// <summary>Slot keys; detail and cliff slots append their index (<c>detail/0</c>).</summary>
		public const string SlotMain = "main";
		public const string SlotDetail = "detail/";
		public const string SlotCliff = "cliff/";
		public const string SlotLakebed = "lakebed";
		public const string SlotRiverbed = "riverbed";
		/// <summary>The scene's river sediment slots: the sand and gravel every biome's bars are painted with.</summary>
		public const string SlotSand = "sediment/sand";
		public const string SlotGravel = "sediment/gravel";
		/// <summary>
		/// The path surface slots (PathSurfaces in SceneGenerator): each biome's trodden ground and road ground, keyed as the
		/// biome's own Small Path and Road slots so a LOCAL sidecar dresses them; and the cobbles every biome shares.
		/// </summary>
		public const string SlotPathEarth = BiomeLocalArt.SlotPath;
		public const string SlotPathGravel = BiomeLocalArt.SlotRoad;
		public const string SlotPathStone = "path/stone";

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
			if (slot.StartsWith(SlotDetail, StringComparison.Ordinal) && int.TryParse(slot.Substring(SlotDetail.Length), out int d))
			{
				return biome.DetailTextureLayers != null && d >= 0 && d < biome.DetailTextureLayers.Count ? biome.DetailTextureLayers[d] : null;
			}
			if (slot.StartsWith(SlotCliff, StringComparison.Ordinal) && int.TryParse(slot.Substring(SlotCliff.Length), out int c))
			{
				return biome.CliffTextureLayers != null && c >= 0 && c < biome.CliffTextureLayers.Count ? biome.CliffTextureLayers[c] : null;
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
		}

		/// <summary>The layers in channel order.</summary>
		public IReadOnlyList<TerrainLayer> Layers => layers;

		/// <summary>Every entry, grouped by biome in field order, main layer first within each.</summary>
		public IReadOnlyList<Entry> Entries => entries;

		/// <summary>Why entries were left out, one line each. Empty when nothing was.</summary>
		public IReadOnlyList<string> Dropped => dropped;

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
		/// <param name="sediment">
		/// The scene's river sediment, slot and texture layer (<see cref="SlotSand"/>, <see cref="SlotGravel"/>),
		/// given to every biome so a bar is sand or gravel by the water that laid it, not by the biome it lies
		/// in; null or empty for a scene with no rivers.
		/// </param>
		public static SceneTerrainPalette Build(SceneBiomeField field,
			Func<BiomeTemplate, string, TerrainTextureLayer, TerrainLayer> resolveSlot, Func<BiomeTemplate, TerrainLayer> placeholder,
			IReadOnlyList<(string slot, TerrainTextureLayer source)> sediment = null,
			Func<BiomeTemplate, IReadOnlyList<(string slot, TerrainTextureLayer source)>> paths = null)
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
				TerrainLayer main = Resolve(resolveSlot, biome, SlotMain, biome.MainTextureLayer);
				bool mainPlaceholder = false;
				if (main == null && placeholder != null)
				{
					main = placeholder(biome);
					mainPlaceholder = main != null;
				}
				if (main != null)
				{
					candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = biome.MainTextureLayer, Role = PaletteRole.Main, Slot = SlotMain, Placeholder = mainPlaceholder }, main, 0));
				}

				TerrainLayer lakebedLayer = Resolve(resolveSlot, biome, SlotLakebed, biome.LakebedTextureLayer);
				bool lakebed = lakebedLayer != null;
				TerrainLayer riverbedLayer = lakebed ? null : Resolve(resolveSlot, biome, SlotRiverbed, biome.RiverbedTextureLayer);
				TerrainTextureLayer submergedSource = lakebed ? biome.LakebedTextureLayer
					: riverbedLayer != null ? biome.RiverbedTextureLayer : null;
				if (submergedSource != null)
				{
					candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = submergedSource, Role = PaletteRole.Submerged, Slot = lakebed ? SlotLakebed : SlotRiverbed }, lakebed ? lakebedLayer : riverbedLayer, 1));
				}

				if (sediment != null)
				{
					foreach ((string slot, TerrainTextureLayer source) in sediment)
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
						TerrainLayer layer = Resolve(resolveSlot, biome, slot, source);
						if (layer != null)
						{
							candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = source, Role = PaletteRole.Path, Slot = slot }, layer, 1));
						}
					}
				}

				if (biome.CliffTextureLayers != null)
				{
					for (int c = 0; c < biome.CliffTextureLayers.Count; c++)
					{
						CliffTextureLayer cliff = biome.CliffTextureLayers[c];
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
						TerrainLayer layer = Resolve(resolveSlot, biome, SlotDetail + d, detail);
						if (layer != null)
						{
							candidates.Add((new Entry { BiomeIndex = b, Biome = biome, Source = detail, Role = PaletteRole.Detail, Slot = SlotDetail + d }, layer, 3));
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
		/// The fallback path slices: x earth, y road, z cobbles (the most-covering biome's earth and road), −1 for one the
		/// palette lacks. Where a pixel's ground has its own biome's, <see cref="PathLayerMap"/> wins.
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
		/// </summary>
		public float[] PathLayerMap(string slot, float[] coverage)
		{
			var map = new float[MaximumLayers];
			var share = new float[MaximumLayers];
			for (int i = 0; i < map.Length; i++)
			{
				map[i] = -1f;
				share[i] = float.NegativeInfinity;
			}
			foreach (Entry entry in entries)
			{
				if (entry.Role == PaletteRole.Path || entry.LayerIndex < 0 || entry.LayerIndex >= MaximumLayers)
				{
					continue;
				}
				float biomeShare = coverage != null && entry.BiomeIndex < coverage.Length ? coverage[entry.BiomeIndex] : 0f;
				if (biomeShare <= share[entry.LayerIndex])
				{
					continue;
				}
				foreach (Entry own in byBiome[entry.BiomeIndex])
				{
					if (own.Role == PaletteRole.Path && own.Slot == slot)
					{
						map[entry.LayerIndex] = own.LayerIndex;
						share[entry.LayerIndex] = biomeShare;
						break;
					}
				}
			}
			return map;
		}

		private static TerrainLayer Resolve(Func<BiomeTemplate, string, TerrainTextureLayer, TerrainLayer> resolve, BiomeTemplate biome, string slot, TerrainTextureLayer layer)
		{
			return layer != null && layer.HasAlbedo ? resolve(biome, slot, layer) : null;
		}
	}
}
#endif
