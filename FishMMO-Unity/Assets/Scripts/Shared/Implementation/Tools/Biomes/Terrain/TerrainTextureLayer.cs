using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>How a texture layer blends with the others in its biome.</summary>
	[Serializable]
	public enum TextureBlendMode
	{
		Linear,
		Bilinear,
	}

	/// <summary>What a ground is made of, as the planet's looks see it (<see cref="TerrainTextureLayer.GroundMakeup"/>).</summary>
	[Serializable]
	public struct SurfaceMakeup
	{
		[Tooltip("Off: not set — the layer's name decides what it is made of, as before 2026-10-15, with a warning.")]
		public bool Set;
		[Tooltip("Living plant matter: grass sward, moss, heath (1); litter and peat partly. Wears the planet's and the patch's leaves.")]
		[Range(0f, 1f)] public float Living;
		[Tooltip("Lichen and coral: wears the patch's lichen colour.")]
		[Range(0f, 1f)] public float Lichen;
		[Tooltip("Rock, sand, soil, ash: takes the world's own ground colour where its air is thin or absent. Snow and ice keep most of their white (0.3).")]
		[Range(0f, 1f)] public float Mineral;
	}

	/// <summary>
	/// The ways across one ground layer of a biome (<see cref="TerrainTextureLayer.ways"/>): the path, road and cobble grounds a
	/// way is surfaced with where this ground is the strongest under it. Each empty one is the biome's own slot (Small Path,
	/// Road, Cobbles), as every ground was before 2026-10-16.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Decision 19 (2026-10-16, the Taiga round): "a biome's detail layer may carry its own road/path surface (e.g. Taiga
	/// corduroy log road only across bog layers, gravel elsewhere)". The scene cut lays each override into the terrain arrays
	/// beside the biome's own way grounds and points the ground layer's column of the layer table at it, so the terrain draws a
	/// road over this ground with this ground's road; the way's shape (trail, cart track, road, street) still picks which of the
	/// three it wears, exactly as it does with the biome's.
	/// </para>
	/// <para>
	/// Read on the biome's main and detail layers only: cliffs, beds and bars carry no ways of their own, and the ways' own
	/// slots (Small Path, Road, Cobbles) are the biome's. Terrain layer assets rather than whole texture layers, because a way
	/// ground is never painted or scattered on (only its art and tiling are read) and a texture layer cannot hold another.
	/// Biome art authoring fills them from the biome's spec (a layer's Ways) and keeps any somebody set by hand.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct GroundWays
	{
		[Tooltip("The ground a footpath, trail or cart track wears to across this ground. Empty: the biome's Small Path layer.")]
		public TerrainLayer path;
		[Tooltip("The ground a road is surfaced with across this ground. Empty: the biome's Road layer.")]
		public TerrainLayer road;
		[Tooltip("The paving of a street, highway or old road across this ground. Empty: the biome's Cobbles layer.")]
		public TerrainLayer cobbles;

		/// <summary>True when no way has a ground of its own here.</summary>
		public bool IsEmpty => path == null && road == null && cobbles == null;
	}

	/// <summary>
	/// One terrain texture of a biome — its maps, tiling, blend noise, and the height and slope
	/// bands it is confined to — plus the prefab spawn rules that apply where it dominates.
	/// Field names match the WorldEditor asset layout so exported biome templates load unchanged.
	/// </summary>
	[Serializable]
	public class TerrainTextureLayer
	{
		[Header("Terrain Layer")]
		/// <remarks>
		/// <para>
		/// The one place a layer's art is swapped. Generated ground textures arrive as
		/// <see cref="TerrainLayer"/> assets and are assigned here; replacing them with bought or
		/// painted art is assigning a different asset, and no generator ever writes over a layer
		/// whose asset is not one it made.
		/// </para>
		/// <para>
		/// A TerrainLayer rather than more fields because it already carries everything the ground
		/// shader reads — normal scale, tile offset and the mask and diffuse remaps — and because
		/// it is what Unity's own terrain tools paint with, so a designer touching up a generated
		/// scene by hand paints with exactly the layers the generator used.
		/// </para>
		/// </remarks>
		[Tooltip("The layer's art. When set, this asset IS the layer: its textures, tiling, normal scale and remaps are what the ground draws, and the texture and material fields below are ignored. Leave empty to have one built from those fields.")]
		public TerrainLayer terrainLayer;

		[Header("Textures")]
		public Texture2D albedoTexture;
		public Texture2D normalTexture;
		public Texture2D maskTexture;

		[Header("Material Properties")]
		[Range(0f, 1f)] public float metallic = 0f;
		[Range(0f, 1f)] public float smoothness = 0f;

		[Header("Tiling")]
		public Vector2 tileSize = Vector2.one * 15f;

		[Header("Blending Configuration")]
		[Tooltip("Blending mode for this texture layer.")]
		public TextureBlendMode blendMode = TextureBlendMode.Linear;
		[Tooltip("Scale of the noise pattern used for blending this texture with others in the same biome.")]
		[Range(1f, 256f)] public float blendNoiseScale = 64f;
		[Tooltip("Adds a random offset to the noise pattern to avoid repetition.")]
		public float blendNoiseOffsetX = 0f;
		public float blendNoiseOffsetY = 0f;
		[Tooltip("Controls the sharpness of the transition. Higher values create harder edges between textures.")]
		[Range(0.1f, 10f)] public float blendSharpness = 2.0f;

		[Header("Height Constraint")]
		[Tooltip("Constrains the texture to a specific height range (normalized 0-1).")]
		public bool useHeightConstraint = false;
		[MinMaxRange(0f, 1f)]
		public MinMaxRange heightRange = new MinMaxRange(0f, 1f);
		[Tooltip("How sharply the texture blends at the edges of its height range.")]
		[Range(0.001f, 0.2f)] public float heightFalloff = 0.05f;

		[Header("Slope Constraint")]
		[Tooltip("Constrains the texture to a specific slope range in degrees (0-90).")]
		public bool useSlopeConstraint = false;
		[MinMaxRange(0f, 90f)]
		public MinMaxRange slopeRange = new MinMaxRange(0f, 90f);
		[Tooltip("How sharply the texture blends at the edges of its slope range.")]
		[Range(1f, 20f)] public float slopeFalloff = 5f;

		/// <remarks>
		/// <para>
		/// What the ground in this slot is made of, as the planet's looks see it (2026-10-15: moved off the layer's NAME, which
		/// BiomeLooks.GroundShareOf read by keywords): x the share of living plant matter (grass sward, moss, heath: it wears
		/// the planet's and the patch's leaves), y of lichen and coral (the lichen colour), z of mineral (rock, sand, soil, ash:
		/// the world's own ground colour where its air is thin or absent). A frozen ground keeps most of its own white (a small
		/// mineral share); lava none of anything (it glows its own colour).
		/// </para>
		/// <para>
		/// A property of the art in the slot: the biome art authoring writes the generated ground's own (SurfaceCatalogue
		/// .MakeupOf) whenever the slot holds generated art, and keeps a value somebody typed. Not set, the terrain array bake
		/// has none for the layer and the runtime reads the layer's name, as before, with a warning. Hand-made art needs one
		/// typed here (and Set ticked).
		/// </para>
		/// <para>
		/// A struct with its own Set flag rather than a sentinel value, so a slot serialised before the field existed reads as
		/// "not set" however the deserialiser fills it in (all zeros is a real makeup: lava's).
		/// </para>
		/// </remarks>
		[Header("Ground Makeup")]
		[Tooltip("What this ground is made of for the planet's looks. Written by Author biome art for generated grounds; for hand-made art, tick Set and type it.")]
		[SerializeField] private SurfaceMakeup groundMakeup;

		/// <summary>What the ground is made of (x living, y lichen, z mineral); only meaningful when <see cref="HasGroundMakeup"/>.</summary>
		public Vector3 GroundMakeup
		{
			get => new Vector3(groundMakeup.Living, groundMakeup.Lichen, groundMakeup.Mineral);
			set => groundMakeup = new SurfaceMakeup { Set = true, Living = value.x, Lichen = value.y, Mineral = value.z };
		}

		/// <summary>True when <see cref="GroundMakeup"/> holds a makeup rather than "not set".</summary>
		public bool HasGroundMakeup => groundMakeup.Set;

		/// <summary>Forgets the makeup ("not set": the layer's name decides).</summary>
		public void ClearGroundMakeup() => groundMakeup = default;

		/// <remarks>
		/// <para>
		/// <b>A ground that changes with the local climate</b> (2026-10-18, decision 29, the Hills round: "temperature bands on
		/// ground layers so a biome's ground changes with climate"): one biome stretching from a cold valley head to a warm
		/// mouth paints its frost turf only where the year is cold and its burnt slope only where it is warm. With the band on,
		/// the layer's strength at a point is multiplied by <see cref="BiomeClimateBand.Weight(Vector2, float, Vector2, float, float, float)"/>
		/// at the scene's placement climate there (the mean annual temperature at the ground's true place on its globe and its
		/// true altitude, and the humidity): the very climate a spawn rule's climate band reads, with the same numbers meaning
		/// the same place, so a reach's ground and the plants authored for it change over together.
		/// </para>
		/// <para>
		/// On a DETAIL layer the band confines its patches, like its height and slope bands. On the MAIN layer it says where
		/// the base ground stands: outside its band the biome's detail layers carry the ground between them (in proportion to
		/// their strengths there), and where no detail does the main layer stays. To swap the main ground for another under a
		/// climate, give a climate variant a texture override instead (<see cref="BiomeClimateVariant.TextureLayerOverrides"/>).
		/// Off (the default) the layer paints exactly as it did before the band existed; a scene with no placement climate
		/// (a hand-painted one) paints every band as if it were off. Cliffs, beds, bars and way grounds ignore it.
		/// </para>
		/// </remarks>
		[Header("Climate Band")]
		[Tooltip("Main and detail layers: paint this ground only within a band of the local climate (the scene's mean annual temperature at the ground's true place and altitude, and the humidity there; the same climate spawn rules' climate bands read). Off = everywhere its other bands allow, as before. Written by Author biome art from the spec (a layer's Temp/Humid); set by hand to override.")]
		public bool useClimateBand = false;
		[Tooltip("Climate band only. Mean annual temperature the ground is painted between, on the climate's scale: 0 = 0 °C, 1 = 33.1 °C, −1 = −33.1 °C. x the coldest, y the warmest. An end at or past ±1 is open.")]
		public Vector2 temperatureRange = new Vector2(-1f, 1f);
		[Tooltip("Climate band only. Width of the soft edge at each closed end of the temperature range, centred on the end (0.1 ≈ 3.3 °C). 0 = a hard line.")]
		[Range(0f, 1f)] public float temperatureFalloff = BiomeClimateBand.DefaultFalloff;
		[Tooltip("Climate band only. Humidity the ground is painted between, −1 driest … 1 wettest. x the driest, y the wettest. An end at or past ±1 is open.")]
		public Vector2 humidityRange = new Vector2(-1f, 1f);
		[Tooltip("Climate band only. Width of the soft edge at each closed end of the humidity range, centred on the end. 0 = a hard line.")]
		[Range(0f, 1f)] public float humidityFalloff = BiomeClimateBand.DefaultFalloff;

		/// <summary>True when the layer's climate band is on and closes at least one end, so the climate changes its strength.</summary>
		public bool HasClimateBand => useClimateBand && (BiomeClimateBand.Closes(temperatureRange) || BiomeClimateBand.Closes(humidityRange));

		/// <summary>How fully the layer is painted in a climate, by its band: 1 without one (or for a NaN reading, no climate known).</summary>
		public float ClimateWeight(float temperature, float humidity)
		{
			return useClimateBand
				? BiomeClimateBand.Weight(temperatureRange, temperatureFalloff, humidityRange, humidityFalloff, temperature, humidity)
				: 1f;
		}

		/// <remarks>
		/// <b>A ground for one kind of sky</b> (2026-10-18, decision 44, the Dust Sea round: "AIR BANDS on ground layers, so one biome
		/// carries an Airless and a Thin palette"): with a band set, the layer is in a scene's palette only when the scene's world
		/// has one of the airs ticked (BiomeAir.Admits), its spawn rules with it. A main layer whose air the world lacks hands its
		/// place to the first detail layer whose band names that air (it is painted as the main ground: grey moondust on an airless
		/// moon, rust dust under a thin sky); with no such detail the main layer stays. Main, detail and cliff layers; beds, bars and
		/// way grounds ignore it (a ground layer's own ways carry its air's road). None ticked (the default) = every air, as before.
		/// </remarks>
		[Tooltip("Main, detail and cliff layers: paint this ground only on worlds with one of these airs (the scene's body). A main layer whose air the world lacks gives its place to the first air-banded detail layer that names the world's air. Nothing ticked = every world, as before. Written by Author biome art from the spec (a layer's Air); set by hand to override.")]
		public BiomeAtmosphereRequirement airBand;

		/// <summary>True when the layer is painted on a world of this air (<see cref="airBand"/>; an unknown air, None, passes).</summary>
		public bool AdmitsAir(BiomeAtmosphereRequirement worldAir) => BiomeAir.Admits(airBand, worldAir);

		/// <remarks><b>A ground for one kind of world</b> (2026-10-25, decision 68): with a band set, a main, detail or cliff layer is in a scene's
		/// palette only on worlds that meet its Requires and none of its Forbids (liquid water on the surface…). A main layer so refused hands
		/// its place to the first air- or world-banded detail the world admits. Nothing ticked (the default) = every world, as before.</remarks>
		[Tooltip("Main, detail and cliff layers: paint this ground only on worlds that meet every Requires flag and no Forbids flag (the scene's body), e.g. Requires Liquid Water. Nothing ticked = every world, as before. Written by Author biome art from the spec (a layer's World); set by hand to override.")]
		public BiomeWorldBand worldBand;

		/// <summary>True when the layer is painted on this world (<see cref="worldBand"/>; an unknown world, null, passes).</summary>
		public bool AdmitsWorld(BiomeWorldConditions? world) => !world.HasValue || worldBand.Admits(world.Value);

		/// <remarks>
		/// <b>A ground of the open land or of the caves</b> (2026-10-25, phase I2n, decision 57: "an UNDERGROUND band on ground layers:
		/// one template carries surface and interior palettes"): a cave biome's mud and gravel for its caves' floors beside the grounds
		/// it paints where it is the open land. Both (the default) is what every layer was: painted on the terrain and, where its
		/// kind fits, laid on the floors of the caves the biome dresses inside (CaveRockLayers). Surface: the terrain only. Interior:
		/// the caves only, chosen there before any other layer of its kind; a detail or cliff layer so banded is not painted on the
		/// terrain (a main layer always is), and a linked interior's Interior cliff is the inner wall of an ice shell.
		/// </remarks>
		[Tooltip("Where this ground is laid: Both (the terrain, and the floors of the caves this biome dresses inside, as before), Surface (the terrain only) or Interior (the caves only: not painted on the terrain unless it is the main layer; a cave's floors take it first; an Interior cliff walls an ice cave's ice). Written by Author biome art from the spec (a layer's Underground); set by hand to override.")]
		public BiomeUnderground underground;

		/// <remarks>
		/// <b>A ground of the hot ground</b> (2026-10-18, phase I2q, decision 47): with the band on, a DETAIL layer is painted only
		/// within a band of the scene's heat mask (baked at the cut from its geothermal sites and their runoff, 0 cold … 1 a
		/// spring's throat): a geyser basin's white sinter apron near the vents, its thermophile mats in the warm ring, its green
		/// halo. The band multiplies the layer's patches like its height and slope bands. Main layers, cliffs, beds and ways ignore
		/// it (a cold main ground under hot details is how the hot ground reads). Off (the default) paints as before.
		/// </remarks>
		[Header("Heat Band")]
		[Tooltip("Detail layers: paint this ground only within a band of the ground's heat (the scene's heat mask from its geothermal sites: 0 cold … 1 boiling). Off = everywhere its other bands allow, as before. Written by Author biome art from the spec (a layer's Heat); set by hand to override.")]
		public bool useHeatBand = false;
		[Tooltip("Heat band only. The heat this ground is painted between, 0 … 1; an end at or below 0, or at or above 1, is open.")]
		public Vector2 heatRange = new Vector2(0f, 1f);
		[Tooltip("Heat band only. Width of the soft edge at each closed end, centred on the end. 0 = a hard line.")]
		[Range(0f, 1f)] public float heatFalloff = BiomeHeatBand.DefaultFalloff;

		/// <summary>True when the layer's heat band is on and closes an end, so the heat changes its strength.</summary>
		public bool HasHeatBand => useHeatBand && BiomeHeatBand.Closes(heatRange);

		/// <summary>How fully the layer is painted at a heat, by its band: 1 without one (or for a NaN reading, no heat known).</summary>
		public float HeatWeight(float heat) => useHeatBand ? BiomeHeatBand.Weight(heatRange, heatFalloff, heat) : 1f;

		/// <remarks>
		/// <b>A ground of one part of a landform</b> (2026-10-25, phase I2g-b, decision 55: "LANDFORM ZONE MASK as a general ground/spawn
		/// BAND"): with the band on, a DETAIL layer is painted only on (or, turned round, only off) one part of its biome's sculpted
		/// landforms: a crater's rays, a basin's mare or wrinkle ridges, a cell field's troughs. It multiplies the layer's patches like
		/// its heat band. Main layers, cliffs, beds and ways ignore it. Off (the default) paints as before.
		/// </remarks>
		[Header("Landform Zone Band")]
		[Tooltip("Detail layers: paint this ground only on (or off) one part of its biome's sculpted landforms (BiomeLandformBand). Off = everywhere its other bands allow, as before. Written by Author biome art from the spec (a layer's Zone); set by hand to override.")]
		public BiomeLandformBand landformBand;

		/// <remarks>
		/// <para>
		/// <b>A ground of one depth</b> (2026-10-19, phase I2m, decisions 38 and 45: "ground layers get DEPTH bands in metres below
		/// the water surface", "the depth bands become SIGNED and may be measured in tide units"): with the band on, the layer is
		/// painted only where the ground lies between two depths below mean sea level, negative above it: a reef's live coral from
		/// 1 to 30 m, its rubble from 12 to 40 m, a kelp reef in the lit water; and, in tides (1 the lowest tide's depth, −1 the
		/// highest tide's height), a salt marsh's pioneer zone just under mean high water and its high marsh above it. Metres read
		/// the same on every scene, where a normalised height band would follow the scene's vertical scale.
		/// </para>
		/// <para>
		/// On a DETAIL layer the band multiplies its patches like its height and slope bands. On the MAIN layer it says where the
		/// base ground stands: outside it the biome's details carry the ground between them, and where none does the main layer
		/// stays (never a hole), as a main layer's climate band does. Read on any biome's main and detail layers, above the sea or
		/// below it; under the sea they paint only in a biome whose Sea Floor lets them (<see cref="BiomeSeaFloor"/>). Cliffs,
		/// beds, bars and ways ignore it (a bed layer has its own depth band, below its own water). On a world with no sea every
		/// depth band passes. Off (the default) the layer paints exactly as before the band existed.
		/// </para>
		/// </remarks>
		[Header("Sea Depth Band")]
		[Tooltip("Main and detail layers: paint this ground only between two depths below mean sea level (negative above it): a reef's coral from 1 to 30 m, a salt marsh's zones by the tide. Off = at every depth its other bands allow, as before. Written by Author biome art from the spec (a layer's Depth or Tide); set by hand to override.")]
		public bool useSeaDepthBand = false;
		[Tooltip("Sea depth band only. The depth below mean sea level this ground is painted between (negative: above it), x the shallowest (highest), y the deepest; metres, or tides when In Tides is ticked. An end at or past ±11000 is open.")]
		public Vector2 seaDepthRange = new Vector2(-BiomeSeaFloors.OpenDepth, BiomeSeaFloors.OpenDepth);
		[Tooltip("Sea depth band only. Width of the soft edge at each closed end, centred on the end (half the ground at the stated depth); in the band's own units. 0 = a hard line.")]
		[Range(0f, 50f)] public float seaDepthFalloff = BiomeSeaFloors.DefaultDepthFalloff;
		[Tooltip("Sea depth band only. Read the range in tides rather than metres: 1 is the depth of the lowest tide, −1 the height of the highest, 0 mean sea level (the scene sea's tide reach, after its coast's tide strength).")]
		public bool seaDepthInTides = false;

		/// <summary>True when the layer's sea depth band is on and closes an end, so the depth changes its strength.</summary>
		public bool HasSeaDepthBand => useSeaDepthBand && BiomeSeaFloors.Closes(seaDepthRange);

		/// <summary>
		/// How fully the layer is painted where the ground lies <paramref name="depthMetres"/> below mean sea level (negative above it)
		/// on a scene whose tide moves the sea <paramref name="tideMetres"/> either way: 1 without a band, or with NaN (no sea).
		/// </summary>
		public float SeaDepthWeight(float depthMetres, float tideMetres)
		{
			if (!useSeaDepthBand)
			{
				return 1f;
			}
			float depth = seaDepthInTides ? BiomeSeaFloors.InTides(depthMetres, tideMetres) : depthMetres;
			return BiomeSeaFloors.DepthBand(seaDepthRange, seaDepthFalloff, depth);
		}

		/// <remarks>
		/// <b>A ground measured from the lava</b> (2026-10-25, phase I2r "molten ground", decision 65 (b)): with the band on, a main or
		/// detail layer is painted only where the ground stands between two heights above the scene's LAVA surface (negative below
		/// it): a lava lake's glassy shore bench from 0 to 2 m, its spatter up to 8 m. It multiplies the layer's patches as the sea
		/// depth band does, and on the main layer says where the base ground stands (the details carry the ground outside it). On a
		/// scene with no lava surface every lava band passes. Off (the default) paints as before.
		/// </remarks>
		[Header("Lava Height Band")]
		[Tooltip("Main and detail layers: paint this ground only between two heights above the scene's lava surface (negative: below it). Off = at every height its other bands allow, as before; a scene with no lava passes every band. Written by Author biome art from the spec (a layer's Lava); set by hand to override.")]
		public bool useLavaBand = false;
		[Tooltip("Lava band only. The height above the lava surface this ground is painted between, m (x the lowest, y the highest); an end at or past ±11000 is open.")]
		public Vector2 lavaRange = new Vector2(-BiomeSeaFloors.OpenDepth, BiomeSeaFloors.OpenDepth);
		[Tooltip("Lava band only. Width of the soft edge at each closed end, centred on the end, m. 0 = a hard line.")]
		[Range(0f, 50f)] public float lavaFalloff = BiomeSeaFloors.DefaultDepthFalloff;

		/// <summary>True when the layer's lava band is on and closes an end, so the height above the lava changes its strength.</summary>
		public bool HasLavaBand => useLavaBand && BiomeSeaFloors.Closes(lavaRange);

		/// <summary>
		/// How fully the layer is painted where the ground stands <paramref name="aboveLavaMetres"/> over the lava's surface (negative
		/// below it): 1 without a band, or with NaN (no lava). The sea depth band's rule, measured upward from the lava.
		/// </summary>
		public float LavaWeight(float aboveLavaMetres)
		{
			if (!useLavaBand || float.IsNaN(aboveLavaMetres))
			{
				return 1f;
			}
			return BiomeSeaFloors.DepthBand(lavaRange, lavaFalloff, aboveLavaMetres);
		}

		/// <remarks>
		/// <b>A ground that glows</b> (2026-10-25, phase I2r, decision 65 (a)): along the seams of a crust that overturns on the shared
		/// clock, or over the whole layer from the world's heat (<see cref="BiomeGroundGlow"/>). Baked into the scene's terrain arrays
		/// with the layer (TerrainArraySet.LayerGlow) and drawn by the terrain shader, and written into the lava's molten mask. None
		/// (the default) draws as before.
		/// </remarks>
		[Header("Glow")]
		[Tooltip("How this ground glows: None (as before), Seams (crust plates overturning on the shared clock, their seams incandescent) or Whole (the whole layer at the world's temperature). Written by Author biome art from the spec (a layer's Glows); set by hand to override. Takes effect when the scene's terrain arrays are baked again (in place).")]
		public BiomeGroundGlow glow;

		[Header("Ways Across This Ground")]
		[Tooltip("Main and detail layers only: the path, road and cobble grounds a way takes where this ground is the strongest under it. Each empty one is the biome's own slot. Written by Author biome art from the spec; set by hand to override.")]
		public GroundWays ways;

		[Header("Prefab Spawning")]
		[Tooltip("Prefab spawn rules that become active wherever this texture dominates.")]
		public List<PrefabSpawnRule> prefabSpawnRules = new List<PrefabSpawnRule>();

		/// <summary>True when the layer has a texture to paint with, from its terrain layer or its own field.</summary>
		public bool HasAlbedo => (terrainLayer != null && terrainLayer.diffuseTexture != null) || albedoTexture != null;
	}
}
