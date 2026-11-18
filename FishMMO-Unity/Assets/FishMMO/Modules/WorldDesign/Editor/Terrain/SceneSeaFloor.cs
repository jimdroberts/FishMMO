#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Biomes;
using FishMMO.Water;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What a scene's coast and sea floor ask of its sea at the cut and on every repaint (2026-10-25, phase I2m): the tide its coast
	/// biomes ask (decision 45: an estuary's funnelled tide, <see cref="BiomeTemplate.TideStrength"/>) written to the sea's
	/// <see cref="WaterEnvironment"/>, and how fast the daylight dims on the way down to its floor (decision 36 (c)) written to its
	/// <see cref="WaterSurface.FloorDaylightExtinction"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Behaviour-preserving.</b> Every template's tide strength is 1 and every template's Sea Floor is Shore until its round says
	/// otherwise: then the coast's strength is exactly 1, the tide settings written are the component's own defaults, and the
	/// extinction is 0, so nothing a scene shows changes.
	/// </para>
	/// <para>
	/// <b>Hand tuning is kept.</b> The tide is written only over settings this arithmetic wrote or the defaults
	/// (<see cref="BiomeSeaFloors.IsAuthoredTide"/>); a sea somebody tuned by hand keeps its tide (and its tide is what the paint reads).
	/// </para>
	/// </remarks>
	public static class SceneSeaFloor
	{
		/// <summary>The spacing of the coast samples, metres.</summary>
		public const float CoastSpacingMetres = 16f;

		/// <summary>Ground within this many metres of mean sea level, either way, is the coast whose biomes ask the tide.</summary>
		public const float CoastBandMetres = 2f;

		/// <summary>
		/// Writes the scene sea's tide and floor daylight from its biomes, and returns how far its tide moves it either way, metres
		/// (what the splat's depth bands in tides and an estuary's low water read: <see cref="BiomeSplatOptions.TideMetres"/>).
		/// </summary>
		public static float Apply(Scene scene, SceneBiomeField field, Terrain[,] terrains, TerrainTilePlan plan, List<string> notes)
		{
			WaterSurface sea = null;
			if (scene.IsValid())
			{
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					if (root.TryGetComponent(out WaterSurface surface) && surface.Liquid == WaterLiquid.Water)
					{
						sea = surface;
						break;
					}
				}
			}
			if (sea == null || field == null || field.Biomes.Count == 0)
			{
				return BiomeSeaFloors.DefaultTideMetres;
			}

			// The tide the coast asks.
			float tide = BiomeSeaFloors.DefaultTideMetres;
			if (sea.TryGetComponent(out WaterEnvironment environment))
			{
				var strengths = new float[field.Biomes.Count];
				for (int b = 0; b < strengths.Length; b++)
				{
					strengths[b] = field.Biomes[b] != null ? field.Biomes[b].TideStrength : 1f;
				}
				float strength = BiomeSeaFloors.CoastStrength(strengths, CoastShares(field, terrains, plan));
				if (BiomeSeaFloors.IsAuthoredTide(environment.CoastalAmplification, environment.MaximumTideMetres))
				{
					float amplification = BiomeSeaFloors.Amplification(strength), metres = BiomeSeaFloors.TideMetres(strength);
					if (!Mathf.Approximately(environment.CoastalAmplification, amplification) || !Mathf.Approximately(environment.MaximumTideMetres, metres))
					{
						environment.CoastalAmplification = amplification;
						environment.MaximumTideMetres = metres;
						EditorUtility.SetDirty(environment);
						notes?.Add($"Sea: the coast's tide strength ×{strength:0.##} sets the tide to {metres:0.##} m (amplification {amplification:0.##}).");
					}
				}
				else if (!Mathf.Approximately(strength, 1f))
				{
					notes?.Add($"Sea: the coast asks a tide strength of ×{strength:0.##}, but the sea's tide was tuned by hand and is kept.");
				}
				tide = environment.MaximumTideMetres;
			}

			// The daylight on the floor.
			float extinction = FloorExtinction(field);
			if (!Mathf.Approximately(sea.FloorDaylightExtinction, extinction))
			{
				sea.FloorDaylightExtinction = extinction;
				EditorUtility.SetDirty(sea);
				notes?.Add(extinction > 0f
					? $"Sea: its floor's daylight dims by {extinction:0.####} a metre (half gone at {Mathf.Log(2f) / extinction:0} m)."
					: "Sea: no sea-floor biome, so its floor's daylight is not dimmed.");
			}
			return tide;
		}

		/// <summary>
		/// The scene's floor daylight extinction (<see cref="BiomeSeaFloors.DaylightExtinction"/>) over its sea-floor biomes by their
		/// coverage; 0 when it has none.
		/// </summary>
		public static float FloorExtinction(SceneBiomeField field)
		{
			float[] coverage = field.Coverage();
			var looks = new List<(BiomeSeaLook, float)>();
			for (int b = 0; b < field.Biomes.Count && b < coverage.Length; b++)
			{
				BiomeTemplate biome = field.Biomes[b];
				if (biome != null && biome.SeaFloor != BiomeSeaFloor.Shore)
				{
					looks.Add((biome.SeaLook, coverage[b]));
				}
			}
			return BiomeSeaFloors.DaylightExtinction(looks);
		}

		/// <summary>Each biome's share of the coast: its weights summed over the ground within <see cref="CoastBandMetres"/> of mean sea level.</summary>
		private static float[] CoastShares(SceneBiomeField field, Terrain[,] terrains, TerrainTilePlan plan)
		{
			var shares = new float[field.Biomes.Count];
			var weights = new float[field.Biomes.Count];
			int nx = Mathf.Max(1, Mathf.FloorToInt(plan.WidthMetres / CoastSpacingMetres));
			int nz = Mathf.Max(1, Mathf.FloorToInt(plan.DepthMetres / CoastSpacingMetres));
			for (int z = 0; z < nz; z++)
			{
				float north = -plan.DepthMetres * 0.5f + (z + 0.5f) * CoastSpacingMetres;
				for (int x = 0; x < nx; x++)
				{
					float east = -plan.WidthMetres * 0.5f + (x + 0.5f) * CoastSpacingMetres;
					if (Mathf.Abs(SceneGenerator.GroundAltitude(terrains, plan, east, north)) > CoastBandMetres)
					{
						continue;
					}
					field.WeightsAt(east, north, weights);
					for (int b = 0; b < shares.Length; b++)
					{
						shares[b] += weights[b];
					}
				}
			}
			return shares;
		}
	}
}
#endif
