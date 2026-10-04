#if UNITY_EDITOR
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
			erosion = null;
			SceneHeightField field = SceneHeightField.Sample(request, plan);
			if (!request.Erosion)
			{
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
			erosion = SceneErosion.Apply(field, process, settings, BaseLevel(request, system), SceneGenerator.SceneSeed(request));
			notes?.Add(erosion.ToString());
			UnityEngine.Debug.Log($"[Scene generator] '{request.SceneName}' {erosion}");
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
