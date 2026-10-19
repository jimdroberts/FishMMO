using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Which worlds a spawn rule grows on (<see cref="PrefabSpawnRule.worldGate"/>): their air, their mean temperature and what
	/// they are (<see cref="BiomeWorldRequirement"/>). Everything at its default is no gate: the rule grows on every world its
	/// biome is chosen on, exactly as before the gate existed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Decision 41 (2026-10-18, the Cryovolcanic Plain round): "a per-spawn-rule WORLD GATE (world air mask, minimum/maximum world
	/// temperature, world flags such as InGiantMagnetosphere) so condition-gated fantasy flora (decision 7) can be authored". One
	/// biome is painted on many worlds (a Crater on an Earth-like world and on an airless moon; a Dust Sea under a thin sky and
	/// under none), and the plants it grows cannot all be right on every one: a glowing lichen that lives only in a thin sky, a
	/// frost candelabra fed by a giant planet's radiation belts.
	/// </para>
	/// <para>
	/// The gate reads the WORLD (<see cref="BiomeWorldConditions"/>: the scene's body, the same conditions the biome resolver
	/// chose its biomes by), so across a scene it is one answer: a rule is grown or it is not. Where a rule must also change
	/// across a scene, its climate band does that (<see cref="PrefabSpawnRule.useClimateBand"/>). The gate draws no random number
	/// and moves nothing else: a rule it refuses is skipped as a rule with no prefabs is.
	/// </para>
	/// <para>
	/// Each part is only what the body data already says: the air from <see cref="BiomeWorldConditions.Atmosphere"/>, the mean
	/// temperature from <see cref="BiomeWorldConditions.MeanTemperature"/> (the climate's scale, unclamped: 0 freezing, about
	/// 33 K a unit), and the world's flags from <see cref="BiomeWorldConditions.Meets"/> (ice world, cryovolcanic, in a giant's
	/// magnetosphere, rock surface…), the very flags a biome template's <see cref="BiomeTemplate.Requires"/> names.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct BiomeWorldGate
	{
		/// <summary>The temperature end at or past which a gate's temperature range is open (±10 on the scale is below 0 K / above 600 K).</summary>
		public const float OpenTemperature = 10f;

		[Tooltip("The worlds' air the rule grows under. Nothing ticked = any air (no air gate).")]
		public BiomeAtmosphereRequirement air;

		[Tooltip("What the world must be for the rule to grow (all ticked must hold): an ice world, cryovolcanic, in a giant planet's magnetosphere, rock surfaced… Nothing ticked = any world.")]
		public BiomeWorldRequirement requires;

		[Tooltip("Grow only on worlds whose MEAN surface temperature lies within the range below. Off = any world's temperature.")]
		public bool useTemperature;

		[Tooltip("World temperature only. The world's mean surface temperature the rule grows between, on the climate's scale, unclamped: 0 = 0 °C, 1 = 33 K warmer, −1 = 33 K colder (an ice moon is near −5, a runaway greenhouse near +5). x the coldest, y the warmest; an end at or past ±10 is open.")]
		public Vector2 temperatureRange;

		/// <summary>A gate that admits every world: the default.</summary>
		public static BiomeWorldGate Open => default;

		/// <summary>True when the gate admits every world (no air, no flags, no temperature): the rule is exactly what it was.</summary>
		public bool IsOpen => air == BiomeAtmosphereRequirement.None && requires == BiomeWorldRequirement.None && !ClosesTemperature;

		/// <summary>True when the temperature range is on and closes at least one end.</summary>
		public bool ClosesTemperature => useTemperature
			&& (Mathf.Min(temperatureRange.x, temperatureRange.y) > -OpenTemperature || Mathf.Max(temperatureRange.x, temperatureRange.y) < OpenTemperature);

		/// <summary>True when the rule grows on a world with these conditions.</summary>
		public bool Admits(in BiomeWorldConditions world)
		{
			if (air != BiomeAtmosphereRequirement.None && (air & BiomeAir.MaskOf(world.Atmosphere)) == 0)
			{
				return false;
			}
			if (ClosesTemperature)
			{
				float low = Mathf.Min(temperatureRange.x, temperatureRange.y), high = Mathf.Max(temperatureRange.x, temperatureRange.y);
				if (float.IsNaN(world.MeanTemperature) || low > -OpenTemperature && world.MeanTemperature < low || high < OpenTemperature && world.MeanTemperature > high)
				{
					return false;
				}
			}
			return requires == BiomeWorldRequirement.None || world.Meets(requires);
		}

		/// <summary>
		/// The gate as text, empty for an open gate: what the authoring fingerprints a rule's gate by. Temperatures to 1/10 000,
		/// open ends as "-".
		/// </summary>
		public string Signature()
		{
			if (IsOpen)
			{
				return string.Empty;
			}
			var text = new StringBuilder(48);
			text.Append('a').Append((int)air).Append("|r").Append((int)requires);
			if (ClosesTemperature)
			{
				float low = Mathf.Min(temperatureRange.x, temperatureRange.y), high = Mathf.Max(temperatureRange.x, temperatureRange.y);
				text.Append("|t").Append(low > -OpenTemperature ? Q(low) : "-").Append(',').Append(high < OpenTemperature ? Q(high) : "-");
			}
			return text.ToString();
		}

		/// <summary>A gate on the air alone.</summary>
		public static BiomeWorldGate Air(BiomeAtmosphereRequirement air) => new BiomeWorldGate { air = air };

		/// <summary>
		/// A gate from its parts: the air (None = any), the world's mean temperature between <paramref name="minTemperature"/> and
		/// <paramref name="maxTemperature"/> (infinite or past ±<see cref="OpenTemperature"/> = open), the world's flags (None = any).
		/// </summary>
		public static BiomeWorldGate Of(BiomeAtmosphereRequirement air, float minTemperature = float.NegativeInfinity, float maxTemperature = float.PositiveInfinity,
			BiomeWorldRequirement requires = BiomeWorldRequirement.None)
		{
			bool closed = minTemperature > -OpenTemperature || maxTemperature < OpenTemperature;
			return new BiomeWorldGate
			{
				air = air,
				requires = requires,
				useTemperature = closed,
				temperatureRange = closed
					? new Vector2(Mathf.Max(-OpenTemperature, minTemperature), Mathf.Min(OpenTemperature, maxTemperature))
					: Vector2.zero,
			};
		}

		private static string Q(float value) => Math.Round(value * 10000.0).ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>The world's air as the biome content reads it: masks, breathable or not, how hard it moves a blade.</summary>
	/// <remarks>Pure; the same answers on the cut and at runtime.</remarks>
	public static class BiomeAir
	{
		/// <summary>The air a template that admits only these worlds offers no fire to (<see cref="OnlyThinAir"/>).</summary>
		public const BiomeAtmosphereRequirement ThinOrNone = BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin;

		/// <summary>An atmosphere as its one bit of <see cref="BiomeAtmosphereRequirement"/> (the resolver's own mapping).</summary>
		public static BiomeAtmosphereRequirement MaskOf(AtmosphereKind kind)
		{
			switch (kind)
			{
				case AtmosphereKind.None: return BiomeAtmosphereRequirement.Airless;
				case AtmosphereKind.Thin: return BiomeAtmosphereRequirement.Thin;
				case AtmosphereKind.Thick: return BiomeAtmosphereRequirement.Thick;
				default: return BiomeAtmosphereRequirement.Standard;
			}
		}

		/// <summary>
		/// True when people can breathe it and fires burn in it: real air (<see cref="BiomeAtmosphereRequirement.Living"/>, Earth-like
		/// or thick), never a thin trace or none. What a point-of-interest kind that needs breathable air asks (decision 39).
		/// </summary>
		public static bool IsBreathable(AtmosphereKind kind) => (MaskOf(kind) & BiomeAtmosphereRequirement.Living) != 0;

		/// <summary>
		/// True when a template admits only worlds of thin air or none (its <see cref="BiomeTemplate.Atmosphere"/> names Airless
		/// and/or Thin and nothing else): no fire burns on any world it is ever painted on (decision 41's light rule).
		/// </summary>
		public static bool OnlyThinAir(BiomeAtmosphereRequirement templateAtmosphere)
			=> templateAtmosphere != BiomeAtmosphereRequirement.None && (templateAtmosphere & ~ThinOrNone) == 0;

		/// <summary>
		/// True when a ground layer's air band (<see cref="TerrainTextureLayer.airBand"/>) admits a world's air: no band (None)
		/// admits every air, and an unknown world (None) passes every band.
		/// </summary>
		public static bool Admits(BiomeAtmosphereRequirement band, BiomeAtmosphereRequirement worldAir)
			=> band == BiomeAtmosphereRequirement.None || worldAir == BiomeAtmosphereRequirement.None || (band & worldAir) != 0;

		/// <summary>How much of the wind's sway a blade keeps under a world's air, Thin's share (decision 41: Thin ≈ 0.3).</summary>
		public const float ThinSway = 0.3f;

		/// <summary>
		/// How hard the world's air moves the grass, as a share of the wind's sway (decision 41, 2026-10-18: "blade SWAY follows the
		/// world's air density, none when airless"): 0 with no air, <see cref="ThinSway"/> under a thin sky, exactly 1 under
		/// Earth-like air (every scene sways as it always did) and under thick air (its winds are slow; a heavier push is not
		/// modelled).
		/// </summary>
		public static float Sway(AtmosphereKind kind)
		{
			switch (kind)
			{
				case AtmosphereKind.None: return 0f;
				case AtmosphereKind.Thin: return ThinSway;
				default: return 1f;
			}
		}
	}

	/// <summary>
	/// Which point-of-interest kinds need breathable air by default (decision 39, 2026-10-18): the authoring writes each biome's
	/// <see cref="BiomeSiteKind.NeedsBreathableAir"/> from this, a biome's file may say otherwise per kind, and the cut skips a
	/// kind whose entry needs it on a world whose air is not breathable (<see cref="BiomeAir.IsBreathable"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The kinds people live in or work at now (settlements, strongholds, works and mills, camps and lodges, coves, hermitages),
	/// worship at with living keepers (shrines, temples, monasteries), and the living groves and nests: nobody keeps a village
	/// or a herb garden on a world without air. Everything else is natural (falls, peaks, craters, vents, caves) or a relic
	/// that outlasts its builders (ruins, barrows, obelisks, wrecks, portals, crypts, battlefields), and stands on any world.
	/// </para>
	/// <para>
	/// No Earth-like or thick-aired scene changes (their air is breathable); a scene on a thin or airless world loses only the
	/// kinds listed here, and only once its biomes are re-authored and its sites regenerated.
	/// </para>
	/// </remarks>
	public static class BiomeSiteAir
	{
		private static readonly HashSet<POIType> Lived = new HashSet<POIType>
		{
			// Settlements, strongholds and works.
			POIType.Village, POIType.Town, POIType.City, POIType.Capital, POIType.Port, POIType.Keep, POIType.Castle, POIType.Fortress,
			POIType.TradingPost, POIType.Waystation, POIType.Mine, POIType.Quarry, POIType.Lighthouse, POIType.Dockyard,
			POIType.Windmill, POIType.Watermill, POIType.LumberMill, POIType.StiltVillage, POIType.Tower, POIType.Bridge,
			// Camps and lived-in wild sites.
			POIType.Camp, POIType.BanditCamp, POIType.HuntingLodge, POIType.LumberCamp, POIType.FishingCamp, POIType.SmugglersCove,
			POIType.PirateCove, POIType.WitchHut, POIType.Hermitage,
			// Kept holy places.
			POIType.Shrine, POIType.Temple, POIType.Monastery, POIType.BogShrine,
			// Living groves, gardens and nests.
			POIType.HerbGrove, POIType.AncientTree, POIType.Clearing, POIType.Oasis, POIType.Nest,
		};

		/// <summary>True when a kind needs breathable air unless a biome's file says otherwise.</summary>
		public static bool NeedsAirByDefault(POIType kind) => Lived.Contains(kind);

		/// <summary>
		/// How gladly a biome's entry sites its kind on a world whose air is (<paramref name="breathable"/>) or is not breathable: its
		/// weight (never below 0), or 0 where the air is not breathable and the entry needs it.
		/// </summary>
		public static float WeightOn(in BiomeSiteKind entry, bool breathable)
			=> breathable || !entry.NeedsBreathableAir ? Mathf.Max(0f, entry.Weight) : 0f;

		/// <summary>Every kind that needs it by default, in kind order (for reports and tests).</summary>
		public static IEnumerable<POIType> DefaultKinds()
		{
			foreach (POIType kind in (POIType[])Enum.GetValues(typeof(POIType)))
			{
				if (Lived.Contains(kind))
				{
					yield return kind;
				}
			}
		}
	}

	/// <summary>
	/// The lights a biome that only exists under thin air or none is lit with: no flame burns there (decision 41, 2026-10-18), so
	/// its streets, caves and mines take the cold Glow kinds (<see cref="BiomeWayLightKind.Glowjar"/>, <see cref="BiomeWayLightKind.CrystalPost"/>,
	/// <see cref="BiomeWayLightKind.RuneStone"/>) in place of every lantern, torch and fire bowl, at the same places and spacing.
	/// </summary>
	public static class BiomeGlowLights
	{
		/// <summary>True for a light that gives off the cold Glow (no flame).</summary>
		public static bool Glows(BiomeWayLightKind light)
			=> light == BiomeWayLightKind.Glowjar || light == BiomeWayLightKind.CrystalPost || light == BiomeWayLightKind.RuneStone;

		/// <summary>
		/// <see cref="BiomeWayLights.Default"/> without flame: a village's torches become glowjars on their stakes, a town's,
		/// city's, capital's and port's lanterns crystal posts, at the same spacing. A fresh list each call, in kind order.
		/// </summary>
		public static List<BiomeWayLight> WayLights()
		{
			List<BiomeWayLight> list = BiomeWayLights.Default();
			for (int i = 0; i < list.Count; i++)
			{
				list[i] = new BiomeWayLight(list[i].Kind, list[i].Kind == POIType.Village ? BiomeWayLightKind.Glowjar : BiomeWayLightKind.CrystalPost, list[i].Spacing);
			}
			return list;
		}

		/// <summary>
		/// <see cref="BiomeCaveLights.Default"/> without flame: crystal posts mark a cave's route and stand in a mine's stopes,
		/// glowjars light its camps, line its drifts (at the wall's foot, at the drift spacing) and hang where its ways meet. A
		/// fresh list each call, in place order.
		/// </summary>
		public static List<BiomeCaveLight> CaveLights()
		{
			List<BiomeCaveLight> list = BiomeCaveLights.Default();
			for (int i = 0; i < list.Count; i++)
			{
				BiomeCaveLightPlace place = list[i].Place;
				BiomeWayLightKind light = place == BiomeCaveLightPlace.Route || place == BiomeCaveLightPlace.MineStope
					? BiomeWayLightKind.CrystalPost
					: BiomeWayLightKind.Glowjar;
				list[i] = new BiomeCaveLight(place, light, list[i].Spacing);
			}
			return list;
		}
	}
}
