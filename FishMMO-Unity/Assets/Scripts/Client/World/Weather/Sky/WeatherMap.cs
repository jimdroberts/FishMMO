using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// A coarse top-down picture of the weather around the camera, so the sky can show storms
	/// that are kilometres away: r cloud cover, g precipitation, b snow share, a storm strength.
	/// </summary>
	/// <remarks>
	/// Rasterised on the CPU from the timeline's scene layers and storm cells a few times a
	/// second. Weather never appears on any map UI; this is only read by the sky shader.
	/// </remarks>
	public sealed class WeatherMap
	{
		public const int DefaultResolution = 128;
		public const float DefaultSizeMeters = 12000f;

		/// <summary>
		/// How far a cell's cloud reaches compared with its rain. A point this much nearer the
		/// centre stands in for the real one, so the same falloff covers a wider circle.
		/// </summary>
		public const float CloudShieldScale = 0.55f;
		public static readonly int TextureId = Shader.PropertyToID("_FishWeatherMap");
		public static readonly int RectId = Shader.PropertyToID("_FishWeatherMapRect");
		public static readonly int ParamsId = Shader.PropertyToID("_FishWeatherMapParams");

		private readonly System.Collections.Generic.List<StormCollision> collisions = new System.Collections.Generic.List<StormCollision>();
		private Texture2D texture;
		private Color32[] pixels;
		private int resolution;
		private float size;
		private Vector2 corner;

		public Texture2D Texture => texture;
		public Rect Area => new Rect(corner, new Vector2(size, size));

		/// <summary>The weather at one map point, 0..1 per channel.</summary>
		public static Color Sample(WeatherTimeline timeline, in WeatherFrame sceneLayers, Vector3 position, uint tick, System.Collections.Generic.List<StormCollision> collisions = null)
		{
			float cover = sceneLayers[WeatherChannel.CloudCover];
			float precipitation = sceneLayers[WeatherChannel.Precipitation];
			float snow = sceneLayers[WeatherChannel.SnowWeight] * precipitation;
			float storm = sceneLayers.StormSeverity;
			if (timeline != null && timeline.SceneMode == WeatherSceneMode.Own)
			{
				for (int i = 0; i < timeline.Cells.Count; i++)
				{
					StormCell cell = timeline.Cells[i];
					float weight = cell.InfluenceAt(position, tick, timeline.TickDelta);
					// A storm's cloud reaches well beyond its rain: the shield is drawn by asking
					// the cell about a point pulled toward its centre, which is the same falloff
					// over a wider circle. Without it a cell is a rain shaft under a clear sky.
					Vector2 centre = cell.CentreAt(tick, timeline.TickDelta);
					var shieldPoint = new Vector3(
						centre.x + (position.x - centre.x) * CloudShieldScale,
						position.y,
						centre.y + (position.z - centre.y) * CloudShieldScale);
					float shield = cell.InfluenceAt(shieldPoint, tick, timeline.TickDelta);
					if (weight <= 0f && shield <= 0f)
					{
						continue;
					}
					WeatherPreset preset = WeatherPreset.Get<WeatherPreset>(cell.PresetID);
					if (preset == null)
					{
						continue;
					}
					WeatherFrame frame = preset.Evaluate();
					cover = Mathf.Max(cover, frame[WeatherChannel.CloudCover] * shield);
					float p = frame[WeatherChannel.Precipitation] * weight;
					precipitation = 1f - (1f - precipitation) * (1f - p);
					snow = Mathf.Max(snow, p * frame[WeatherChannel.SnowWeight]);
					storm = Mathf.Max(storm, frame.StormSeverity * weight);
				}
			}
			// Where two cells collide the map carries a storm of its own, and the sky reads a storm
			// here as "this column goes all the way up": the tower stands on the boundary.
			if (collisions != null)
			{
				for (int i = 0; i < collisions.Count; i++)
				{
					float clash = collisions[i].InfluenceAt(position);
					if (clash > 0f)
					{
						cover = Mathf.Max(cover, clash);
						storm = Mathf.Max(storm, clash);
						precipitation = 1f - (1f - precipitation) * (1f - clash * 0.35f);
					}
				}
			}
			float snowShare = precipitation > 0.001f ? Mathf.Clamp01(snow / precipitation) : 0f;
			return new Color(Mathf.Clamp01(cover), Mathf.Clamp01(precipitation), snowShare, Mathf.Clamp01(storm));
		}

		/// <summary>Redraws the map centred near a position and publishes it.</summary>
		public void Build(WeatherTimeline timeline, Vector3 centre, uint tick, int res = DefaultResolution, float sizeMeters = DefaultSizeMeters)
		{
			if (texture == null || resolution != res)
			{
				Dispose();
				resolution = Mathf.Max(8, res);
				texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
				{
					name = "Weather Map",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.DontSave,
				};
				pixels = new Color32[resolution * resolution];
			}
			size = sizeMeters;
			float texel = size / resolution;
			// Snap so the map does not crawl as the camera moves.
			corner = new Vector2(Mathf.Round((centre.x - size * 0.5f) / texel) * texel, Mathf.Round((centre.z - size * 0.5f) / texel) * texel);

			var accumulator = new WeatherAccumulator();
			timeline?.AccumulateSceneLayers(tick, ref accumulator);
			WeatherFrame sceneLayers = accumulator.HasAny ? accumulator.Resolve() : WeatherFrame.Clear;
			// Once for the whole map, not once a texel: sixteen thousand texels times sixty-six pairs
			// is a million tests to find the one or two collisions there ever are.
			collisions.Clear();
			timeline?.CollisionsAt(tick, collisions);
			for (int y = 0; y < resolution; y++)
			{
				for (int x = 0; x < resolution; x++)
				{
					var position = new Vector3(corner.x + (x + 0.5f) * texel, 0f, corner.y + (y + 0.5f) * texel);
					pixels[y * resolution + x] = Sample(timeline, sceneLayers, position, tick, collisions);
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			Publish(true);
		}

		public void Publish(bool valid)
		{
			if (texture != null)
			{
				Shader.SetGlobalTexture(TextureId, texture);
			}
			Shader.SetGlobalVector(RectId, new Vector4(corner.x, corner.y, size, size));
			Shader.SetGlobalVector(ParamsId, new Vector4(valid && texture != null ? 1f : 0f, 0f, 0f, 0f));
		}

		public void Dispose()
		{
			if (texture != null)
			{
				if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
				texture = null;
			}
		}
	}
}
