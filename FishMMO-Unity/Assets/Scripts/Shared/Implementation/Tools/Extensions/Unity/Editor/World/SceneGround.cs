#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A scene's ground as the generator writes it: the planet's ground, sampled, then shaped by
	/// every pass the request asks for.
	/// </summary>
	/// <remarks>
	/// One place, so the generator and anything checking its work (<see cref="SceneCutSnapshot"/>)
	/// cannot disagree about what the ground should be. Every shaping pass goes here, in order, on the
	/// stitched grid before any tile exists.
	/// </remarks>
	public static class SceneGround
	{
		/// <summary>Samples and shapes a scene's ground.</summary>
		/// <param name="request">The scene being cut.</param>
		/// <param name="plan">Its tile grid.</param>
		/// <param name="system">Its solar system, for the climate, the rock and the sea.</param>
		/// <param name="notes">Receives what each pass did, or why it did not run; may be null.</param>
		/// <remarks>Main thread: the passes read assets while they set up, then do their arithmetic on every core.</remarks>
		public static SceneHeightField Shape(SceneGenerationRequest request, TerrainTilePlan plan, SolarSystemProfile system, List<string> notes)
		{
			return Shape(request, plan, system, notes, out _);
		}

		/// <summary>The same, also handing back what erosion found: where the plateaus are and what rock is under them.</summary>
		/// <param name="erosion">What erosion did; null when it did not run.</param>
		public static SceneHeightField Shape(SceneGenerationRequest request, TerrainTilePlan plan, SolarSystemProfile system, List<string> notes,
			out SceneErosionReport erosion)
		{
			return Shape(request, plan, system, notes, out erosion, out _);
		}

		/// <summary>The same, also handing back the rivers and lakes laid into the ground.</summary>
		/// <param name="water">The scene's rivers and lakes, laid; null when none were (erosion off, underground, no body).</param>
		/// <remarks>
		/// The water is worked out from the planet's drainage (<see cref="PlanetDrainage"/>) on the ground
		/// as the plateaus leave it, laid into it before the rain wears it so the gullies grow toward the
		/// rivers, and laid again on the worn ground past erosion's edge fade (<see cref="SceneWater"/>).
		/// </remarks>
		public static SceneHeightField Shape(SceneGenerationRequest request, TerrainTilePlan plan, SolarSystemProfile system, List<string> notes,
			out SceneErosionReport erosion, out SceneWater water)
		{
			erosion = null;
			water = null;
			SceneHeightField field = SceneHeightField.Sample(request, plan);
			if (!request.Erosion)
			{
				// The planet's ground exactly: no rain wears it and no river is laid into it (SceneGenerationRequest.Rivers).
				return field;
			}
			if (request.Layer != null && request.Layer.Underground)
			{
				notes?.Add("Erosion skipped: an underground layer has no rain.");
				return field;
			}

			// How it wears, read from the ground before anything has worn it.
			SceneTerrainProcess process = SceneTerrainProcess.Build(request, plan, field.MetresAt, system);
			var settings = new ErosionSettings();
			settings.Aeolian = AeolianFor(request, system);
			// The scene's own dial: how deep rivers, glaciers and rain cut, together.
			float strength = Mathf.Max(0f, request.ErosionStrength);
			settings.Strength *= strength;
			settings.Landscape.RiverErodibility *= strength;
			settings.Glacial.CutMetres *= strength;
			SceneWater laid = null;
			Func<SceneHeightField, float[]> lay = null;
			if (request.Rivers)
			{
				lay = ground =>
				{
					// Roots hold a river's banks: bare ground lets it spread wide and shallow.
					laid = SceneWater.Build(request, system, ground, null, (east, north) => process.ProcessAt(east, north).VegetationCohesion);
					return laid.Any ? laid.Lay(ground) : null;
				};
			}
			erosion = SceneErosion.Apply(field, process, settings, BaseLevel(request, system), SceneGenerator.SceneSeed(request), lay);
			notes?.Add(erosion.ToString());
			UnityEngine.Debug.Log($"[Scene generator] '{request.SceneName}' {erosion}");
			if (lay != null && laid == null)
			{
				// Erosion did not run, so nothing laid the water: it still runs where the planet sends it.
				lay(field);
			}
			if (laid != null)
			{
				/* The water again, from the eroded ground. What was laid before the rain only guided it (the
				 * gullies grow toward those rivers and lakes); erosion has since cut valleys deeper, filled others
				 * and moved their floors, and a river kept on its old line and levels hangs over a valley cut under
				 * it, with its boulders in the air, or crosses ground it no longer runs down. Carving only lowers
				 * the ground, so the old levels could never be put right afterwards: the lines are snapped to the
				 * eroded floors and their surfaces and beds worked out from them, then cut. */
				if (laid.Any)
				{
					SceneWater final = SceneWater.Build(request, system, field, null, (east, north) => process.ProcessAt(east, north).VegetationCohesion);
					if (final.Any)
					{
						final.Finish(field);
						laid = final;
					}
					else
					{
						laid.Finish(field);
					}
				}
				notes?.AddRange(laid.Notes);
				UnityEngine.Debug.Log($"[Scene generator] '{request.SceneName}' {string.Join(" ", laid.Notes)}");
				water = laid;
			}
			return field;
		}

		/// <summary>
		/// The wind's settings for a scene: its prevailing wind, from the body's own belts at the scene's
		/// latitude, turned into the scene's axes. Null on a world with no air to blow.
		/// </summary>
		public static AeolianSettings AeolianFor(SceneGenerationRequest request, SolarSystemProfile system)
		{
			if (request.Body == null || request.Body.Atmosphere == AtmosphereKind.None)
			{
				return null;
			}
			PlanetClimateField climate = PlanetClimateField.For(system, request.Body);
			Vector2 wind = FishMMO.Shared.Weather.WeatherDriver.PrevailingWind((float)request.Latitude, climate.Moisture.Belts);

			/* East and north on the globe at the scene's centre, as scene axes: the footprint's heading
			 * turns the scene, so the wind is measured the way the ground was laid. */
			AtlasFootprint footprint = request.Footprint;
			double radius = request.ResolvedRadiusKm;
			Vector3 centre = AtlasGeometry.SceneToUnit(footprint, 0.0, 0.0, radius).ToVector3();
			Vector3 alongX = AtlasGeometry.SceneToUnit(footprint, 1.0, 0.0, radius).ToVector3() - centre;
			Vector3 alongZ = AtlasGeometry.SceneToUnit(footprint, 0.0, 1.0, radius).ToVector3() - centre;
			Vector3 east = AtlasGeometry.ToUnit(request.Latitude, request.Longitude + 0.01).ToVector3() - AtlasGeometry.ToUnit(request.Latitude, request.Longitude).ToVector3();
			Vector3 north = AtlasGeometry.ToUnit(request.Latitude + 0.01, request.Longitude).ToVector3() - AtlasGeometry.ToUnit(request.Latitude, request.Longitude).ToVector3();
			Vector3 world = east.normalized * wind.x + north.normalized * wind.y;
			var scene = new Vector2(Vector3.Dot(world, alongX.normalized), Vector3.Dot(world, alongZ.normalized));
			if (scene.sqrMagnitude < 1e-8f)
			{
				scene = Vector2.right;
			}
			scene.Normalize();
			return new AeolianSettings { WindX = scene.x, WindZ = scene.y };
		}

		/// <summary>
		/// World Y of the liquid standing in the scene's low ground — a sea, a magma ocean or lava lakes
		/// — or negative infinity where none does: where running water ends.
		/// </summary>
		public static float BaseLevel(SceneGenerationRequest request, SolarSystemProfile system)
		{
			if (request.Body == null)
			{
				return float.NegativeInfinity;
			}
			SurfaceLiquid liquid = SurfaceLiquids.For(system, request.Body, out float levelMetres);
			return liquid == SurfaceLiquid.None ? float.NegativeInfinity : levelMetres * request.VerticalScale;
		}
	}
}
#endif
