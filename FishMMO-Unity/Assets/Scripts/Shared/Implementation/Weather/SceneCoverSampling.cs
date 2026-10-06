using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The weather a scene's snow, wetness, ash and sand are integrated from: the average over five
	/// points of its area. One copy for the server and the client.
	/// </summary>
	/// <remarks>
	/// The server integrated from these five points and the client, between the server's snapshots,
	/// from the weather where its camera stood — two different forcings of the same figure, so each
	/// client's ground drifted from the server's and from every other client's until the next
	/// snapshot pulled it back (audit 2026-10-06). The weather at each point is a pure function of the
	/// tick, so both sides integrating from the same points track the same figure.
	/// </remarks>
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

		/// <summary>The scene's average weather and temperature at a tick, over its five cover points.</summary>
		public static void Sample(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, uint tick,
			out WeatherFrame frame, out float temperature)
		{
			if (!TryGetArea(settings, scene, out Rect area))
			{
				area = new Rect(-50f, -50f, 100f, 100f);
			}
			var average = new WeatherAccumulator();
			temperature = 0f;
			Vector2 c = area.center;
			for (int i = 0; i < Offsets.Length; i++)
			{
				Vector2 p = c + Vector2.Scale(Offsets[i], area.size);
				WeatherSample sample = WeatherField.Sample(timeline, settings, scene, new Vector3(p.x, 0f, p.y), tick);
				average.Add(sample.Frame, 1f / Offsets.Length);
				temperature += sample.Temperature / Offsets.Length;
			}
			frame = average.Resolve();
		}
	}
}
