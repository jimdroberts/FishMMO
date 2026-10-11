#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The ice a generated scene's land carries where its summer never thaws: the ground of the Ice Sheet biome.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why it is ground.</b> Land whose warmest month stays below freezing keeps every snowfall, and the
	/// snow compacts into glacier ice that buries it (Köppen's ice cap, EF). The biome alone would paint the
	/// old ground white; the ice is tens of metres thick, so it is laid on the terrain as the Ice Shelf's is,
	/// and gets collision, footing, painting and cliffs for nothing.
	/// </para>
	/// <para>
	/// <b>Thickness from the summer, point by point.</b> Thicker the further the summer stays below
	/// freezing, nothing at the 0 °C summer line — so on land the sheet thins out to a margin instead of
	/// ending in a wall, and where it meets the sea while still cold it meets it at full thickness: the
	/// calving front, an ice cliff straight out of the water, as at every Antarctic coast. The summer read is
	/// the biome resolver's own (<see cref="PlanetClimateField.SelectionPoint"/>, the same noise), so the ice
	/// and the Ice Sheet biome painted over it share one edge.
	/// </para>
	/// <para>
	/// Only on land (a frozen sea is the Ice Shelf's: <see cref="SceneGeneration.IceShelfMetres(uint, float, float)"/>),
	/// only on a world with water to make ice of, and only once the Ice Sheet biome exists — before the spec
	/// table has created it, ice would be painted as whatever grows there.
	/// </para>
	/// </remarks>
	public readonly struct IceSheetSurface
	{
		/// <summary>The biome this ice is the ground of.</summary>
		public const string BiomeName = "Ice Sheet";

		/// <summary>The thickest the sheet stands above the ground it buries, scene metres: the height of a calving front.</summary>
		/// <remarks>Tidewater ice cliffs stand 20–60 m above the water (Antarctic shelf fronts, Svalbard and Greenland tidewater glaciers).</remarks>
		public const float MaxThicknessMetres = 40f;

		/// <summary>How far below freezing the summer must stay for the full thickness, kelvin.</summary>
		public const float FullThicknessKelvin = 4f;

		/// <summary>How much the wind-glazed swells of the surface rise and fall at full thickness, scene metres.</summary>
		public const float SwellMetres = 2f;

		/// <summary>Metres between the swells, roughly.</summary>
		public const float SwellSpacingMetres = 140f;

		private readonly bool active;
		private readonly PlanetClimateField field;
		private readonly float offset;

		/// <summary>Main thread only (registry and body lookups); <see cref="ThicknessMetres"/> is then safe anywhere.</summary>
		public IceSheetSurface(SolarSystemProfile system, WorldBody body, bool frozenSeas)
		{
			field = PlanetClimateField.For(system, body);
			active = body != null
				&& !frozenSeas
				&& field.Conditions.HasWater
				&& BiomeRegistry.Contains(BiomeName);
			offset = ((body != null ? body.ResolvedTerrainSeed : 1u) & 0xFFFF) * 0.311f;
		}

		/// <summary>True when this scene's world can carry an ice sheet at all.</summary>
		public bool Active => active;

		/// <summary>
		/// The ice on the land at a point, scene metres: 0 off the sheet or under water.
		/// </summary>
		/// <param name="direction">The point as a unit vector from the body's centre.</param>
		/// <param name="planetAltitudeMetres">The planet's own altitude there, in its metres: what the climate's lapse reads.</param>
		/// <param name="sceneAltitudeMetres">The scene's ground there before the ice: at or below 0 is sea, which takes none.</param>
		public float ThicknessMetres(Vector3 direction, float planetAltitudeMetres, float sceneAltitudeMetres, float eastMetres, float northMetres)
		{
			if (!active || sceneAltitudeMetres <= 0f)
			{
				return 0f;
			}
			double latitude = PlanetClimateField.LatitudeOf(direction);
			float warmest = field.WarmestSeasonAt(latitude, direction, planetAltitudeMetres);
			field.SelectionNoise(direction, out _, out float temperatureNoise, out _);
			float room = Mathf.Clamp01((1f - Mathf.Abs(warmest)) / PlanetClimateField.SelectionTemperature);
			warmest += temperatureNoise * PlanetClimateField.SelectionTemperature * room;
			float cover = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(-warmest / (FullThicknessKelvin / (float)ClimateModel.KelvinPerUnit)));
			if (cover <= 0f)
			{
				return 0f;
			}
			float swell = Mathf.PerlinNoise(eastMetres / SwellSpacingMetres + offset, northMetres / SwellSpacingMetres - offset) * 2f - 1f;
			return cover * (MaxThicknessMetres + swell * SwellMetres);
		}
	}
}
#endif
