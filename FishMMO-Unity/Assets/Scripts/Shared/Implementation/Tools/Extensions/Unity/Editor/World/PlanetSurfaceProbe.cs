#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Bakes one body of each kind and says what came out, for looking at the result rather than
	/// reasoning about it.
	/// </summary>
	/// <remarks>
	/// A surface is judged by eye — whether an ice cap reads as a cap or as a stripe, whether a
	/// moon looks cratered, whether a gas giant looks like gas — and none of that is reachable from
	/// a number. This bakes a representative few instead of all of them, because the question is
	/// usually about one world and waiting for thirty is how a tool stops being used.
	/// </remarks>
	public static class PlanetSurfaceProbe
	{
		[DashboardTool(DashboardToolAttribute.Validate, "Probe: bake one of each body kind", Section = "World", Order = 21,
			Tooltip = "Bakes one rocky planet, one moon and one gas giant, and logs where the images landed.")]
		public static void Run()
		{
			WorldBody planet = null, moon = null, giant = null;
			foreach (WorldBody body in WorldEditorAssets.FindAll<WorldBody>())
			{
				if (body == null)
				{
					continue;
				}
				if (body.Kind == WorldBodyKind.GasGiant)
				{
					giant = giant ?? body;
				}
				else if (body.Kind == WorldBodyKind.Moon)
				{
					moon = moon ?? body;
				}
				else if (body.Atmosphere != AtmosphereKind.None && body.Water > 0.2f)
				{
					planet = planet ?? body;
				}
			}

			/* What the shader is actually told, latitude by latitude. Reconstructing this by hand
			 * from the orbit and the star is guesswork; asking the same functions the bake asks is
			 * not. A straight ice edge is either the latitude term doing nothing or the regional
			 * term being too small to bend it, and these numbers say which. */
			if (planet != null)
			{
				SolarSystemProfile system = WorldEditorAssets.FindFirst<SolarSystemProfile>();
				double insolation = system != null ? CelestialMath.Insolation(system, planet, 0.0) : 1.0;
				double meanK = ClimateModel.MeanSurfaceKelvin(insolation, planet.Atmosphere, planet.Water);
				float mean = (float)ClimateModel.ToScaleUnclamped(meanK);
				Debug.Log($"[Surface probe] {planet.ResolvedName}: system={(system != null ? system.name : "NONE")} " +
					$"insolation={insolation:0.###} mean={meanK:0.#} K ({mean:+0.00;-0.00}) tilt={planet.AxialTiltDegrees:0.#}");

				uint seed = planet.ResolvedTerrainSeed;
				foreach (double lat in new[] { 0.0, 20.0, 40.0, 55.0, 70.0, 85.0 })
				{
					double latTerm = system != null ? CelestialMath.LatitudeTemperature(system, planet, 0.0, lat) : 0.0;
					Vector3 dir = PlanetSurface.Direction(lat, 0.0);
					float regional = (PlanetSurface.FieldNoise(seed ^ 0x51CEEDA7u, dir, 2.6f) - 0.5f) * 2f * 0.09f;
					Debug.Log($"[Surface probe]   lat {lat,5:0}: latTerm={latTerm:+0.00;-0.00} regional={regional:+0.000;-0.000} " +
						$"-> sea-level T {mean + latTerm + regional:+0.00;-0.00}");
				}
			}

			foreach (WorldBody body in new List<WorldBody> { planet, moon, giant })
			{
				if (body == null)
				{
					continue;
				}
				Texture2D baked = PlanetSurfaceBaker.Bake(body);
				PlanetSurface.PlanetProfile profile = PlanetSurface.ProfileOf(body.ResolvedTerrainSeed, body);
				Debug.Log($"[Surface probe] {body.ResolvedName}: kind={body.Kind} atm={body.Atmosphere} water={body.Water:0.##} " +
					$"tilt={body.AxialTiltDegrees:0.#} cratering={PlanetSurface.CrateringOf(body.Atmosphere):0.##} " +
					$"sea={profile.SeaLevel:0.###} range={profile.Range:0.###} -> {(baked != null ? PlanetSurfaceBaker.BakedImagePath(body.name) : "NOT BAKED")}");
			}
			AssetDatabase.SaveAssets();
		}
	}
}
#endif
