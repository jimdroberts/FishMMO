using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
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

		/// <summary>
		/// Seconds the whole map takes to sweep, whatever the frame rate: each frame integrates the rows that time has
		/// earned. It was twelve rows every frame, the whole map eight times a second at 60 fps (more at 300), each texel
		/// a full weather sample and the texture uploaded every frame: 0.8 ms of the main thread for cover that moves
		/// over minutes (ScenePerfProbe, 2026-10-07).
		/// </summary>
		public const float SweepSeconds = 6f;

		/// <summary>
		/// The most steps one row's turn takes. A row is brought up to the world's time each turn in steps of up to
		/// <see cref="WeatherCover.StepSeconds"/>; at the world's own pace that is one, and in a world raced a thousand
		/// times over, a few long ones rather than a hundred short ones, each a full weather sample per texel.
		/// </summary>
		public const int MaxRowSteps = 4;

		private Texture2D texture;
		private Color32[] pixels;
		private WeatherCover[] cover;
		private WeatherCover[] scratch;
		private int resolution;
		private float size;
		private Vector2 corner;
		private int nextRow;
		private float rowCredit;
		private bool valid;
		/// <summary>The world time each row was last brought up to, seconds.</summary>
		private double[] rowSeconds;
		/// <summary>Set when every texel was just given the same cover: the rows' clocks then start from now.</summary>
		private bool restartRows;

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
		/// is advanced each time, each over the WORLD time since that row was last brought up to date, so
		/// the whole map keeps the world's time — held, raced or jumped — at a fraction of the cost.
		/// </summary>
		/// <remarks>
		/// How many rows a frame sweeps is paid in real time (<paramref name="deltaTime"/>): that is the cost. What
		/// each row is advanced by is world time: that is the weather. It was the wall clock's for both, so a held
		/// world went on drying and settling under a sky that stood still, and a raced one dried at the speed of
		/// the wall while the storms raced overhead.
		/// </remarks>
		public void Update(WeatherTimeline timeline, WorldSceneSettings settings, Vector3 centre, uint tick,
			float deltaTime, in WeatherCover anchor, bool reseed,
			int res = DefaultResolution, float sizeMeters = DefaultSizeMeters)
		{
			Ensure(res, sizeMeters);
			Recentre(centre, anchor);
			if (reseed)
			{
				Seed(anchor);
			}
			double now = timeline != null ? timeline.WorldSecondsAt(tick) : 0.0;
			if (restartRows)
			{
				// The anchor already stands for the ground at this moment; the sweep then works the weather in from here.
				for (int y = 0; y < resolution; y++)
				{
					rowSeconds[y] = now;
				}
				restartRows = false;
			}

			rowCredit += resolution * Mathf.Max(0f, deltaTime) / SweepSeconds;
			int rows = Mathf.Min(resolution, Mathf.FloorToInt(rowCredit));
			if (rows <= 0)
			{
				Publish();
				return;
			}
			rowCredit -= rows;

			for (int i = 0; i < rows; i++)
			{
				AdvanceRow(timeline, settings, nextRow, now, MaxRowSteps);
				nextRow = (nextRow + 1) % resolution;
			}

			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			valid = true;
			Publish();
		}

		/// <summary>
		/// Brings every row up to the world's time at once, rather than over the next sweep: for a test bed that has
		/// just moved the world clock on and wants to look at the ground now. Nothing in the game calls this.
		/// </summary>
		public void CatchUp(WeatherTimeline timeline, WorldSceneSettings settings, uint tick)
		{
			if (!valid || cover == null)
			{
				return;
			}
			double now = timeline != null ? timeline.WorldSecondsAt(tick) : 0.0;
			for (int y = 0; y < resolution; y++)
			{
				AdvanceRow(timeline, settings, y, now, SceneCoverSampling.MaxSteps);
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, false);
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
			rowSeconds = null;
			pixels = null;
			resolution = 0;
		}

		// ── Inside ─────────────────────────────────────────────────────

		/// <summary>
		/// One row from the world time it was last brought up to, to <paramref name="now"/>, in at most
		/// <paramref name="maxSteps"/> steps, each under the weather and daylight of its own moment. A clock set
		/// back leaves the row as it is and counts on from the new time.
		/// </summary>
		private void AdvanceRow(WeatherTimeline timeline, WorldSceneSettings settings, int y, double now, int maxSteps)
		{
			double from = rowSeconds[y];
			int steps = WeatherCover.StepsFor(now - from, maxSteps);
			rowSeconds[y] = now;
			if (steps <= 0)
			{
				return;
			}
			from = System.Math.Max(from, now - WeatherCover.MaxAdvanceSeconds);
			double step = (now - from) / steps;
			float texel = TexelMeters;
			Vector3 middle = new Vector3(corner.x + size * 0.5f, 0f, corner.y + size * 0.5f);
			for (int k = 1; k <= steps; k++)
			{
				double at = from + step * k;
				WeatherFrame sceneLayers = BaseFrame(timeline, settings, middle, at, storms, out float temperature);
				float sunlight = SceneTime.IsDaylight(settings, at / 3600.0) ? 1f : 0f;
				for (int x = 0; x < resolution; x++)
				{
					var position = new Vector3(corner.x + (x + 0.5f) * texel, 0f, corner.y + (y + 0.5f) * texel);
					WeatherFrame frame = FrameAt(timeline, sceneLayers, position, at, storms);
					frame.RetypeForTemperature(temperature);
					frame.DeriveSurfaceRates();
					cover[y * resolution + x].Integrate(frame, temperature, (float)step, sunlight);
				}
			}
			for (int x = 0; x < resolution; x++)
			{
				int index = y * resolution + x;
				pixels[index] = Encode(cover[index]);
			}
		}

		/// <summary>
		/// The weather over the middle of the map without its storms: the air's own. The field turns
		/// over across tens of kilometres and the map is a kilometre or two, so one reading at its
		/// middle serves; the storms are then added point by point. Readies the storms' weather in
		/// that same air.
		/// </summary>
		private static WeatherFrame BaseFrame(WeatherTimeline timeline, WorldSceneSettings settings, Vector3 centre, double worldSeconds,
			StormFrames storms, out float temperature)
		{
			if (timeline == null)
			{
				storms.Reset(default);
				temperature = 0f;
				return WeatherFrame.Clear;
			}
			WeatherSample around = WeatherField.SampleAtSeconds(timeline, settings, default, centre, worldSeconds);
			storms.Reset(around);
			temperature = around.Temperature;
			return around.Background;
		}

		/// <summary>
		/// The weather standing over one point: the air's own, plus what each storm reaching it makes
		/// in that air.
		/// </summary>
		private static WeatherFrame FrameAt(WeatherTimeline timeline, in WeatherFrame open, Vector3 position, double worldSeconds, StormFrames storms)
		{
			WeatherFrame frame = open;
			if (timeline == null || timeline.SceneMode != WeatherSceneMode.Own)
			{
				frame.DeriveSurfaceRates();
				return frame;
			}
			var accumulator = new WeatherAccumulator();
			accumulator.Add(open, 1f);
			bool any = false;
			for (int i = 0; i < timeline.Cells.Count; i++)
			{
				StormCell cell = timeline.Cells[i];
				float weight = cell.InfluenceAtSeconds(position, worldSeconds);
				if (weight <= 0f)
				{
					continue;
				}
				accumulator.Add(storms.Of(cell.Kind), weight);
				any = true;
			}
			frame = any ? accumulator.Resolve() : open;
			frame.DeriveSurfaceRates();
			return frame;
		}

		private readonly StormFrames storms = new StormFrames();

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
			rowSeconds = new double[resolution];
			restartRows = true;
			nextRow = 0;
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
			restartRows = true;
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
