#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using FishMMO.Shared.Biomes;
using A = FishMMO.Shared.Biomes.BiomeAtmosphereRequirement;
using R = FishMMO.Shared.Biomes.BiomeWorldRequirement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Where every climate-selected biome is chosen, and what each needs of its world, as one explicit
	/// table written onto the biome assets by a re-runnable Dashboard button.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the envelopes moved.</b> The authored envelopes were set before the climate they are
	/// matched against existed in its present form. Since then humidity gained its wind-driven half
	/// (<see cref="MoistureModel"/>), the field's temperature starts from the sub-solar point rather
	/// than the mean (<see cref="PlanetClimateField.SubSolarTemperature"/>), and the ocean's tiers
	/// became its real depth zones. Measured on an Earth-like body with all three in, 46% of land and
	/// 82% of the sea floor fell outside every envelope — the resolver was choosing by nearest miss —
	/// a dozen Earth biomes were never chosen at all, and about 9% of the globe went to biomes
	/// that cannot exist on an Earth-like world: Sulphur Flats across the hot interiors, Subsurface
	/// Ocean Vent in the polar seas, Tidal Fracture and Cryovolcanic Plain on cold coasts.
	/// </para>
	/// <para>
	/// <b>What the table achieves, measured</b> (the real <see cref="PlanetClimateField"/> on a synthetic
	/// Earth and on the project's own bodies, 10⁵–10⁶ points each, re-measured 2026-10-02 with the
	/// physical land zones and the field-derived water test): land outside every envelope 0.11% on
	/// the Earth-like body and 0.20% on Arthis, sea floor 0%; every climate-selected Earth biome
	/// chosen somewhere on the Earth-like body; the shore biomes 2% of its land, as Earth's
	/// low-lying coastal zone is; the alien biomes 0% of it; and every alien biome still chosen on
	/// the world it belongs to — vents, cryovolcanic plains, fractures, geyser fields and ice shelves
	/// on an ice moon, sulphur flats, fractures and molten ground on a volcanic one, tholin plains and
	/// methane seas on a methane world, nitrogen ice on a nitrogen world, regolith, rilles, dust seas
	/// and lava tubes on a dead airless rock, greenhouse plains and acid cloud decks on a runaway
	/// greenhouse.
	/// </para>
	/// <para>
	/// <b>Rilles and tidal fractures are not shore landforms</b>, so they are tier 9 — channels and
	/// fractures cut ground of every height — and since 2026-10-02 the resolver offers a tier-9 biome
	/// in EVERY tier, inside its height band, to win on climate fit and weight like the tier's own
	/// biomes (it used to offer them only where a world had no biome of the point's tier). On a cold
	/// airless moon every reading is the same corner of the climate plane (−1, −1), so weight alone
	/// decides there: both sit below the native biomes that share that corner — Rille 0.7, Tidal
	/// Fracture 0.75, under Impact Basin's 0.8 and every plain's 1 or more — and above half of
	/// anything (the most an envelope the reading falls outside can score). So they take the ground
	/// no native fits and never flood a world. Rilles keep their band of 0.42–0.75, the shore, plains
	/// and plateaus — lava channels are not cut into summits or basins: 7–9% of the Moon and the dead
	/// Soluris moons, 22% of thin-aired dry Mars, as before. Tidal fractures take an ice moon's low
	/// ground, shore and mountains (24% of Europa, 26% of Enceladus) and now a volcanic moon's
	/// mountains, where the warm-climate Volcanic biome only won by nearest miss (8% of Io, 10% of
	/// Helis, from 2%). Scree's band runs to the summit, so a world with no water for glaciers keeps
	/// bare rock there instead of whatever the last-resort pass found.
	/// </para>
	/// <para>
	/// <b>Molten Surface is where the rock is molten</b> (<see cref="BiomeWorldRequirement.MoltenRock"/>):
	/// a magma ocean or lava lakes, the lava code's own test (<see cref="SurfaceLiquids"/>). It was a
	/// lowland plain gated on <c>Volcanic</c>, which an Earth-sized world is from its size alone, at any
	/// reading of +1 — so it took 30% of Rheis (324 K, airless, warm inside but not molten) from its
	/// regolith, and 30% of a runaway greenhouse, whose basalt set long ago under thick air. Now it is
	/// the lowest ground (tier 0) of a world whose melt stands open, where the lava lakes are, at any
	/// temperature, since the heat is the rock's and not the sunlight's: 23% of Helis (the Dust Sea's
	/// basins), none of Rheis (Regolith Plain back to 31%) or Venus (Runaway Greenhouse Plain back to
	/// 81%). Io's computed heat is exactly the lake threshold and the lava code asks for more than it,
	/// so Io itself has neither lava nor molten ground.
	/// </para>
	/// <para>
	/// <b>Ice Shelf is a frozen-through world's sea</b>: the scene raises the ground under the datum of a
	/// world frozen through to the shore tier, a few metres above it, and the shelf (tier 3, weight
	/// 1.05, needs <see cref="BiomeWorldRequirement.IceWorld"/>) outweighs Rocky Coast there — all of
	/// Galris's raised shelf, and the 1–2% of natural shore on every ice moon; none of any world with
	/// open water.
	/// </para>
	/// <para>
	/// <b>A frozen world with air is not an ice moon's surface.</b> Galris (210 K, a standard
	/// atmosphere, 13% water) used to read liquid seas; frozen through, it now takes Snow, High
	/// Desert, Alpine and ice, its frozen sea floor the Subsurface Ocean Vent over a warm interior and
	/// its shores, natural and raised, the Ice Shelf (Rocky Coast, which needs water to have a coast
	/// rather than liquid water, keeps the shores of a cold world with open seas). That is why
	/// Cryovolcanic Plain is airless or thin-aired only — every confirmed cryovolcanic surface is
	/// (Europa, Enceladus, Triton, Pluto's Wright Mons, Ceres' Ahuna Mons); under real air the same
	/// world snows — and why Sulphur Flats, Wasteland and Molten Surface also need a rock surface:
	/// none of them lies on an ice shell (Molten Surface needs molten rock, which no ice shell is).
	/// </para>
	/// <para>
	/// <b>Aliens are kept off by the world, not by the climate</b> (<see cref="BiomeTemplate.Requires"/>,
	/// <see cref="BiomeWorldRequirement"/>). Their envelopes are set where their own worlds' readings
	/// actually fall — an airless body reads humidity −1 everywhere, a moon past the frost line
	/// temperature −1 — rather than shaped to dodge an Earth climate they happen to resemble.
	/// </para>
	/// <para>
	/// <b>The tiers are landform bands at physical altitudes</b> (2026-10-02,
	/// <see cref="ClimateSettings.TierEdgeMetres"/>, Earth metres scaled by the body's relief): tier 3
	/// the shore to 30 m, tier 4 lowland and plateau to 1.5 km, tier 5 highland to 2.5 km, tier 6
	/// mountain to 3.5 km, tier 7 alpine to 4.5 km, tier 8 nival above. They used to be fractions of
	/// the summit, which made a third of all land coast (0–460 m). The treeline and the snowline are
	/// the temperature's, not the tiers': forest gives way to alpine at −0.45 and ice takes over
	/// below about −0.65 in every tier, the same thresholds the lowland's taiga, tundra and snow
	/// plain use. So the vegetation and the peat bogs are tier 4, the highland biomes (high desert,
	/// alpine meadow, valleys with geyser basins at their core, karst, hills) tier 5, mountain
	/// slopes, bare rock and alpine tier 6, scree and permanent ice tier 7, glaciers tier 8. Height
	/// bands are each tier's, except where a biome must also claim ground below its tier in the
	/// resolver's fallback pass: on a world with no sea, everything under the datum is a basin, and
	/// Impact Basin, Methane Lake and Subsurface Ocean Vent take it there (the vent the frozen
	/// shelf too), Runaway Greenhouse Plain claims every height there, and Scree the summits of a
	/// world with no water for glaciers. A tier-9 biome's band is where in every tier it competes:
	/// 0–1 for Tidal Fracture, the shore to the plateaus for Rille.
	/// </para>
	/// <para>
	/// <b>Explicit, never guessed.</b> Every value below is written out and matched on the exact
	/// asset name; nothing is inferred from a name or a description (a keyword guesser corrupted
	/// eight deliberately authored biomes in this project once). Running it again writes nothing once
	/// it has been applied, and every field it does change is logged before → after. Biomes not in
	/// the table — the hand-placed ones at weight 0 — are never touched.
	/// </para>
	/// <para>
	/// <b>A tabled biome with no asset is created</b> when <see cref="NewBiomes"/> says what it is
	/// called and how it looks on a map — its display name, description and colours, the identity
	/// no envelope can supply — and then written like any other. Only those: a missing name with no
	/// identity is reported, never invented. Creation happens once, since the next run finds the
	/// asset, and is logged. A new asset carries no naming data, art or Addressables entry: "Fill
	/// missing biome naming" gives it its voice (<see cref="BiomeNamingGenerator"/>), and the
	/// Addressables dashboard's smart grouping makes it load at runtime.
	/// </para>
	/// <para>
	/// The measuring harness is outside the project (an offline .NET build of the real sources); if
	/// the climate changes again, re-measure before editing a row, since every number here is a
	/// partition of that climate's actual distribution.
	/// </para>
	/// </remarks>
	public static class BiomeSpecTable
	{
		/// <summary>Where the biome assets live; an entry names the file without its extension.</summary>
		public const string Folder = "Assets/Templates/Entity/Biomes";

		/// <summary>One biome's tabled values.</summary>
		public readonly struct Entry
		{
			public readonly string Name;
			public readonly int ElevationTier;
			public readonly float MinHeight, MaxHeight;
			public readonly float MinTemperature, MaxTemperature;
			public readonly float MinHumidity, MaxHumidity;
			public readonly float SelectionWeight;
			public readonly BiomeAtmosphereRequirement Atmosphere;
			public readonly bool RequiresLiquidWater;
			public readonly BiomeWorldRequirement Requires;

			public Entry(string name, int tier, float minHeight, float maxHeight, float minTemperature, float maxTemperature,
				float minHumidity, float maxHumidity, float weight, BiomeAtmosphereRequirement atmosphere, bool requiresLiquidWater,
				BiomeWorldRequirement requires)
			{
				Name = name;
				ElevationTier = tier;
				MinHeight = minHeight;
				MaxHeight = maxHeight;
				MinTemperature = minTemperature;
				MaxTemperature = maxTemperature;
				MinHumidity = minHumidity;
				MaxHumidity = maxHumidity;
				SelectionWeight = weight;
				Atmosphere = atmosphere;
				RequiresLiquidWater = requiresLiquidWater;
				Requires = requires;
			}

			/// <summary>The asset this entry writes to.</summary>
			public string AssetPath => $"{Folder}/{Name}.asset";
		}

		/// <summary>What a biome the table creates is called and how it looks: the identity an envelope cannot supply.</summary>
		public readonly struct Identity
		{
			public readonly string Name;
			public readonly string DisplayName;
			public readonly string Description;
			public readonly Color BiomeColorId;
			public readonly Color GizmoColor;

			public Identity(string name, string displayName, string description, Color biomeColorId, Color gizmoColor)
			{
				Name = name;
				DisplayName = displayName;
				Description = description;
				BiomeColorId = biomeColorId;
				GizmoColor = gizmoColor;
			}
		}

		/// <summary>
		/// Tabled biomes the tool may create when their asset does not exist yet, by exact asset name.
		/// </summary>
		/// <remarks>
		/// Map colours are picked apart from every existing biome's (Ice Shelf's nearest, Ice Cave, is
		/// 0.16 away in RGB), so a biome map can tell them apart. The alien biomes' gizmo colour is
		/// their map colour, as here.
		/// </remarks>
		public static readonly Identity[] NewBiomes =
		{
			new Identity("Ice Shelf", "Ice Shelf",
				"A frozen sea: flat shelf ice to the horizon, pressure ridges and refrozen leads",
				new Color(0.55f, 0.8f, 0.92f, 1f), new Color(0.55f, 0.8f, 0.92f, 1f)),
		};

		/// <summary>The creation identity for a tabled name, if the table may create it.</summary>
		public static bool TryGetIdentity(string name, out Identity identity)
		{
			foreach (Identity candidate in NewBiomes)
			{
				if (candidate.Name == name)
				{
					identity = candidate;
					return true;
				}
			}
			identity = default;
			return false;
		}

		/// <summary>
		/// The table. Columns: name, tier, min/max height, min/max temperature, min/max humidity,
		/// selection weight, atmosphere, requires liquid water, world requirements.
		/// </summary>
		/// <remarks>
		/// Within a tier the envelopes tile the temperature × humidity plane the tier's ground
		/// actually reads, so nothing falls outside them; where two overlap, the one whose centre the
		/// reading is nearer wins, and a narrower niche (Bamboo Forest between temperate forest and
		/// jungle, Salt Flat in the driest hottest corner of the desert, Wetlands at the wet end of the
		/// forest) wins its own core. Temperature is −1 frozen … +1 scorching at about 33 K a unit, 0
		/// at freezing: +0.6 is 20 °C. On the sea floor the temperature is the water's at the surface.
		/// </remarks>
		public static readonly Entry[] Entries =
		{
			// Ocean floor. The temperature is the water's at the surface: the abyss under cold seas is the sediment-blanketed plain, canyons cut the cold wet slopes, seamount chains stand in the warm subtropical gyres.
			new Entry("Deep Ocean", 0, 0f, 0.2f, -0.3f, 1f, -1f, 1f, 1f, A.Living, true, R.None),
			new Entry("Abyssal Plain", 0, 0f, 0.2f, -1f, 0.1f, -1f, 1f, 1f, A.Living, true, R.None),
			new Entry("Ocean", 1, 0.2f, 0.35f, -1f, 1f, -1f, 1f, 1f, A.Living, true, R.None),
			new Entry("Underwater Canyon", 1, 0.2f, 0.35f, -1f, 0.3f, 0.5f, 1f, 0.8f, A.Living, true, R.None),
			new Entry("Seamount", 1, 0.2f, 0.35f, 0.3f, 1f, -1f, 0.6f, 0.8f, A.Living, true, R.None),
			new Entry("Coastal Water", 2, 0.35f, 0.42f, -1f, 1f, -1f, 1f, 1f, A.Living, true, R.None),
			new Entry("Coral Reef", 2, 0.35f, 0.42f, 0.5f, 1f, -0.2f, 1f, 1f, A.Living, true, R.None),

			// Shore (tier 3, the land up to 30 m: beaches, dune ridges, salt marsh, estuary flats): rocky where cold, sand where warm and dry, estuary and mangrove where wet.
			new Entry("Beach", 3, 0.42f, 0.45f, 0.1f, 1f, -1f, 0.4f, 1f, A.Living, true, R.None),
			new Entry("Rocky Coast", 3, 0.42f, 0.45f, -1f, 0.15f, -1f, 1f, 1f, A.Living, false, R.SurfaceWater),
			new Entry("Estuary", 3, 0.42f, 0.45f, -0.3f, 0.6f, 0.4f, 1f, 0.8f, A.Living, true, R.None),
			new Entry("Mangrove", 3, 0.42f, 0.45f, 0.55f, 1f, 0.4f, 1f, 1f, A.Living, true, R.None),

			// Lowland and plateau (tier 4, 30 m to 1.5 km): the vegetation, laid out the way Whittaker's diagram lays it out, with the peat bogs of the cold wet lowlands. The snow plain sits with the tundra at the coldest end; permanent ice belongs to the summits.
			new Entry("Snow", 4, 0.45f, 0.6f, -1f, -0.7f, -1f, 1f, 1f, A.Any, false, R.SurfaceWater),
			new Entry("Tundra", 4, 0.45f, 0.6f, -0.75f, -0.4f, -1f, 1f, 1f, A.Living, true, R.None),
			new Entry("Taiga", 4, 0.45f, 0.6f, -0.45f, 0.05f, -0.2f, 1f, 1f, A.Living, true, R.None),
			new Entry("Steppe", 4, 0.45f, 0.6f, -0.45f, 0.45f, -1f, -0.2f, 1f, A.Living, true, R.None),
			new Entry("Plains", 4, 0.45f, 0.6f, 0f, 0.45f, -0.25f, 0.05f, 1f, A.Living, true, R.None),
			new Entry("Grassland", 4, 0.45f, 0.6f, 0f, 0.45f, 0f, 0.25f, 1f, A.Living, true, R.None),
			new Entry("Woodland", 4, 0.45f, 0.6f, 0.05f, 0.6f, 0.15f, 0.45f, 0.8f, A.Living, true, R.None),
			new Entry("Forest", 4, 0.45f, 0.6f, 0f, 0.5f, 0.35f, 1f, 1f, A.Living, true, R.None),
			new Entry("Wetlands", 4, 0.45f, 0.6f, -0.1f, 0.5f, 0.8f, 1f, 1f, A.Living, true, R.None),
			new Entry("Peat Bog", 4, 0.45f, 0.6f, -0.55f, 0.1f, 0.55f, 1f, 1f, A.Living, true, R.None),
			new Entry("Bamboo Forest", 4, 0.45f, 0.6f, 0.45f, 0.65f, 0.4f, 0.9f, 1f, A.Living, true, R.None),
			new Entry("Jungle", 4, 0.45f, 0.6f, 0.6f, 1f, 0.35f, 1f, 1f, A.Living, true, R.None),
			new Entry("Savanna", 4, 0.45f, 0.6f, 0.45f, 1f, -0.1f, 0.35f, 1f, A.Living, true, R.None),
			new Entry("Scrubland", 4, 0.45f, 0.6f, 0.45f, 1f, -0.45f, -0.1f, 1f, A.Living, true, R.None),
			new Entry("Desert", 4, 0.45f, 0.6f, 0.05f, 1f, -1f, -0.45f, 1f, A.Living, false, R.None),
			new Entry("Badlands", 4, 0.45f, 0.6f, 0.2f, 0.7f, -0.75f, -0.35f, 0.8f, A.Living, false, R.None),
			new Entry("Salt Flat", 4, 0.45f, 0.6f, 0.55f, 1f, -1f, -0.7f, 1f, A.Living, false, R.None),
			new Entry("Oasis", 4, 0.45f, 0.6f, 0.5f, 0.9f, 0.8f, 1f, 0f, A.Living, true, R.None),

			// Highland (tier 5, 1.5 to 2.5 km): high desert where dry (volcanic plateaus where it is also warm and the world volcanic), alpine meadow above the treeline, valleys where wet with geyser basins in their core, karst where warm and wet, hills between.
			new Entry("High Desert", 5, 0.6f, 0.75f, -1f, 0.2f, -1f, -0.45f, 1f, A.Living, false, R.None),
			new Entry("Volcanic", 5, 0.6f, 0.75f, 0.2f, 1f, -1f, -0.45f, 1f, A.Any, false, R.Volcanic),
			new Entry("Alpine Meadow", 5, 0.6f, 0.75f, -1f, -0.45f, -0.45f, 1f, 1f, A.Living, true, R.None),
			new Entry("Hills", 5, 0.6f, 0.75f, -0.45f, 1f, -0.45f, 0f, 1f, A.Living, true, R.None),
			new Entry("Valley", 5, 0.6f, 0.75f, -0.45f, 0.2f, 0f, 1f, 1f, A.Living, true, R.None),
			new Entry("Geyser Basin", 5, 0.6f, 0.75f, -0.3f, 0.1f, 0f, 0.3f, 0.8f, A.Living, true, R.Volcanic),
			new Entry("Karst", 5, 0.6f, 0.75f, 0.2f, 1f, 0f, 1f, 1f, A.Living, true, R.None),

			// Mountain (tier 6, 2.5 to 3.5 km: mountain slopes where wetter, bare rock where dry, alpine above the treeline), alpine (tier 7, to 4.5 km) and nival (tier 8). Snow and ice need water on the world to be made of; scree's band runs to the summit, so a world with none keeps bare rock there.
			new Entry("Mountain Slope", 6, 0.75f, 0.9f, -0.45f, 1f, -0.3f, 1f, 1f, A.Any, false, R.None),
			new Entry("Rocky Terrain", 6, 0.75f, 0.9f, -0.45f, 1f, -1f, -0.2f, 1f, A.Any, false, R.None),
			new Entry("Alpine", 6, 0.75f, 0.9f, -1f, -0.45f, -1f, 1f, 1f, A.Living, false, R.None),
			new Entry("Scree", 7, 0.9f, 1f, -0.65f, 1f, -1f, 1f, 1f, A.Any, false, R.None),
			new Entry("Permanent Ice", 7, 0.9f, 0.95f, -1f, -0.65f, -1f, 1f, 1f, A.Any, false, R.SurfaceWater),
			new Entry("Glacier", 8, 0.95f, 1f, -1f, 0.3f, -1f, 1f, 1f, A.Any, false, R.SurfaceWater),

			// Alien biomes, kept off any world that is not theirs by BiomeTemplate.Requires; each envelope sits where its own world's readings fall (an airless body reads humidity -1, a moon past the frost line temperature -1). Impact Basin, Methane Lake and Subsurface Ocean Vent also claim the ground below their tier in the fallback pass: on a world with no sea, everything under the datum is a basin. Molten Surface is the lowest ground of a world with molten rock open at its surface — a magma ocean, or lava lakes over an interior heated past the lake threshold (R.MoltenRock, the lava code's own test) — where the melt stands; any temperature, since the heat is the rock's, not the sunlight's. Ice Shelf is the frozen sea of a world frozen through, whose below-datum ground the scene raises to the shore tier; it outweighs Rocky Coast there. Runaway Greenhouse Plain claims every height in the fallback pass.
			new Entry("Dust Sea", 0, 0f, 0.2f, -1f, 1f, -1f, -0.6f, 1f, A.Airless | A.Thin, false, R.RockSurface),
			new Entry("Molten Surface", 0, 0f, 0.2f, -1f, 1f, -1f, 1f, 1.1f, A.Any, false, R.MoltenRock),
			new Entry("Impact Basin", 1, 0f, 0.42f, -1f, 1f, -1f, -0.6f, 0.8f, A.Airless | A.Thin, false, R.None),
			new Entry("Subsurface Ocean Vent", 1, 0f, 0.42f, -1f, -0.3f, -1f, 1f, 1f, A.Any, false, R.Cryovolcanic),
			new Entry("Lava Tube", 2, 0.35f, 0.42f, -1f, 0.8f, -1f, -0.5f, 1f, A.Airless | A.Thin, false, R.NoLiquidWater | R.RockSurface),
			new Entry("Methane Lake", 2, 0f, 0.42f, -1f, -0.65f, -1f, 1f, 1f, A.Breathing, false, R.MethaneCycle),
			new Entry("Ice Shelf", 3, 0.42f, 0.45f, -1f, -0.4f, -1f, 1f, 1.05f, A.Any, false, R.IceWorld),
			new Entry("Regolith Plain", 4, 0.45f, 0.6f, -1f, 1f, -1f, -0.6f, 1f, A.Airless | A.Thin, false, R.RockSurface),
			new Entry("Wasteland", 4, 0.45f, 0.6f, -1f, 0.9f, -1f, -0.4f, 1.1f, A.Breathing, false, R.NoLiquidWater | R.RockSurface),
			new Entry("Radiation Plain", 4, 0.45f, 0.6f, -1f, 0.6f, -1f, -0.5f, 1.1f, A.Airless | A.Thin, false, R.GiantMagnetosphere),
			new Entry("Sulphur Flats", 4, 0.45f, 0.6f, -1f, 0.7f, -1f, 0f, 1.15f, A.Any, false, R.Volcanic | R.NoLiquidWater | R.RockSurface),
			new Entry("Cryovolcanic Plain", 4, 0.45f, 0.6f, -1f, -0.55f, -1f, 0.4f, 1.2f, A.Airless | A.Thin, false, R.Cryovolcanic),
			new Entry("Tholin Plain", 4, 0.45f, 0.6f, -1f, -0.5f, -1f, 0.4f, 1.2f, A.Breathing, false, R.CryogenicAir),
			new Entry("Runaway Greenhouse Plain", 4, 0f, 1f, 0.75f, 1f, -1f, -0.3f, 1.2f, A.Thick, false, R.RunawayGreenhouse),
			new Entry("Ice Geyser Field", 5, 0.6f, 0.75f, -1f, -0.6f, -1f, 0.6f, 1f, A.Airless | A.Thin, false, R.Cryovolcanic),
			new Entry("Nitrogen Ice Field", 5, 0.6f, 0.75f, -1f, -0.7f, -1f, 0.5f, 1f, A.Airless | A.Thin, false, R.FrozenNitrogen),
			new Entry("Sulphuric Cloud Deck", 6, 0.75f, 0.9f, 0.5f, 1f, -1f, 1f, 1.2f, A.Thick, false, R.RunawayGreenhouse),

			// Any elevation (tier 9): channels and fractures cut ground of every height, so they compete in every tier inside their height band, weighted below the native biomes they share a reading with: they take the ground no native fits rather than flood a world. Rilles cut an airless rock's shore, plains and plateaus (their band keeps them off the mountains and out of the basins); tidal fractures a flexed moon's low ground, shore and mountains.
			new Entry("Rille", 9, 0.42f, 0.75f, -1f, 0.6f, -1f, -0.6f, 0.7f, A.Airless | A.Thin, false, R.RockSurface),
			new Entry("Tidal Fracture", 9, 0f, 1f, -1f, 0.2f, -1f, 0.4f, 0.75f, A.Any, false, R.TidallyHeated),
		};

		[DashboardTool(DashboardToolAttribute.Maintenance, "Apply biome envelopes and world requirements", Section = "Content", Order = 3,
			Tooltip = "Writes the tabled elevation tier, height band, climate envelope, selection weight, atmosphere, liquid-water and world requirements onto the named biome assets, logging every field it changes, and creates the new biomes the table defines (Ice Shelf) if their asset does not exist yet. Re-running it changes nothing once applied; biomes not in the table are left alone.",
			Confirm = "Write the biome spec table onto the biome assets in Assets/Templates/Entity/Biomes, creating any new biome it defines that has no asset yet? Every changed field is logged before and after.")]
		public static void ApplyFromDashboard()
		{
			var log = new StringBuilder();
			int fields = Apply(true, log, out int biomes, out List<string> missing, out List<string> created);
			if (created.Count > 0)
			{
				Debug.Log($"[Biome spec] Created {created.Count} biome asset(s) in {Folder}: {string.Join(", ", created)}. " +
					"Run \"Fill missing biome naming\" to give them names, and the Addressables dashboard's smart grouping so they load at runtime.");
			}
			if (fields > 0)
			{
				Debug.Log($"[Biome spec] Changed {fields} field(s) on {biomes} biome(s) of {Entries.Length} tabled.\n{log}");
			}
			else
			{
				Debug.Log($"[Biome spec] All {Entries.Length - missing.Count} tabled biomes already match the table; nothing written.");
			}
			if (missing.Count > 0)
			{
				Debug.LogWarning($"[Biome spec] {missing.Count} tabled biome(s) have no asset at {Folder}/<name>.asset and were skipped: {string.Join(", ", missing)}.");
			}
		}

		/// <summary>
		/// Writes the table onto the assets. Returns how many fields changed.
		/// </summary>
		/// <param name="save">False to report what would change without writing anything.</param>
		/// <param name="log">Receives one line per changed field, before → after.</param>
		/// <param name="changedBiomes">How many biomes had at least one field changed.</param>
		/// <param name="missing">Tabled names with no asset and no creation identity: skipped.</param>
		/// <param name="created">Tabled names whose asset was created from <see cref="NewBiomes"/> (with
		/// <paramref name="save"/> false: would be).</param>
		public static int Apply(bool save, StringBuilder log, out int changedBiomes, out List<string> missing, out List<string> created)
		{
			missing = new List<string>();
			created = new List<string>();
			changedBiomes = 0;
			int fields = 0;
			foreach (Entry entry in Entries)
			{
				var biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(entry.AssetPath);
				if (biome == null)
				{
					if (!TryGetIdentity(entry.Name, out Identity identity))
					{
						missing.Add(entry.Name);
						continue;
					}
					created.Add(entry.Name);
					log?.AppendLine($"  {entry.Name}: created at {entry.AssetPath}");
					if (!save)
					{
						continue;
					}
					biome = Create(identity, entry.AssetPath);
				}
				if (Differences(biome, entry) == 0)
				{
					continue;
				}
				if (save)
				{
					Undo.RecordObject(biome, "Apply biome spec");
				}
				int changed = save ? ApplyTo(biome, entry, log) : Describe(biome, entry, log);
				if (save)
				{
					EditorUtility.SetDirty(biome);
				}
				fields += changed;
				changedBiomes++;
			}
			int registered = save ? RegisterUnaddressedBiomes(log) : CountUnaddressedBiomes(log);
			if (save && (fields > 0 || created.Count > 0 || registered > 0))
			{
				AssetDatabase.SaveAssets();
			}
			return fields;
		}

		/// <summary>
		/// Gives every biome asset in the folder that has none an Addressables entry. Returns how many.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why here.</b> A build loads biomes through Addressables and registers only what it loads,
		/// so a biome with no entry exists in the editor and silently not in the game: the resolver
		/// never offers it and a baked map cell naming it resolves to nothing. Assets got entries only
		/// when somebody opened them in the inspector, so the 23 biomes added by script on 2026-09-23
		/// — every alien among them — went unregistered, and so would anything this tool creates.
		/// Registering here makes applying the table also make it loadable.
		/// </para>
		/// <para>
		/// <b>Only the missing ones.</b> Registering an asset that already has an entry rewrites the
		/// group and marks the settings dirty, which would churn the group file on every run.
		/// </para>
		/// </remarks>
		public static int RegisterUnaddressedBiomes(StringBuilder log)
		{
			int registered = 0;
			foreach (BiomeTemplate biome in UnaddressedBiomes())
			{
				WorldEditorAssets.RegisterAddressable(biome);
				log?.AppendLine($"  {biome.name}: made addressable");
				registered++;
			}
			return registered;
		}

		private static int CountUnaddressedBiomes(StringBuilder log)
		{
			int count = 0;
			foreach (BiomeTemplate biome in UnaddressedBiomes())
			{
				log?.AppendLine($"  {biome.name}: would be made addressable");
				count++;
			}
			return count;
		}

		private static IEnumerable<BiomeTemplate> UnaddressedBiomes()
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				yield break;
			}
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate), new[] { Folder }))
			{
				if (settings.FindAssetEntry(guid) != null)
				{
					continue;
				}
				var biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (biome != null)
				{
					yield return biome;
				}
			}
		}

		/// <summary>
		/// A new biome asset carrying only its identity; the caller writes the entry onto it.
		/// </summary>
		/// <remarks>
		/// <c>CreateAsset</c> writes the file at once, so the asset (and the GUID every reference will
		/// carry) exists before any envelope field is written; the entry is then applied through the
		/// same logged path as an existing biome's, and a second run finds it and creates nothing.
		/// </remarks>
		public static BiomeTemplate Create(in Identity identity, string assetPath)
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.name = identity.Name;
			biome.DisplayName = identity.DisplayName;
			biome.Description = identity.Description;
			biome.BiomeColorId = identity.BiomeColorId;
			biome.GizmoColor = identity.GizmoColor;
			AssetDatabase.CreateAsset(biome, assetPath);
			// A biome a build cannot load does not exist in the game: see RegisterUnaddressedBiomes.
			WorldEditorAssets.RegisterAddressable(biome);
			return biome;
		}

		/// <summary>How many of the entry's fields the biome does not already hold.</summary>
		public static int Differences(BiomeTemplate biome, in Entry entry)
		{
			return Describe(biome, entry, null);
		}

		/// <summary>
		/// Writes one entry onto one biome. Returns how many fields changed, logging each before → after.
		/// </summary>
		/// <remarks>Exact comparisons: the table's floats round-trip through the asset unchanged, so an applied entry reads back equal and a second run writes nothing.</remarks>
		public static int ApplyTo(BiomeTemplate biome, in Entry entry, StringBuilder log)
		{
			return Walk(biome, entry, log, true);
		}

		private static int Describe(BiomeTemplate biome, in Entry entry, StringBuilder log)
		{
			return Walk(biome, entry, log, false);
		}

		private static int Walk(BiomeTemplate biome, in Entry e, StringBuilder log, bool write)
		{
			int n = 0;
			string name = e.Name;
			if (biome.ElevationTier != e.ElevationTier) { Note(log, name, "ElevationTier", biome.ElevationTier, e.ElevationTier); if (write) biome.ElevationTier = e.ElevationTier; n++; }
			if (biome.MinHeight != e.MinHeight) { Note(log, name, "MinHeight", biome.MinHeight, e.MinHeight); if (write) biome.MinHeight = e.MinHeight; n++; }
			if (biome.MaxHeight != e.MaxHeight) { Note(log, name, "MaxHeight", biome.MaxHeight, e.MaxHeight); if (write) biome.MaxHeight = e.MaxHeight; n++; }
			if (biome.MinTemperature != e.MinTemperature) { Note(log, name, "MinTemperature", biome.MinTemperature, e.MinTemperature); if (write) biome.MinTemperature = e.MinTemperature; n++; }
			if (biome.MaxTemperature != e.MaxTemperature) { Note(log, name, "MaxTemperature", biome.MaxTemperature, e.MaxTemperature); if (write) biome.MaxTemperature = e.MaxTemperature; n++; }
			if (biome.MinHumidity != e.MinHumidity) { Note(log, name, "MinHumidity", biome.MinHumidity, e.MinHumidity); if (write) biome.MinHumidity = e.MinHumidity; n++; }
			if (biome.MaxHumidity != e.MaxHumidity) { Note(log, name, "MaxHumidity", biome.MaxHumidity, e.MaxHumidity); if (write) biome.MaxHumidity = e.MaxHumidity; n++; }
			if (biome.SelectionWeight != e.SelectionWeight) { Note(log, name, "SelectionWeight", biome.SelectionWeight, e.SelectionWeight); if (write) biome.SelectionWeight = e.SelectionWeight; n++; }
			if (biome.Atmosphere != e.Atmosphere) { Note(log, name, "Atmosphere", biome.Atmosphere, e.Atmosphere); if (write) biome.Atmosphere = e.Atmosphere; n++; }
			if (biome.RequiresLiquidWater != e.RequiresLiquidWater) { Note(log, name, "RequiresLiquidWater", biome.RequiresLiquidWater, e.RequiresLiquidWater); if (write) biome.RequiresLiquidWater = e.RequiresLiquidWater; n++; }
			if (biome.Requires != e.Requires) { Note(log, name, "Requires", biome.Requires, e.Requires); if (write) biome.Requires = e.Requires; n++; }
			return n;
		}

		private static void Note(StringBuilder log, string biome, string field, object before, object after)
		{
			log?.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}.{1}: {2} → {3}", biome, field, before, after));
		}
	}
}
#endif
