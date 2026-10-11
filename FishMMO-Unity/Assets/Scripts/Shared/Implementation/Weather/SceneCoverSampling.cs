using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Where a scene's own snow, wetness, ash and sand figure is read: its area, and five points of it. One copy for the
	/// server and the client.
	/// </summary>
	public static class SceneCoverSampling
	{
		/// <summary>
		/// Where cover is sampled, as fractions of the scene's area from its centre: the centre and
		/// the middle of each quarter. Static, so a pass does not allocate the list.
		/// </summary>
		private static readonly Vector2[] Offsets =
		{
			Vector2.zero,
			new Vector2(-0.25f, -0.25f),
			new Vector2(0.25f, -0.25f),
			new Vector2(-0.25f, 0.25f),
			new Vector2(0.25f, 0.25f),
		};

		/// <summary>The scene's world-space X/Z rectangle, from its biome map or its terrain.</summary>
		public static bool TryGetArea(WorldSceneSettings settings, Scene scene, out Rect area)
		{
			SceneBiomeMap map = settings != null ? settings.BiomeMap : null;
			if (map != null && map.WorldSize.x > 0f && map.WorldSize.y > 0f)
			{
				area = new Rect(map.WorldOrigin, map.WorldSize);
				return true;
			}
			/* One measurement of the scene's ground, shared with the biome sampler. This used to
			 * union the tiles here as well, which was the same arithmetic written twice — and the
			 * two could drift, leaving the director working over a different landmass from the one
			 * the climate was being read against. */
			SceneTerrainExtent extent = SceneTerrainExtent.Of(scene);
			area = extent.Area;
			return extent.Found;
		}

		/// <summary>The scene's cover points in world space (y 0), for an area.</summary>
		public static void PointsOf(Rect area, List<Vector3> into)
		{
			into.Clear();
			if (!(area.width > 0f && area.height > 0f))
			{
				area = new Rect(-50f, -50f, 100f, 100f);
			}
			Vector2 c = area.center;
			for (int i = 0; i < Offsets.Length; i++)
			{
				Vector2 p = c + Vector2.Scale(Offsets[i], area.size);
				into.Add(new Vector3(p.x, 0f, p.y));
			}
		}
	}

	/// <summary>
	/// A scene's snow, wetness, ash and sand at a moment: the ground (<see cref="GroundCover"/>) averaged over the
	/// scene's five cover points (<see cref="SceneCoverSampling.PointsOf"/>). The server and every client work out the
	/// same figure from the world time, so it is never sent: gameplay reads the server's, the eye the client's, and
	/// they are one number.
	/// </summary>
	public sealed class SceneCover
	{
		private readonly CoverPoints points = new CoverPoints();
		private readonly List<Vector3> scratch = new List<Vector3>();

		/// <summary>The scene's cover at <paramref name="worldSeconds"/>, written to <see cref="WeatherTimeline.Cover"/> and returned.</summary>
		public WeatherCover Update(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, double worldSeconds)
		{
			if (timeline == null)
			{
				return default;
			}
			SceneCoverSampling.PointsOf(timeline.Area, scratch);
			points.SetPoints(scratch);
			points.Update(timeline, settings, scene, worldSeconds);
			timeline.Cover = points.Mean(worldSeconds);
			return timeline.Cover;
		}
	}
}
