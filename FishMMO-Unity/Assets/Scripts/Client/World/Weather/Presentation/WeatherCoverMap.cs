using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Where the snow lies, where the ground is wet, and where ash or sand has settled — a coarse top-down map around
	/// the camera, so a storm that passed over the north field leaves the north field white and the south field bare.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The ground of the moment, the same for everyone.</b> Each texel is the ground (<see cref="GroundCover"/>) at
	/// its own place in the world at the world's time: a pure function of the place and the moment, worked out here as
	/// the server works out its figure. Two players looking at one field see one ground, whenever each joined and
	/// wherever each has been; a set of the clock shows the ground of the new moment. It used to be seeded from the
	/// server's running figure, filled with it where ground came into view, nudged toward each snapshot and integrated
	/// row by row at moments of its own.
	/// </para>
	/// <para>
	/// <b>Shared until a storm reaches it.</b> A texel no storm has reached in the ground's memory has the open air's
	/// ground, which is one track for the whole map. A texel a storm reaches is worked out on its own from the step it
	/// was first reached — before that it was the shared track exactly — and goes back to sharing once it has come
	/// back to the shared ground exactly (dried, melted, cleared). The texels are fixed to the world, so moving the
	/// camera only works out the strip coming into view.
	/// </para>
	/// <para>
	/// <b>Cost.</b> One weather reading a world minute for the shared track, and each storm-reached texel a step a
	/// minute. After a join, a scene change or a set of the clock the shared ground is worked out at once and the
	/// storm-reached texels are worked in under a time budget, a large one on the frame the weather snaps
	/// (<see cref="WeatherClient.Snaps"/>), so the ground lands with the rest of the weather.
	/// </para>
	/// </remarks>
	public sealed class WeatherCoverMap
	{
		public const int DefaultResolution = 96;
		public const float DefaultSizeMeters = 4000f;

		public static readonly int TextureId = Shader.PropertyToID("_FishCoverTex");
		public static readonly int RectId = Shader.PropertyToID("_FishCoverRect");
		public static readonly int ParamsId = Shader.PropertyToID("_FishCoverParams");
		public static readonly int DepthTextureId = Shader.PropertyToID("_FishSnowDepthTex");
		public static readonly int DepthParamsId = Shader.PropertyToID("_FishSnowDepthParams");

		/// <summary>Milliseconds a frame spends working storm-reached texels in.</summary>
		public const float BudgetMilliseconds = 3f;
		/// <summary>Milliseconds the frame the weather snaps spends on them, so the ground lands with everything else.</summary>
		public const float SnapBudgetMilliseconds = 150f;
		/// <summary>The most steps the map steps through in a frame; past that it is worked out again from its memory.</summary>
		public const int MaxStepsPerFrame = 32;
		/// <summary>Real seconds between redraws of the texture while nothing else asks for one.</summary>
		public const float RedrawSeconds = 0.1f;
		/// <summary>Real seconds between full workings-out while the world races past what stepping can follow.</summary>
		public const float RebuildIntervalSeconds = 0.25f;

		private enum TexelState : byte
		{
			/// <summary>The open air's ground: no storm has reached it in the ground's memory.</summary>
			Shared,
			/// <summary>Worked out on its own.</summary>
			Own,
			/// <summary>Reached by a storm and not yet worked out: shown as the shared ground meanwhile.</summary>
			Pending,
		}

		private Texture2D texture;
		private Color32[] pixels;
		private Texture2D depthTexture;
		private byte[] depthPixels;
		private float depthMax;
		private int resolution;
		private float size;
		/// <summary>The world texel (x, z) the map's corner texel is.</summary>
		private Vector2Int origin;
		private bool valid;

		private TexelState[] state;
		private long[] firstTouch;
		private CoverTrack[] oldAt, oldNext, youngAt, youngNext;
		private readonly Queue<int> pending = new Queue<int>();

		private CoverTrack bgOldAt, bgOldNext, bgYoungAt, bgYoungNext;
		/// <summary>The shared track of each live generation, from its start step to the step after the map's.</summary>
		private readonly Dictionary<long, List<CoverTrack>> bgHistory = new Dictionary<long, List<CoverTrack>>();

		private long step = long.MinValue;
		private long generation;
		private int version = -1;
		private WeatherTimeline timeline;
		private WorldSceneSettings settings;
		private Scene scene;
		private DeepSnow deep;
		private uint seenSnaps;
		private double drawnSeconds = double.NaN;
		private float sinceDraw;
		private float sinceRebuild = float.MaxValue;
		private bool dirty;

		private bool held;
		private WeatherCover heldCover;
		private float heldDepth;

		public Texture2D Texture => texture;
		public bool IsValid => valid;
		public Rect Area => new Rect(origin.x * TexelMeters, origin.y * TexelMeters, size, size);
		public float TexelMeters => resolution > 0 ? size / resolution : 0f;
		/// <summary>Storm-reached texels still to be worked out.</summary>
		public int PendingTexels => pending.Count;

		/// <summary>How deep snow can lie past the blanket, and how many world hours of the heaviest fall build it: the profile's.</summary>
		private static DeepSnow DeepSnowSettings()
		{
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			return new DeepSnow
			{
				MaxMetres = profile != null ? Mathf.Max(0f, profile.DeepSnowMetres) : 1f,
				Hours = profile != null ? Mathf.Max(0.1f, profile.DeepSnowHours) : 4f,
			};
		}

		/// <summary>
		/// Brings the map to the ground of <paramref name="worldSeconds"/> around <paramref name="centre"/> and publishes
		/// it. Called every frame; <paramref name="realDeltaTime"/> paces the redraws, never the ground.
		/// </summary>
		public void Update(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, Vector3 centre, double worldSeconds,
			float realDeltaTime, int res = DefaultResolution, float sizeMeters = DefaultSizeMeters)
		{
			Ensure(res, sizeMeters);
			sinceDraw += Mathf.Max(0f, realDeltaTime);
			sinceRebuild += Mathf.Max(0f, realDeltaTime);
			bool snap = WeatherClient.Snaps != seenSnaps;
			seenSnaps = WeatherClient.Snaps;
			if (held)
			{
				Publish();
				return;
			}
			if (timeline == null)
			{
				valid = false;
				Publish();
				return;
			}

			this.settings = settings;
			this.scene = scene;
			DeepSnow wantedDeep = DeepSnowSettings();
			long g = GroundCover.StepOf(worldSeconds);
			long j = GroundCover.GenerationOf(worldSeconds);
			int v = GroundCover.VersionOf(timeline, settings);
			Vector2Int wanted = OriginFor(centre);
			bool rebuild = step == long.MinValue || v != version || !ReferenceEquals(timeline, this.timeline)
				|| wantedDeep.MaxMetres != deep.MaxMetres || wantedDeep.Hours != deep.Hours
				|| g < step || j < generation || g - step > MaxStepsPerFrame;
			if (rebuild)
			{
				// A world raced past what stepping can follow is worked out afresh a few times a second, not every frame.
				bool racing = step != long.MinValue && v == version && ReferenceEquals(timeline, this.timeline) && g > step;
				if (!racing || snap || sinceRebuild >= RebuildIntervalSeconds)
				{
					this.timeline = timeline;
					version = v;
					deep = wantedDeep;
					origin = wanted;
					Rebuild(g, j);
					sinceRebuild = 0f;
				}
			}
			else
			{
				if (wanted != origin)
				{
					Recentre(wanted);
				}
				while (step < g)
				{
					StepForward();
				}
			}

			WorkPending(snap ? SnapBudgetMilliseconds : BudgetMilliseconds);
			if (snap || dirty || sinceDraw >= RedrawSeconds || double.IsNaN(drawnSeconds))
			{
				Draw(worldSeconds);
			}
			Publish();
		}

		/// <summary>Works every storm-reached texel out now: for a test bed that wants to look at the ground at once.</summary>
		public void CatchUp()
		{
			if (step == long.MinValue)
			{
				return;
			}
			WorkPending(float.MaxValue);
			if (!double.IsNaN(drawnSeconds))
			{
				Draw(drawnSeconds);
			}
			Publish();
		}

		/// <summary>How deep the snow lies past the blanket at a world position, metres; 0 where the map has nothing.</summary>
		public float SampleDeepSnow(Vector3 position)
		{
			if (!valid)
			{
				return 0f;
			}
			if (held)
			{
				return Mathf.Min(heldDepth, depthMax * Mathf.Clamp01(heldCover.Snow));
			}
			return TryIndex(position, out int index) ? TrackAt(index, drawnSeconds).Depth : 0f;
		}

		/// <summary>The cover at a world position, or <paramref name="fallback"/> where the map has nothing.</summary>
		public WeatherCover Sample(Vector3 position, in WeatherCover fallback)
		{
			if (!valid)
			{
				return fallback;
			}
			if (held)
			{
				return heldCover;
			}
			return TryIndex(position, out int index) ? TrackAt(index, drawnSeconds).Cover : fallback;
		}

		/// <summary>
		/// Shows the same cover everywhere, held there until <see cref="Release"/>: a test bed that wants to look at a
		/// wet street or a snowed-in courtyard now, not at what the weather made. Nothing in the game calls this.
		/// </summary>
		public void Hold(in WeatherCover everywhere)
		{
			if (texture == null)
			{
				Ensure(DefaultResolution, DefaultSizeMeters);
			}
			held = true;
			heldCover = everywhere;
			depthMax = DeepSnowSettings().MaxMetres;
			heldDepth = Mathf.Min(heldDepth, depthMax * Mathf.Clamp01(everywhere.Snow));
			DrawHeld();
		}

		/// <summary>Snow this deep past the blanket everywhere, held (with the cover held) until <see cref="Release"/>. For a test bed.</summary>
		public void HoldDeepSnow(float metres)
		{
			if (!held)
			{
				// From the ground as it is shown now.
				Hold(valid && step != long.MinValue && !double.IsNaN(drawnSeconds)
					? Blend(bgOldAt, bgOldNext, bgYoungAt, bgYoungNext, StepFraction(drawnSeconds), GroundCover.YoungWeight(drawnSeconds)).Cover
					: default);
			}
			depthMax = DeepSnowSettings().MaxMetres;
			heldDepth = Mathf.Clamp(metres, 0f, depthMax * Mathf.Clamp01(heldCover.Snow));
			DrawHeld();
		}

		/// <summary>Lets the ground be the weather's again after a <see cref="Hold"/>.</summary>
		public void Release()
		{
			if (!held)
			{
				return;
			}
			held = false;
			heldDepth = 0f;
			dirty = true;
		}

		public void Clear()
		{
			valid = false;
			Shader.SetGlobalVector(ParamsId, Vector4.zero);
			Shader.SetGlobalVector(DepthParamsId, Vector4.zero);
		}

		public void Dispose()
		{
			Clear();
			Destroy(ref texture);
			Destroy(ref depthTexture);
			pixels = null;
			depthPixels = null;
			state = null;
			firstTouch = null;
			oldAt = oldNext = youngAt = youngNext = null;
			pending.Clear();
			bgHistory.Clear();
			resolution = 0;
			step = long.MinValue;
		}

		// ── Inside ─────────────────────────────────────────────────────

		private static void Destroy(ref Texture2D t)
		{
			if (t == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				Object.Destroy(t);
			}
			else
			{
				Object.DestroyImmediate(t);
			}
			t = null;
		}

		private Vector2Int OriginFor(Vector3 centre)
		{
			float texel = TexelMeters;
			return new Vector2Int(
				Mathf.RoundToInt((centre.x - size * 0.5f) / texel),
				Mathf.RoundToInt((centre.z - size * 0.5f) / texel));
		}

		/// <summary>The world position of a texel's middle: fixed to the world, whoever's map it is in.</summary>
		private Vector3 TexelCentre(int index)
		{
			float texel = TexelMeters;
			int x = index % resolution;
			int y = index / resolution;
			return new Vector3((origin.x + x + 0.5f) * texel, 0f, (origin.y + y + 0.5f) * texel);
		}

		private bool TryIndex(Vector3 position, out int index)
		{
			float texel = TexelMeters;
			int x = Mathf.FloorToInt(position.x / texel) - origin.x;
			int y = Mathf.FloorToInt(position.z / texel) - origin.y;
			index = y * resolution + x;
			return x >= 0 && y >= 0 && x < resolution && y < resolution;
		}

		private GroundCover.Forcing Forcing(long k) => GroundCover.ForcingAt(timeline, settings, scene, k);

		/// <summary>The ground of a texel at a moment between the map's step and the next.</summary>
		private CoverTrack TrackAt(int index, double seconds)
		{
			if (double.IsNaN(seconds) || step == long.MinValue)
			{
				return default;
			}
			float f = StepFraction(seconds);
			float w = GroundCover.YoungWeight(seconds);
			if (state[index] == TexelState.Own)
			{
				return Blend(oldAt[index], oldNext[index], youngAt[index], youngNext[index], f, w);
			}
			return Blend(bgOldAt, bgOldNext, bgYoungAt, bgYoungNext, f, w);
		}

		/// <summary>How far toward the next step a moment is; a moment the map has not stepped to shows the next step.</summary>
		private float StepFraction(double seconds)
		{
			long s = GroundCover.StepOf(seconds);
			return s == step ? GroundCover.StepFraction(seconds) : s > step ? 1f : 0f;
		}

		private static CoverTrack Blend(in CoverTrack oldA, in CoverTrack oldB, in CoverTrack youngA, in CoverTrack youngB, float f, float w)
		{
			return CoverTrack.Lerp(CoverTrack.Lerp(oldA, oldB, f), CoverTrack.Lerp(youngA, youngB, f), w);
		}

		/// <summary>
		/// The whole map from the ground's memory: the shared track for both generations, then which texels a storm has
		/// reached in it, to be worked out on their own.
		/// </summary>
		private void Rebuild(long g, long j)
		{
			step = g;
			generation = j;
			bgHistory.Clear();
			long youngStart = GroundCover.StartStep(j);
			long oldStart = youngStart - GroundCover.StepsPerGeneration;
			bgHistory[j - 1] = SharedTrack(oldStart, g + 1);
			bgHistory[j] = SharedTrack(youngStart, g + 1);
			SetSharedFromHistory();

			pending.Clear();
			for (int i = 0; i < state.Length; i++)
			{
				state[i] = TexelState.Shared;
				firstTouch[i] = long.MaxValue;
			}
			MarkTouches(new RectInt(0, 0, resolution, resolution), oldStart + 1, g + 1);
			valid = true;
			dirty = true;
		}

		/// <summary>The shared (storm-free) track from a generation's start to <paramref name="last"/>.</summary>
		private List<CoverTrack> SharedTrack(long start, long last)
		{
			var history = new List<CoverTrack>((int)(last - start + 2));
			CoverTrack track = GroundCover.Prior(Forcing(start));
			history.Add(track);
			for (long k = start + 1; k <= last; k++)
			{
				GroundCover.Forcing f = Forcing(k);
				track = GroundCover.Advance(track, GroundCover.OpenFrame(f), f, deep);
				history.Add(track);
			}
			return history;
		}

		private void SetSharedFromHistory()
		{
			List<CoverTrack> older = bgHistory[generation - 1];
			List<CoverTrack> younger = bgHistory[generation];
			long oldStart = GroundCover.StartStep(generation - 1);
			long youngStart = GroundCover.StartStep(generation);
			bgOldAt = older[(int)(step - oldStart)];
			bgOldNext = older[(int)(step + 1 - oldStart)];
			bgYoungAt = younger[(int)(step - youngStart)];
			bgYoungNext = younger[(int)(step + 1 - youngStart)];
		}

		/// <summary>
		/// Marks the texels of <paramref name="region"/> a storm reaches at any step from <paramref name="from"/> to
		/// <paramref name="to"/>, at the first such step, as waiting to be worked out. By each storm's reach, which is a
		/// little more than it touches: a texel marked early was the shared ground exactly until then, so it comes out
		/// the same.
		/// </summary>
		private void MarkTouches(RectInt region, long from, long to)
		{
			float texel = TexelMeters;
			for (long k = from; k <= to; k++)
			{
				GroundCover.Forcing f = Forcing(k);
				for (int c = 0; c < f.Cells.Count; c++)
				{
					StormCell cell = f.Cells[c];
					if (cell.EnvelopeAtSeconds(f.Seconds) <= 0f)
					{
						continue;
					}
					Vector2 centre = cell.CentreAtSeconds(f.Seconds);
					float reach = cell.ReachMeters + texel;
					int x0 = Mathf.Max(region.xMin, Mathf.FloorToInt((centre.x - reach) / texel) - origin.x);
					int x1 = Mathf.Min(region.xMax - 1, Mathf.FloorToInt((centre.x + reach) / texel) - origin.x);
					int y0 = Mathf.Max(region.yMin, Mathf.FloorToInt((centre.y - reach) / texel) - origin.y);
					int y1 = Mathf.Min(region.yMax - 1, Mathf.FloorToInt((centre.y + reach) / texel) - origin.y);
					for (int y = y0; y <= y1; y++)
					{
						for (int x = x0; x <= x1; x++)
						{
							int i = y * resolution + x;
							if (state[i] == TexelState.Own || firstTouch[i] <= k)
							{
								continue;
							}
							firstTouch[i] = k;
							if (state[i] != TexelState.Pending)
							{
								state[i] = TexelState.Pending;
								pending.Enqueue(i);
							}
						}
					}
				}
			}
		}

		/// <summary>Works waiting texels out, from the step a storm first reached each, for at most <paramref name="milliseconds"/>.</summary>
		private void WorkPending(float milliseconds)
		{
			if (pending.Count == 0)
			{
				return;
			}
			Stopwatch watch = Stopwatch.StartNew();
			while (pending.Count > 0 && watch.Elapsed.TotalMilliseconds < milliseconds)
			{
				int i = pending.Dequeue();
				if (state[i] != TexelState.Pending)
				{
					continue;
				}
				Integrate(i);
				dirty = true;
			}
		}

		private void Integrate(int index)
		{
			Vector3 position = TexelCentre(index);
			IntegrateGeneration(index, position, generation - 1, out oldAt[index], out oldNext[index]);
			IntegrateGeneration(index, position, generation, out youngAt[index], out youngNext[index]);
			state[index] = TexelState.Own;
		}

		private void IntegrateGeneration(int index, Vector3 position, long gen, out CoverTrack at, out CoverTrack next)
		{
			long start = GroundCover.StartStep(gen);
			List<CoverTrack> history = bgHistory[gen];
			long from = System.Math.Max(firstTouch[index] - 1, start);
			CoverTrack track = history[(int)(from - start)];
			at = track;
			next = track;
			for (long k = from + 1; k <= step + 1; k++)
			{
				GroundCover.Forcing f = Forcing(k);
				track = GroundCover.Advance(track, GroundCover.FrameAt(f, position), f, deep);
				if (k == step)
				{
					at = track;
				}
				else if (k == step + 1)
				{
					next = track;
				}
			}
		}

		/// <summary>One step on: the shared track, the texels worked out on their own, and those a storm reaches now.</summary>
		private void StepForward()
		{
			long next = step + 1;
			GroundCover.Forcing ahead = Forcing(next + 1);
			WeatherFrame open = GroundCover.OpenFrame(ahead);
			bool roll = next == GroundCover.StartStep(generation + 1);
			if (roll)
			{
				// A new generation starts: the young one becomes the old, and the new one starts from the climate's ground.
				CoverTrack start = GroundCover.Prior(Forcing(next));
				bgOldAt = bgYoungNext;
				bgOldNext = GroundCover.Advance(bgOldAt, open, ahead, deep);
				bgYoungAt = start;
				bgYoungNext = GroundCover.Advance(start, open, ahead, deep);
				bgHistory.Remove(generation - 1);
				bgHistory[generation].Add(bgOldNext);
				bgHistory[generation + 1] = new List<CoverTrack>(GroundCover.StepsPerGeneration + 2) { bgYoungAt, bgYoungNext };
			}
			else
			{
				bgOldAt = bgOldNext;
				bgOldNext = GroundCover.Advance(bgOldAt, open, ahead, deep);
				bgYoungAt = bgYoungNext;
				bgYoungNext = GroundCover.Advance(bgYoungAt, open, ahead, deep);
				bgHistory[generation - 1].Add(bgOldNext);
				bgHistory[generation].Add(bgYoungNext);
			}

			for (int i = 0; i < state.Length; i++)
			{
				if (state[i] != TexelState.Own)
				{
					continue;
				}
				WeatherFrame frame = GroundCover.FrameAt(ahead, TexelCentre(i));
				if (roll)
				{
					oldAt[i] = youngNext[i];
					youngAt[i] = bgYoungAt;
				}
				else
				{
					oldAt[i] = oldNext[i];
					youngAt[i] = youngNext[i];
				}
				oldNext[i] = GroundCover.Advance(oldAt[i], frame, ahead, deep);
				youngNext[i] = GroundCover.Advance(youngAt[i], frame, ahead, deep);
				// Back to the shared ground exactly: it shares again until a storm reaches it.
				if (oldAt[i].SameAs(bgOldAt) && oldNext[i].SameAs(bgOldNext) && youngAt[i].SameAs(bgYoungAt) && youngNext[i].SameAs(bgYoungNext))
				{
					state[i] = TexelState.Shared;
					firstTouch[i] = long.MaxValue;
				}
			}
			if (roll)
			{
				generation++;
			}
			step = next;
			TouchShared(ahead);
			dirty = true;
		}

		/// <summary>The shared texels a storm reaches at <paramref name="ahead"/>'s step: they leave the shared track there.</summary>
		private void TouchShared(GroundCover.Forcing ahead)
		{
			float texel = TexelMeters;
			for (int c = 0; c < ahead.Cells.Count; c++)
			{
				StormCell cell = ahead.Cells[c];
				if (cell.EnvelopeAtSeconds(ahead.Seconds) <= 0f)
				{
					continue;
				}
				Vector2 centre = cell.CentreAtSeconds(ahead.Seconds);
				float reach = cell.ReachMeters + texel;
				int x0 = Mathf.Max(0, Mathf.FloorToInt((centre.x - reach) / texel) - origin.x);
				int x1 = Mathf.Min(resolution - 1, Mathf.FloorToInt((centre.x + reach) / texel) - origin.x);
				int y0 = Mathf.Max(0, Mathf.FloorToInt((centre.y - reach) / texel) - origin.y);
				int y1 = Mathf.Min(resolution - 1, Mathf.FloorToInt((centre.y + reach) / texel) - origin.y);
				for (int y = y0; y <= y1; y++)
				{
					for (int x = x0; x <= x1; x++)
					{
						int i = y * resolution + x;
						if (state[i] != TexelState.Shared)
						{
							continue;
						}
						// It was the shared ground up to this step; from here it takes the weather over its own place.
						WeatherFrame frame = GroundCover.FrameAt(ahead, TexelCentre(i));
						oldAt[i] = bgOldAt;
						youngAt[i] = bgYoungAt;
						oldNext[i] = GroundCover.Advance(bgOldAt, frame, ahead, deep);
						youngNext[i] = GroundCover.Advance(bgYoungAt, frame, ahead, deep);
						state[i] = TexelState.Own;
						firstTouch[i] = ahead.Step;
					}
				}
			}
		}

		/// <summary>
		/// Moves the window with the camera, in whole texels fixed to the world. What was already in view keeps what it
		/// was worked out to; the strip coming into view is worked out from the ground's memory.
		/// </summary>
		private void Recentre(Vector2Int wanted)
		{
			int shiftX = wanted.x - origin.x;
			int shiftY = wanted.y - origin.y;
			if (Mathf.Abs(shiftX) >= resolution || Mathf.Abs(shiftY) >= resolution)
			{
				origin = wanted;
				ResetTexels();
				return;
			}
			int n = resolution * resolution;
			var newState = new TexelState[n];
			var newTouch = new long[n];
			var newOldAt = new CoverTrack[n];
			var newOldNext = new CoverTrack[n];
			var newYoungAt = new CoverTrack[n];
			var newYoungNext = new CoverTrack[n];
			for (int y = 0; y < resolution; y++)
			{
				for (int x = 0; x < resolution; x++)
				{
					int i = y * resolution + x;
					int sx = x + shiftX;
					int sy = y + shiftY;
					if (sx >= 0 && sy >= 0 && sx < resolution && sy < resolution)
					{
						int s = sy * resolution + sx;
						newState[i] = state[s];
						newTouch[i] = firstTouch[s];
						newOldAt[i] = oldAt[s];
						newOldNext[i] = oldNext[s];
						newYoungAt[i] = youngAt[s];
						newYoungNext[i] = youngNext[s];
					}
					else
					{
						newState[i] = TexelState.Shared;
						newTouch[i] = long.MaxValue;
					}
				}
			}
			state = newState;
			firstTouch = newTouch;
			oldAt = newOldAt;
			oldNext = newOldNext;
			youngAt = newYoungAt;
			youngNext = newYoungNext;
			origin = wanted;
			// The queue held old indices: what still waits is found again from the states.
			pending.Clear();
			for (int i = 0; i < n; i++)
			{
				if (state[i] == TexelState.Pending)
				{
					pending.Enqueue(i);
				}
			}
			long oldStart = GroundCover.StartStep(generation - 1);
			// The strips that came into view: columns on one side, rows on the other.
			if (shiftX != 0)
			{
				int x0 = shiftX > 0 ? resolution - shiftX : 0;
				MarkTouches(new RectInt(x0, 0, Mathf.Abs(shiftX), resolution), oldStart + 1, step + 1);
			}
			if (shiftY != 0)
			{
				int y0 = shiftY > 0 ? resolution - shiftY : 0;
				MarkTouches(new RectInt(0, y0, resolution, Mathf.Abs(shiftY)), oldStart + 1, step + 1);
			}
			dirty = true;
		}

		private void ResetTexels()
		{
			pending.Clear();
			for (int i = 0; i < state.Length; i++)
			{
				state[i] = TexelState.Shared;
				firstTouch[i] = long.MaxValue;
			}
			MarkTouches(new RectInt(0, 0, resolution, resolution), GroundCover.StartStep(generation - 1) + 1, step + 1);
			dirty = true;
		}

		/// <summary>The ground of <paramref name="seconds"/> into the textures.</summary>
		private void Draw(double seconds)
		{
			drawnSeconds = seconds;
			sinceDraw = 0f;
			dirty = false;
			if (step == long.MinValue)
			{
				return;
			}
			depthMax = deep.MaxMetres;
			float f = StepFraction(seconds);
			float w = GroundCover.YoungWeight(seconds);
			CoverTrack shared = Blend(bgOldAt, bgOldNext, bgYoungAt, bgYoungNext, f, w);
			Color32 sharedPixel = Encode(shared.Cover);
			byte sharedDepth = EncodeDepth(shared.Depth, depthMax);
			for (int i = 0; i < pixels.Length; i++)
			{
				if (state[i] == TexelState.Own)
				{
					CoverTrack own = Blend(oldAt[i], oldNext[i], youngAt[i], youngNext[i], f, w);
					pixels[i] = Encode(own.Cover);
					depthPixels[i] = EncodeDepth(own.Depth, depthMax);
				}
				else
				{
					pixels[i] = sharedPixel;
					depthPixels[i] = sharedDepth;
				}
			}
			Upload();
		}

		private void DrawHeld()
		{
			Color32 pixel = Encode(heldCover);
			byte depthByte = EncodeDepth(heldDepth, depthMax);
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = pixel;
				depthPixels[i] = depthByte;
			}
			Upload();
			valid = true;
			Publish();
		}

		private void Upload()
		{
			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			depthTexture.SetPixelData(depthPixels, 0);
			depthTexture.Apply(false, false);
		}

		private static Color32 Encode(in WeatherCover c)
		{
			return new Color32(
				(byte)(Mathf.Clamp01(c.Snow) * 255f),
				(byte)(Mathf.Clamp01(c.Wet) * 255f),
				(byte)(Mathf.Clamp01(c.Ash) * 255f),
				(byte)(Mathf.Clamp01(c.Sand) * 255f));
		}

		private static byte EncodeDepth(float metres, float maxMetres) =>
			maxMetres > 0f ? (byte)Mathf.RoundToInt(Mathf.Clamp01(metres / maxMetres) * 255f) : (byte)0;

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
			depthTexture = new Texture2D(resolution, resolution, TextureFormat.R8, false, true)
			{
				name = "Weather Deep Snow",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			int n = resolution * resolution;
			pixels = new Color32[n];
			depthPixels = new byte[n];
			state = new TexelState[n];
			firstTouch = new long[n];
			oldAt = new CoverTrack[n];
			oldNext = new CoverTrack[n];
			youngAt = new CoverTrack[n];
			youngNext = new CoverTrack[n];
			depthMax = DeepSnowSettings().MaxMetres;
			step = long.MinValue;
			drawnSeconds = double.NaN;
		}

		private void Publish()
		{
			float texel = TexelMeters;
			Shader.SetGlobalTexture(TextureId, texture);
			Shader.SetGlobalVector(RectId, new Vector4(origin.x * texel, origin.y * texel, size, size));
			Shader.SetGlobalVector(ParamsId, new Vector4(valid ? 1f : 0f, texel, 0f, 0f));
			Shader.SetGlobalTexture(DepthTextureId, depthTexture);
			Shader.SetGlobalVector(DepthParamsId, new Vector4(valid ? depthMax : 0f, 0f, 0f, 0f));
		}
	}
}
