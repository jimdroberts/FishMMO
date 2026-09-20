using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Where the snow lies, where the ground is wet, and where ash or sand has settled — a coarse
	/// top-down map around the camera, so a storm that passed over the north field leaves the north
	/// field white and the south field bare.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The server keeps one cover value for the whole scene, because that is what gameplay needs
	/// and what a joining player must be told. This map is the picture of it: every texel carries
	/// its own cover and integrates the weather standing over <i>it</i>, using the same
	/// <see cref="WeatherCover.Integrate"/> the server runs. Nothing here is sent or received —
	/// the client already has the timeline, so it can work this out for itself, and a grid on the
	/// wire would cost a kilobyte a resync for something only the eye uses.
	/// </para>
	/// <para>
	/// The server's value stays the anchor. A full timeline seeds every texel with it, so a player
	/// who joins mid-blizzard sees the right depth at once, and each later snapshot nudges the whole
	/// map toward the server's figure without flattening the variation the cells have drawn into it.
	/// </para>
	/// </remarks>
	public sealed class WeatherCoverMap
	{
		public const int DefaultResolution = 96;
		public const float DefaultSizeMeters = 4000f;

		public static readonly int TextureId = Shader.PropertyToID("_FishCoverTex");
		public static readonly int RectId = Shader.PropertyToID("_FishCoverRect");
		public static readonly int ParamsId = Shader.PropertyToID("_FishCoverParams");

		/// <summary>How much of the gap to the server's figure one snapshot closes.</summary>
		private const float AnchorBlend = 0.25f;

		/// <summary>Rows integrated per update: the whole map is swept over several frames.</summary>
		private const int RowsPerUpdate = 12;

		private Texture2D texture;
		private Color32[] pixels;
		private WeatherCover[] cover;
		private WeatherCover[] scratch;
		private int resolution;
		private float size;
		private Vector2 corner;
		private int nextRow;
		private float sinceRow;
		private bool valid;

		public Texture2D Texture => texture;
		public bool IsValid => valid;
		public Rect Area => new Rect(corner, new Vector2(size, size));
		public float TexelMeters => resolution > 0 ? size / resolution : 0f;

		/// <summary>The cover at a world position, or the anchor when the map has nothing there.</summary>
		public WeatherCover Sample(Vector3 position, in WeatherCover fallback)
		{
			if (!valid)
			{
				return fallback;
			}
			float texel = TexelMeters;
			int x = Mathf.FloorToInt((position.x - corner.x) / texel);
			int y = Mathf.FloorToInt((position.z - corner.y) / texel);
			if (x < 0 || y < 0 || x >= resolution || y >= resolution)
			{
				return fallback;
			}
			return cover[y * resolution + x];
		}

		/// <summary>
		/// Integrates part of the map and publishes it. Called every frame; only a slice of the rows
		/// is advanced each time, each by the time since that row was last touched, so the whole map
		/// keeps correct time at a fraction of the cost.
		/// </summary>
		public void Update(WeatherTimeline timeline, WorldSceneSettings settings, Vector3 centre, uint tick,
			float temperature, float deltaTime, in WeatherCover anchor, bool reseed,
			int res = DefaultResolution, float sizeMeters = DefaultSizeMeters, float sunlight = 0.5f)
		{
			Ensure(res, sizeMeters);
			Recentre(centre, anchor);
			if (reseed)
			{
				Seed(anchor);
			}

			sinceRow += Mathf.Max(0f, deltaTime);
			int rows = Mathf.Min(RowsPerUpdate, resolution);
			// Each row is advanced by how long it has been since its own turn came round.
			float sweepSeconds = sinceRow * resolution / Mathf.Max(1, rows);

			WeatherFrame sceneLayers = BaseFrame(timeline, settings, centre, tick);
			float texel = TexelMeters;
			for (int i = 0; i < rows; i++)
			{
				int y = nextRow;
				nextRow = (nextRow + 1) % resolution;
				for (int x = 0; x < resolution; x++)
				{
					var position = new Vector3(corner.x + (x + 0.5f) * texel, 0f, corner.y + (y + 0.5f) * texel);
					WeatherFrame frame = FrameAt(timeline, sceneLayers, position, tick);
					int index = y * resolution + x;
					frame.RetypeForTemperature(temperature);
					frame.DeriveSurfaceRates();
					cover[index].Integrate(frame, temperature, sweepSeconds, sunlight);
					pixels[index] = Encode(cover[index]);
				}
			}
			sinceRow = 0f;

			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			valid = true;
			Publish();
		}

		/// <summary>
		/// Pulls the whole map toward the server's figure, keeping the shape the cells have drawn.
		/// Called when a snapshot arrives, which is rarely: cover moves slowly.
		/// </summary>
		public void Anchor(in WeatherCover server)
		{
			if (!valid || cover == null)
			{
				return;
			}
			WeatherCover mean = default;
			for (int i = 0; i < cover.Length; i++)
			{
				mean.Snow += cover[i].Snow;
				mean.Wet += cover[i].Wet;
				mean.Ash += cover[i].Ash;
				mean.Sand += cover[i].Sand;
			}
			float count = cover.Length;
			Vector4 shift = new Vector4(
				(server.Snow - mean.Snow / count) * AnchorBlend,
				(server.Wet - mean.Wet / count) * AnchorBlend,
				(server.Ash - mean.Ash / count) * AnchorBlend,
				(server.Sand - mean.Sand / count) * AnchorBlend);
			for (int i = 0; i < cover.Length; i++)
			{
				cover[i].Snow = Mathf.Clamp01(cover[i].Snow + shift.x);
				cover[i].Wet = Mathf.Clamp01(cover[i].Wet + shift.y);
				cover[i].Ash = Mathf.Clamp01(cover[i].Ash + shift.z);
				cover[i].Sand = Mathf.Clamp01(cover[i].Sand + shift.w);
				pixels[i] = Encode(cover[i]);
			}
		}

		/// <summary>
		/// Puts the same cover everywhere at once. A test bed and the sim panel use this: the ground
		/// is meant to look like that <i>now</i>, not after the weather has worked on it.
		/// </summary>
		public void Hold(in WeatherCover everywhere)
		{
			if (cover == null)
			{
				Ensure(DefaultResolution, DefaultSizeMeters);
			}
			Seed(everywhere);
		}

		/// <summary>
		/// Runs the whole map forward, for a test bed or a preview that will not wait a quarter of
		/// an hour to see snow lie. Nothing in the game calls this.
		/// </summary>
		public void Advance(WeatherTimeline timeline, WorldSceneSettings settings, uint tick, float temperature, float seconds)
		{
			if (!valid || seconds <= 0f)
			{
				return;
			}
			WeatherFrame sceneLayers = BaseFrame(timeline, settings, new Vector3(corner.x + size * 0.5f, 0f, corner.y + size * 0.5f), tick);
			float texel = TexelMeters;
			for (int y = 0; y < resolution; y++)
			{
				for (int x = 0; x < resolution; x++)
				{
					var position = new Vector3(corner.x + (x + 0.5f) * texel, 0f, corner.y + (y + 0.5f) * texel);
					WeatherFrame frame = FrameAt(timeline, sceneLayers, position, tick);
					int index = y * resolution + x;
					cover[index].Integrate(frame, temperature, seconds);
					pixels[index] = Encode(cover[index]);
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			Publish();
		}

		public void Clear()
		{
			valid = false;
			Shader.SetGlobalVector(ParamsId, Vector4.zero);
		}

		public void Dispose()
		{
			Clear();
			if (texture != null)
			{
				if (Application.isPlaying)
				{
					Object.Destroy(texture);
				}
				else
				{
					Object.DestroyImmediate(texture);
				}
				texture = null;
			}
			cover = null;
			scratch = null;
			pixels = null;
			resolution = 0;
		}

		// ── Inside ─────────────────────────────────────────────────────

		/// <summary>
		/// The weather standing over one point: the scene's own layers, plus whatever storm cells
		/// reach it. The biome's background is left out on purpose — it is the same everywhere the
		/// eye can see from here, and the server's anchor already carries it.
		/// </summary>
		/// <summary>
		/// Everything but the storm cells, at the middle of the map: the scene's own layers AND the
		/// drifting field's weather. This used to be the scene layers alone, so the ground under a
		/// field's rain never got wet on the map and nothing the field did — rain, snow, a clearing
		/// sky — ever reached it; only presets and cells did. The field turns over across tens of
		/// kilometres and the map is a kilometre or two, so one reading at its middle serves.
		/// </summary>
		private static WeatherFrame BaseFrame(WeatherTimeline timeline, WorldSceneSettings settings, Vector3 centre, uint tick)
		{
			if (timeline == null)
			{
				return WeatherFrame.Clear;
			}
			return WeatherField.Sample(timeline, settings, default, centre, tick).Background;
		}

		private static WeatherFrame FrameAt(WeatherTimeline timeline, in WeatherFrame sceneLayers, Vector3 position, uint tick)
		{
			WeatherFrame frame = sceneLayers;
			if (timeline == null || timeline.SceneMode != WeatherSceneMode.Own)
			{
				frame.DeriveSurfaceRates();
				return frame;
			}
			var accumulator = new WeatherAccumulator();
			accumulator.Add(sceneLayers, 1f);
			bool any = false;
			for (int i = 0; i < timeline.Cells.Count; i++)
			{
				StormCell cell = timeline.Cells[i];
				float weight = cell.InfluenceAt(position, tick, timeline.TickDelta);
				if (weight <= 0f)
				{
					continue;
				}
				WeatherPreset preset = WeatherPreset.Get<WeatherPreset>(cell.PresetID);
				if (preset == null)
				{
					continue;
				}
				accumulator.Add(preset.Evaluate(), weight);
				any = true;
			}
			frame = any ? accumulator.Resolve() : sceneLayers;
			frame.DeriveSurfaceRates();
			return frame;
		}

		private static Color32 Encode(in WeatherCover c)
		{
			return new Color32(
				(byte)(Mathf.Clamp01(c.Snow) * 255f),
				(byte)(Mathf.Clamp01(c.Wet) * 255f),
				(byte)(Mathf.Clamp01(c.Ash) * 255f),
				(byte)(Mathf.Clamp01(c.Sand) * 255f));
		}

		private void Ensure(int res, float sizeMeters)
		{
			int wanted = Mathf.Max(8, res);
			if (texture != null && resolution == wanted && Mathf.Approximately(size, sizeMeters))
			{
				return;
			}
			Dispose();
			resolution = wanted;
			size = sizeMeters;
			texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
			{
				name = "Weather Cover",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			pixels = new Color32[resolution * resolution];
			cover = new WeatherCover[resolution * resolution];
			scratch = new WeatherCover[resolution * resolution];
			nextRow = 0;
			sinceRow = 0f;
		}

		/// <summary>
		/// Moves the window with the camera, in whole texels. What was already covered keeps its
		/// cover; ground that has just come into the window starts at the server's figure, which is
		/// the best guess anyone has for land nobody has been watching.
		/// </summary>
		private void Recentre(Vector3 centre, in WeatherCover anchor)
		{
			float texel = TexelMeters;
			var wanted = new Vector2(
				Mathf.Round((centre.x - size * 0.5f) / texel) * texel,
				Mathf.Round((centre.z - size * 0.5f) / texel) * texel);
			if (!valid)
			{
				corner = wanted;
				Seed(anchor);
				return;
			}
			int shiftX = Mathf.RoundToInt((wanted.x - corner.x) / texel);
			int shiftY = Mathf.RoundToInt((wanted.y - corner.y) / texel);
			if (shiftX == 0 && shiftY == 0)
			{
				return;
			}
			for (int y = 0; y < resolution; y++)
			{
				for (int x = 0; x < resolution; x++)
				{
					int sourceX = x + shiftX;
					int sourceY = y + shiftY;
					bool inside = sourceX >= 0 && sourceY >= 0 && sourceX < resolution && sourceY < resolution;
					scratch[y * resolution + x] = inside ? cover[sourceY * resolution + sourceX] : anchor;
				}
			}
			WeatherCover[] swap = cover;
			cover = scratch;
			scratch = swap;
			corner = wanted;
			for (int i = 0; i < cover.Length; i++)
			{
				pixels[i] = Encode(cover[i]);
			}
		}

		private void Seed(in WeatherCover anchor)
		{
			for (int i = 0; i < cover.Length; i++)
			{
				cover[i] = anchor;
				pixels[i] = Encode(anchor);
			}
			if (texture != null)
			{
				texture.SetPixels32(pixels);
				texture.Apply(false, false);
			}
			valid = true;
			Publish();
		}

		private void Publish()
		{
			Shader.SetGlobalTexture(TextureId, texture);
			Shader.SetGlobalVector(RectId, new Vector4(corner.x, corner.y, size, size));
			Shader.SetGlobalVector(ParamsId, new Vector4(valid ? 1f : 0f, TexelMeters, 0f, 0f));
		}
	}
}
