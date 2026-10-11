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
				SolarSystemProfile system = SolarSystemProfile.Resolve(planet);
				// The orbit's mean, the one absolute figure the field and the conditions read.
				double insolation = ClimateModel.MeanInsolation(system, planet);
				double meanK = ClimateModel.MeanSurfaceKelvin(system, planet);
				float mean = (float)ClimateModel.ToScaleUnclamped(meanK);
				Debug.Log($"[Surface probe] {planet.ResolvedName}: system={(system != null ? system.name : "NONE")} " +
					$"insolation={insolation:0.###} mean={meanK:0.#} K ({mean:+0.00;-0.00}) tilt={planet.AxialTiltDegrees:0.#}");

				// The field itself, both hemispheres: the year's mean and the warmest season, exactly as biomes read them.
				PlanetClimateField field = PlanetClimateField.For(system, planet);
				foreach (double lat in new[] { 85.0, 70.0, 55.0, 40.0, 20.0, 0.0, -20.0, -40.0, -55.0, -70.0, -85.0 })
				{
					Vector3 dir = PlanetSurface.Direction(lat, 0.0);
					float regional = field.RegionalOffset(dir);
					Debug.Log($"[Surface probe]   lat {lat,5:0}: yearly latTerm={field.YearlyMeanLatitudeTemperature(lat):+0.00;-0.00} " +
						$"summer latTerm={field.WarmestLatitudeTemperature(lat):+0.00;-0.00} regional={regional:+0.000;-0.000} " +
						$"-> sea-level T {field.TemperatureAt(lat, dir, 0f):+0.00;-0.00}, warmest season {field.WarmestSeasonAt(lat, dir, 0f):+0.00;-0.00}");
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
					$"tilt={body.AxialTiltDegrees:0.#} cratering={PlanetSurface.CrateringOf(body):0.##} " +
					$"sea={profile.SeaLevel:0.###} range={profile.Range:0.###} -> {(baked != null ? PlanetSurfaceBaker.BakedImagePath(body.name) : "NOT BAKED")}");
			}
			AssetDatabase.SaveAssets();
		}
	}
}
#endif
