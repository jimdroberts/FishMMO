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
		/// <summary>
		/// Told the ground and the water after each step of <see cref="Shape"/> (the step's name, the ground, the water laid
		/// so far or null), for a probe auditing where the ground took its shape. Null in a cut.
		/// </summary>
		public static Action<string, SceneHeightField, SceneWater> Observer;

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
			return Shape(request, plan, system, notes, out erosion, out water, out _);
		}

		/// <summary>The same, also handing back the sculpted landforms carved into the ground (2026-10-18, I2g).</summary>
		/// <param name="landforms">What the landform stage placed and carved; null when no biome names a landform (or erosion did not run).</param>
		public static SceneHeightField Shape(SceneGenerationRequest request, TerrainTilePlan plan, SolarSystemProfile system, List<string> notes,
			out SceneErosionReport erosion, out SceneWater water, out SceneLandformRecord landforms)
		{
			erosion = null;
			water = null;
			landforms = null;
			SceneHeightField field = SceneHeightField.Sample(request, plan);
			Observer?.Invoke("sampled from the planet", field, null);
			// The open ice shelf the sample laid, if any (decision 68): said, since it is ground no biome round it explains.
			IceShelfReach shelf = new SceneAltitude(request).IceShelf;
			if (shelf != null)
			{
				notes?.Add(shelf.Describe(new Rect(-plan.WidthMetres * 0.5f, -plan.DepthMetres * 0.5f, plan.WidthMetres, plan.DepthMetres)));
			}
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
					Observer?.Invoke("benched (plateaus), before the first water", ground, null);
					laid = SceneWater.Build(request, system, ground, null, (east, north) => process.ProcessAt(east, north).VegetationCohesion);
					float[] levels = laid.Any ? laid.Lay(ground) : null;
					Observer?.Invoke("the first water laid and carved", ground, laid);
					return levels;
				};
			}
			erosion = SceneErosion.Apply(field, process, settings, BaseLevel(request, system), SceneGenerator.SceneSeed(request), lay);
			Observer?.Invoke("eroded", field, laid);
			notes?.Add(erosion.ToString());
			UnityEngine.Debug.Log($"[Scene generator] '{request.SceneName}' {erosion}");
			if (lay != null && laid == null)
			{
				// Erosion did not run, so nothing laid the water: it still runs where the planet sends it.
				lay(field);
			}
			/* The biomes' sculpted landforms (2026-10-18, I2g): placed on the patches the erosion's biome field painted and carved into
			 * the worn ground, kept off the first water's channels and lakes, before the final water is worked out from the ground (so
			 * a crater's bowl or a playa can hold a lake, and rivers find their way round a mound). Nothing when no biome names one. */
			landforms = SceneLandforms.Carve(request, system, field, process.Biomes, laid, settings.Aeolian, BaseLevel(request, system),
				SceneGenerator.SceneSeed(request), notes);
			if (landforms != null && landforms.Sites.Count > 0)
			{
				Observer?.Invoke("sculpted landforms", field, laid);
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
					Observer?.Invoke("the final water worked out, not yet carved", field, final);
					if (final.Any)
					{
						final.Finish(field);
						laid = final;
						Observer?.Invoke("the final water carved", field, final);
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

		/// <summary>
		/// World Y of the scene's LAVA surface (2026-10-25, phase I2r, decision 65 (b): a magma ocean's or an Io's lava lakes'), NaN
		/// where the low ground holds water or nothing: what a lava band and a lava-datum landform measure from.
		/// </summary>
		public static float LavaLevel(SceneGenerationRequest request, SolarSystemProfile system)
		{
			if (request?.Body == null)
			{
				return float.NaN;
			}
			SurfaceLiquid liquid = SurfaceLiquids.For(system, request.Body, out float levelMetres);
			return liquid == SurfaceLiquid.Lava ? levelMetres * request.VerticalScale : float.NaN;
		}
	}
}
#endif
