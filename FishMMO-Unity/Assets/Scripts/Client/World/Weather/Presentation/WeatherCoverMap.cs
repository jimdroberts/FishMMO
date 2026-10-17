using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.Profiling;
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
	/// <para>
	/// <b>On worker threads.</b> Each storm-reached texel is its own sum (<see cref="IntegrateTexel"/>), so a batch of
	/// them is worked out across the machine's cores, off the main thread between one frame's update and the next,
	/// and taken in on the next; the snap's batch is waited for. What a batch reads — the weather of each step
	/// (<see cref="CoverWork"/>), fetched here first, since the forcing cache is not for other threads — does not
	/// change while it runs: anything that would change it (a step, a move, a rebuild) waits for the batch first.
	/// Managed threads and the same arithmetic in the same order, never Burst: each texel comes out to the bit
	/// what the server's ground is.
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
		/// <summary>
		/// The seasonal snow line (GroundCover.SnowLine) for what lies past the map: x the height the pack starts at, y where
		/// it is whole (world y), z 1 while snow may lie anywhere in view (the map's own, or terrain reaching the line: the
		/// gate the snow lift and the ground's snow blend take), w how much of a cold enough place it covers (0: no line).
		/// </summary>
		public static readonly int SnowLineId = Shader.PropertyToID("_FishSnowLine");

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

		/// <summary>
		/// The open air's ground, one per temperature band (<see cref="GroundCover.BandAt"/>): a texel no storm has reached
		/// shows its band's. Indexed by band; only the bands some texel is in are worked out.
		/// </summary>
		private readonly CoverTrack[] bgOldAt = new CoverTrack[GroundCover.BandCount], bgOldNext = new CoverTrack[GroundCover.BandCount],
			bgYoungAt = new CoverTrack[GroundCover.BandCount], bgYoungNext = new CoverTrack[GroundCover.BandCount];
		/// <summary>Each live generation's shared tracks, one per band in use (null for the rest), from its start step to the step after the map's.</summary>
		private readonly Dictionary<long, List<CoverTrack>[]> bgHistory = new Dictionary<long, List<CoverTrack>[]>();
		/// <summary>Each texel's band: its own climate against the area's middle.</summary>
		private byte[] band;
		/// <summary>Which bands some texel is in, and so are worked out; and the list of them.</summary>
		private readonly bool[] bandUsed = new bool[GroundCover.BandCount];
		private readonly List<int> usedBands = new List<int>();
		// What the bands were worked out for: a change to any of these works them out again.
		private WorldSceneSettings bandsSettings;
		private Rect bandsArea;
		private Vector2Int bandsOrigin;
		private float bandsMiddle;

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
		// The snow line now (GroundCover.SnowLine), the most snow on the map at its last draw, and the scene's highest ground.
		private bool lineOn;
		private float lineStart, lineWhole, lineShare;
		private float mapMostSnow;
		private Scene topScene;
		private float terrainTop = float.PositiveInfinity;
		private float heldDepth;

		// The batch of storm-reached texels being worked out on workers: their indices in queue order, what they read,
		// and how many of them have been claimed (each one claimed is finished; the rest go back to the queue).
		private Task working;
		private int[] batch = System.Array.Empty<int>();
		private int batchCount;
		private int batchClaimed;
		private long batchDeadline;
		private volatile bool batchStop;
		private CoverWork batchWork;
		private GroundCover.Forcing[] batchForcing = System.Array.Empty<GroundCover.Forcing>();
		private System.Action batchBody;
		private readonly List<int> touchedScratch = new List<int>();

		private static readonly ProfilerMarker WaitMarker = new ProfilerMarker("Weather.CoverMap.Wait");
		private static readonly ProfilerMarker LaunchMarker = new ProfilerMarker("Weather.CoverMap.Launch");
		private static readonly ProfilerMarker StepMarker = new ProfilerMarker("Weather.CoverMap.Step");

		public Texture2D Texture => texture;
		public bool IsValid => valid;
		public Rect Area => new Rect(origin.x * TexelMeters, origin.y * TexelMeters, size, size);
		public float TexelMeters => resolution > 0 ? size / resolution : 0f;
		/// <summary>Storm-reached texels still to be worked out, those on the workers now included.</summary>
		public int PendingTexels => pending.Count + (working != null ? batchCount : 0);

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
			// What the workers finished since the last frame.
			if (working != null && working.IsCompleted)
			{
				Finish();
			}
			Ensure(res, sizeMeters);
			sinceDraw += Mathf.Max(0f, realDeltaTime);
			sinceRebuild += Mathf.Max(0f, realDeltaTime);
			bool snap = WeatherClient.Snaps != seenSnaps;
			seenSnaps = WeatherClient.Snaps;
			if (held)
			{
				lineOn = false;
				Publish();
				return;
			}
			if (timeline == null)
			{
				valid = false;
				lineOn = false;
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

			lineOn = GroundCover.SnowLine(Forcing(g), out lineStart, out lineWhole, out lineShare);
			// The snap's batch is waited for, so the ground lands with the rest of the weather; any other runs on until the
			// next frame.
			WorkPending(snap ? SnapBudgetMilliseconds : BudgetMilliseconds, snap);
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
			WorkPending(float.MaxValue, true);
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
					? SharedAt(band[(resolution / 2) * resolution + resolution / 2], StepFraction(drawnSeconds), GroundCover.YoungWeight(drawnSeconds)).Cover
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
			lineOn = false;
			mapMostSnow = 0f;
			Shader.SetGlobalVector(ParamsId, Vector4.zero);
			Shader.SetGlobalVector(DepthParamsId, Vector4.zero);
			Shader.SetGlobalVector(SnowLineId, Vector4.zero);
		}

		public void Dispose()
		{
			// The workers write into the arrays let go of here.
			Finish(true);
			Clear();
			Destroy(ref texture);
			Destroy(ref depthTexture);
			pixels = null;
			depthPixels = null;
			state = null;
			firstTouch = null;
			oldAt = oldNext = youngAt = youngNext = null;
			band = null;
			bandsSettings = null;
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
			return SharedAt(band[index], f, w);
		}

		/// <summary>A band's shared ground at a point between the map's step and the next.</summary>
		private CoverTrack SharedAt(int b, float f, float w) => Blend(bgOldAt[b], bgOldNext[b], bgYoungAt[b], bgYoungNext[b], f, w);

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
			// Everything is worked out afresh: a batch on the workers is cut short (what it finished is still right).
			Finish(true);
			step = g;
			generation = j;
			RefreshBands();
			bgHistory.Clear();
			bgHistory[j - 1] = new List<CoverTrack>[GroundCover.BandCount];
			bgHistory[j] = new List<CoverTrack>[GroundCover.BandCount];
			foreach (int b in usedBands)
			{
				BuildBandHistory(b);
			}

			pending.Clear();
			for (int i = 0; i < state.Length; i++)
			{
				state[i] = TexelState.Shared;
				firstTouch[i] = long.MaxValue;
			}
			MarkTouches(new RectInt(0, 0, resolution, resolution), GroundCover.StartStep(j - 1) + 1, g + 1);
			valid = true;
			dirty = true;
		}

		/// <summary>A band's shared track for both live generations, from their starts to the step after the map's, and its tracks now.</summary>
		private void BuildBandHistory(int b)
		{
			float offset = GroundCover.BandOffset(b);
			bgHistory[generation - 1][b] = SharedTrack(GroundCover.StartStep(generation - 1), step + 1, offset);
			bgHistory[generation][b] = SharedTrack(GroundCover.StartStep(generation), step + 1, offset);
			SetSharedFromHistory(b);
		}

		/// <summary>The shared (storm-free) track of a band from a generation's start to <paramref name="last"/>.</summary>
		private List<CoverTrack> SharedTrack(long start, long last, float offset)
		{
			var history = new List<CoverTrack>((int)(last - start + 2));
			CoverTrack track = GroundCover.Prior(Forcing(start), offset);
			history.Add(track);
			for (long k = start + 1; k <= last; k++)
			{
				GroundCover.Forcing f = Forcing(k);
				track = GroundCover.Advance(track, GroundCover.OpenFrame(f, offset), f, deep, offset);
				history.Add(track);
			}
			return history;
		}

		private void SetSharedFromHistory(int b)
		{
			List<CoverTrack> older = bgHistory[generation - 1][b];
			List<CoverTrack> younger = bgHistory[generation][b];
			long oldStart = GroundCover.StartStep(generation - 1);
			long youngStart = GroundCover.StartStep(generation);
			bgOldAt[b] = older[(int)(step - oldStart)];
			bgOldNext[b] = older[(int)(step + 1 - oldStart)];
			bgYoungAt[b] = younger[(int)(step - youngStart)];
			bgYoungNext[b] = younger[(int)(step + 1 - youngStart)];
		}

		/// <summary>
		/// Each texel's band, worked out again when the scene, its area or the window has moved (main thread: it reads the
		/// terrain); otherwise kept.
		/// </summary>
		private void RefreshBands()
		{
			Rect area = timeline != null ? timeline.Area : default;
			if (band != null && bandsSettings != null && ReferenceEquals(bandsSettings, settings) && area == bandsArea && origin == bandsOrigin)
			{
				return;
			}
			bandsSettings = settings;
			bandsArea = area;
			bandsOrigin = origin;
			bandsMiddle = GroundCover.MiddleKelvin(timeline, settings);
			BandRegion(new RectInt(0, 0, resolution, resolution));
		}

		/// <summary>The bands of a region's texels, and the list of bands in use after them.</summary>
		private void BandRegion(RectInt region)
		{
			for (int y = region.yMin; y < region.yMax; y++)
			{
				for (int x = region.xMin; x < region.xMax; x++)
				{
					int i = y * resolution + x;
					band[i] = (byte)GroundCover.BandAt(settings, TexelCentre(i), bandsMiddle);
				}
			}
			System.Array.Clear(bandUsed, 0, bandUsed.Length);
			for (int i = 0; i < band.Length; i++)
			{
				bandUsed[band[i]] = true;
			}
			usedBands.Clear();
			for (int b = 0; b < bandUsed.Length; b++)
			{
				if (bandUsed[b])
				{
					usedBands.Add(b);
				}
			}
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

		/// <summary>
		/// Works waiting texels out, from the step a storm first reached each, for at most <paramref name="milliseconds"/>
		/// of the workers' time: every one waiting is handed to them in queue order, and what they have not reached when
		/// the time is up goes back to the queue. <paramref name="wait"/> runs the batch now, on this thread and the
		/// workers together; otherwise it runs between this frame and the next, and a batch still running is left to.
		/// </summary>
		private void WorkPending(float milliseconds, bool wait)
		{
			if (working != null)
			{
				if (!wait)
				{
					return;
				}
				Finish();
			}
			if (pending.Count == 0)
			{
				return;
			}
			LaunchMarker.Begin();
			if (batch.Length < state.Length)
			{
				batch = new int[state.Length];
			}
			batchCount = 0;
			long earliest = long.MaxValue;
			while (pending.Count > 0)
			{
				int i = pending.Dequeue();
				if (state[i] != TexelState.Pending)
				{
					continue;
				}
				batch[batchCount++] = i;
				earliest = System.Math.Min(earliest, firstTouch[i]);
			}
			if (batchCount == 0)
			{
				LaunchMarker.End();
				return;
			}
			// Every step any of them is worked through, fetched here: the forcing cache is the main thread's.
			long first = System.Math.Max(earliest - 1, GroundCover.StartStep(generation - 1)) + 1;
			FetchForcing(timeline, settings, scene, first, step + 1, ref batchForcing);
			batchWork = new CoverWork
			{
				Step = step,
				Generation = generation,
				OldHistories = bgHistory[generation - 1],
				YoungHistories = bgHistory[generation],
				Bands = band,
				Forcing = batchForcing,
				ForcingBase = first,
				Deep = deep,
				Origin = origin,
				Resolution = resolution,
				TexelMeters = TexelMeters,
			};
			batchClaimed = 0;
			batchStop = false;
			batchBudget = milliseconds >= float.MaxValue ? long.MaxValue : (long)(milliseconds * (Stopwatch.Frequency / 1000.0));
			batchBody ??= RunBatch;
			LaunchMarker.End();
			if (wait)
			{
				WaitMarker.Begin();
				bool failed = false;
				try
				{
					RunBatch();
				}
				catch (System.Exception e)
				{
					UnityEngine.Debug.LogException(e is System.AggregateException a && a.InnerException != null ? a.InnerException : e);
					failed = true;
				}
				WaitMarker.End();
				TakeBatch(failed);
			}
			else
			{
				working = Task.Run(batchBody);
			}
		}

		/// <summary>The batch on as many workers as the machine has cores to spare, each claiming the next texel in turn.</summary>
		private void RunBatch()
		{
			long budget = batchBudget;
			batchDeadline = budget == long.MaxValue ? long.MaxValue : Stopwatch.GetTimestamp() + budget;
			int workers = Mathf.Clamp(System.Environment.ProcessorCount - 1, 1, MaxWorkers);
			claimBody ??= ClaimTexels;
			Parallel.For(0, workers, claimBody);
		}

		private void ClaimTexels(int worker)
		{
			while (!batchStop && Stopwatch.GetTimestamp() < batchDeadline)
			{
				int slot = Interlocked.Increment(ref batchClaimed) - 1;
				if (slot >= batchCount)
				{
					return;
				}
				// A texel claimed is always finished, so those finished are the queue's head.
				int index = batch[slot];
				IntegrateTexel(batchWork, index, firstTouch[index], out oldAt[index], out oldNext[index], out youngAt[index], out youngNext[index]);
			}
		}

		/// <summary>Waits for the batch on the workers (cut short with <paramref name="stop"/>) and takes in what it finished.</summary>
		private void Finish(bool stop = false)
		{
			if (working == null)
			{
				return;
			}
			if (stop)
			{
				batchStop = true;
			}
			WaitMarker.Begin();
			bool failed = false;
			try
			{
				working.Wait();
			}
			catch (System.AggregateException e)
			{
				UnityEngine.Debug.LogException(e.InnerException ?? e);
				failed = true;
			}
			WaitMarker.End();
			working = null;
			TakeBatch(failed);
		}

		/// <summary>The texels a batch finished are the map's own from now; the rest wait again, in the order they waited.</summary>
		private void TakeBatch(bool failed)
		{
			int done = failed ? 0 : Mathf.Min(batchClaimed, batchCount);
			for (int s = 0; s < done; s++)
			{
				state[batch[s]] = TexelState.Own;
			}
			for (int s = done; s < batchCount; s++)
			{
				pending.Enqueue(batch[s]);
			}
			if (done > 0)
			{
				dirty = true;
			}
			batchCount = 0;
			batchWork = default;
		}

		/// <summary>The most workers a batch is spread over.</summary>
		private const int MaxWorkers = 16;
		/// <summary>Fewer texels than this reached by a storm at a step are worked out on this thread alone.</summary>
		private const int ParallelMinimum = 64;
		private long batchBudget;
		private System.Action<int> claimBody;

		/// <summary>
		/// What working storm-reached texels out reads (<see cref="IntegrateTexel"/>): the map's step and generation, the
		/// shared track of both generations from their starts, the weather of each step from <see cref="ForcingBase"/> on,
		/// and the window the texels are in. Nothing in it changes while a batch is worked.
		/// </summary>
		public struct CoverWork
		{
			public long Step;
			/// <summary>The young generation; the old is the one before it.</summary>
			public long Generation;
			public List<CoverTrack> OldHistory, YoungHistory;
			/// <summary>The shared tracks of every band (<see cref="Bands"/>); null for one shared track (<see cref="OldHistory"/>) and no offsets.</summary>
			public List<CoverTrack>[] OldHistories, YoungHistories;
			/// <summary>Each texel's band; null for none.</summary>
			public byte[] Bands;
			public GroundCover.Forcing[] Forcing;
			public long ForcingBase;
			public DeepSnow Deep;
			public Vector2Int Origin;
			public int Resolution;
			public float TexelMeters;

			/// <summary>The world position of a texel's middle, as the map works it out.</summary>
			public Vector3 TexelCentre(int index)
			{
				int x = index % Resolution;
				int y = index / Resolution;
				return new Vector3((Origin.x + x + 0.5f) * TexelMeters, 0f, (Origin.y + y + 0.5f) * TexelMeters);
			}
		}

		/// <summary>
		/// The weather of steps <paramref name="first"/> to <paramref name="last"/> into <paramref name="into"/> (grown as
		/// needed), with each storm's own weather in it worked out now: <see cref="GroundCover.FrameAt"/> reads it lazily,
		/// and a worker must only read it.
		/// </summary>
		public static void FetchForcing(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, long first, long last,
			ref GroundCover.Forcing[] into)
		{
			int count = (int)System.Math.Max(0L, last - first + 1);
			if (into.Length < count)
			{
				into = new GroundCover.Forcing[System.Math.Max(count, 2 * GroundCover.StepsPerGeneration + 4)];
			}
			for (int k = 0; k < count; k++)
			{
				GroundCover.Forcing f = GroundCover.ForcingAt(timeline, settings, scene, first + k);
				WarmStorms(f);
				into[k] = f;
			}
		}

		/// <summary>Works out each storm kind's weather a step holds, so <see cref="GroundCover.FrameAt"/> only reads it.</summary>
		public static void WarmStorms(GroundCover.Forcing forcing)
		{
			if (!forcing.HasWeather || forcing.Storms == null)
			{
				return;
			}
			for (int c = 0; c < forcing.Cells.Count; c++)
			{
				forcing.Storms.Of(forcing.Cells[c].Kind);
			}
		}

		/// <summary>
		/// One storm-reached texel, both generations, from the step a storm first reached it (<paramref name="firstTouch"/>)
		/// to the map's step and the next. Reads only <paramref name="work"/>, so any thread may run it, and it comes out
		/// to the bit the same on every one.
		/// </summary>
		public static void IntegrateTexel(in CoverWork work, int index, long firstTouch,
			out CoverTrack oldAt, out CoverTrack oldNext, out CoverTrack youngAt, out CoverTrack youngNext)
		{
			Vector3 position = work.TexelCentre(index);
			int b = work.Bands != null ? work.Bands[index] : GroundCover.BandReach;
			float offset = work.Bands != null ? GroundCover.BandOffset(b) : 0f;
			List<CoverTrack> older = work.OldHistories != null ? work.OldHistories[b] : work.OldHistory;
			List<CoverTrack> younger = work.YoungHistories != null ? work.YoungHistories[b] : work.YoungHistory;
			IntegrateGeneration(work, position, offset, firstTouch, GroundCover.StartStep(work.Generation - 1), older, out oldAt, out oldNext);
			IntegrateGeneration(work, position, offset, firstTouch, GroundCover.StartStep(work.Generation), younger, out youngAt, out youngNext);
		}

		private static void IntegrateGeneration(in CoverWork work, Vector3 position, float offset, long firstTouch, long start, List<CoverTrack> history,
			out CoverTrack at, out CoverTrack next)
		{
			long from = System.Math.Max(firstTouch - 1, start);
			CoverTrack track = history[(int)(from - start)];
			at = track;
			next = track;
			long step = work.Step;
			for (long k = from + 1; k <= step + 1; k++)
			{
				GroundCover.Forcing f = work.Forcing[k - work.ForcingBase];
				track = GroundCover.Advance(track, GroundCover.FrameAt(f, position, offset), f, work.Deep, offset);
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

		/// <summary>
		/// Works <paramref name="count"/> texels of <paramref name="indices"/> out into the four track arrays (indexed by
		/// texel), on workers or here: the same sums either way. For the tests, which hold the two to each other and to
		/// the map's arithmetic step by step.
		/// </summary>
		public static void IntegrateTexels(CoverWork work, int[] indices, int count, long[] firstTouch,
			CoverTrack[] oldAt, CoverTrack[] oldNext, CoverTrack[] youngAt, CoverTrack[] youngNext, bool threaded)
		{
			if (!threaded)
			{
				for (int s = 0; s < count; s++)
				{
					int i = indices[s];
					IntegrateTexel(work, i, firstTouch[i], out oldAt[i], out oldNext[i], out youngAt[i], out youngNext[i]);
				}
				return;
			}
			Parallel.For(0, count, s =>
			{
				int i = indices[s];
				IntegrateTexel(work, i, firstTouch[i], out oldAt[i], out oldNext[i], out youngAt[i], out youngNext[i]);
			});
		}

		/// <summary>One step on: the shared track, the texels worked out on their own, and those a storm reaches now.</summary>
		private void StepForward()
		{
			// The workers read the step and the shared track this moves on.
			Finish();
			StepMarker.Begin();
			long next = step + 1;
			GroundCover.Forcing ahead = Forcing(next + 1);
			WarmStorms(ahead);
			bool roll = next == GroundCover.StartStep(generation + 1);
			GroundCover.Forcing atStart = roll ? Forcing(next) : null;
			if (roll)
			{
				bgHistory.Remove(generation - 1);
				bgHistory[generation + 1] = new List<CoverTrack>[GroundCover.BandCount];
			}
			foreach (int b in usedBands)
			{
				float offset = GroundCover.BandOffset(b);
				WeatherFrame open = GroundCover.OpenFrame(ahead, offset);
				if (roll)
				{
					// A new generation starts: the young one becomes the old, and the new one starts from the climate's ground.
					CoverTrack start = GroundCover.Prior(atStart, offset);
					bgOldAt[b] = bgYoungNext[b];
					bgOldNext[b] = GroundCover.Advance(bgOldAt[b], open, ahead, deep, offset);
					bgYoungAt[b] = start;
					bgYoungNext[b] = GroundCover.Advance(start, open, ahead, deep, offset);
					bgHistory[generation][b].Add(bgOldNext[b]);
					bgHistory[generation + 1][b] = new List<CoverTrack>(GroundCover.StepsPerGeneration + 2) { bgYoungAt[b], bgYoungNext[b] };
				}
				else
				{
					bgOldAt[b] = bgOldNext[b];
					bgOldNext[b] = GroundCover.Advance(bgOldAt[b], open, ahead, deep, offset);
					bgYoungAt[b] = bgYoungNext[b];
					bgYoungNext[b] = GroundCover.Advance(bgYoungAt[b], open, ahead, deep, offset);
					bgHistory[generation - 1][b].Add(bgOldNext[b]);
					bgHistory[generation][b].Add(bgYoungNext[b]);
				}
			}

			// Each texel worked out on its own steps alone, so the rows go to the workers.
			stepAhead = ahead;
			stepRoll = roll;
			stepRowBody ??= StepRow;
			Parallel.For(0, resolution, stepRowBody);
			stepAhead = null;
			if (roll)
			{
				generation++;
			}
			step = next;
			TouchShared(ahead);
			dirty = true;
			StepMarker.End();
		}

		// What StepRow reads: the step being moved to and whether it starts a generation.
		private GroundCover.Forcing stepAhead;
		private bool stepRoll;
		private System.Action<int> stepRowBody;

		/// <summary>One row of the texels worked out on their own, a step on.</summary>
		private void StepRow(int y)
		{
			GroundCover.Forcing ahead = stepAhead;
			bool roll = stepRoll;
			int end = (y + 1) * resolution;
			for (int i = y * resolution; i < end; i++)
			{
				if (state[i] != TexelState.Own)
				{
					continue;
				}
				int b = band[i];
				float offset = GroundCover.BandOffset(b);
				WeatherFrame frame = GroundCover.FrameAt(ahead, TexelCentre(i), offset);
				if (roll)
				{
					oldAt[i] = youngNext[i];
					youngAt[i] = bgYoungAt[b];
				}
				else
				{
					oldAt[i] = oldNext[i];
					youngAt[i] = youngNext[i];
				}
				oldNext[i] = GroundCover.Advance(oldAt[i], frame, ahead, deep, offset);
				youngNext[i] = GroundCover.Advance(youngAt[i], frame, ahead, deep, offset);
				// Back to its band's shared ground exactly: it shares again until a storm reaches it.
				if (oldAt[i].SameAs(bgOldAt[b]) && oldNext[i].SameAs(bgOldNext[b]) && youngAt[i].SameAs(bgYoungAt[b]) && youngNext[i].SameAs(bgYoungNext[b]))
				{
					state[i] = TexelState.Shared;
					firstTouch[i] = long.MaxValue;
				}
			}
		}

		/// <summary>The shared texels a storm reaches at <paramref name="ahead"/>'s step: they leave the shared track there.</summary>
		private void TouchShared(GroundCover.Forcing ahead)
		{
			float texel = TexelMeters;
			// Which they are, here (a texel two storms reach is taken once); what each comes to, on the workers when many.
			touchedScratch.Clear();
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
						state[i] = TexelState.Own;
						firstTouch[i] = ahead.Step;
						touchedScratch.Add(i);
					}
				}
			}
			touchAhead = ahead;
			if (touchedScratch.Count < ParallelMinimum)
			{
				for (int t = 0; t < touchedScratch.Count; t++)
				{
					TouchTexel(t);
				}
			}
			else
			{
				touchBody ??= TouchTexel;
				Parallel.For(0, touchedScratch.Count, touchBody);
			}
			touchAhead = null;
		}

		private GroundCover.Forcing touchAhead;
		private System.Action<int> touchBody;

		/// <summary>It was the shared ground up to this step; from here it takes the weather over its own place.</summary>
		private void TouchTexel(int t)
		{
			int i = touchedScratch[t];
			GroundCover.Forcing ahead = touchAhead;
			int b = band[i];
			float offset = GroundCover.BandOffset(b);
			WeatherFrame frame = GroundCover.FrameAt(ahead, TexelCentre(i), offset);
			oldAt[i] = bgOldAt[b];
			youngAt[i] = bgYoungAt[b];
			oldNext[i] = GroundCover.Advance(bgOldAt[b], frame, ahead, deep, offset);
			youngNext[i] = GroundCover.Advance(bgYoungAt[b], frame, ahead, deep, offset);
		}

		/// <summary>
		/// Moves the window with the camera, in whole texels fixed to the world. What was already in view keeps what it
		/// was worked out to; the strip coming into view is worked out from the ground's memory. In place, row by row in
		/// the order that reads each row before it is written over: a camera crossing a texel no longer makes six new
		/// arrays of the whole map.
		/// </summary>
		private void Recentre(Vector2Int wanted)
		{
			// The workers write into these arrays by index.
			Finish();
			int shiftX = wanted.x - origin.x;
			int shiftY = wanted.y - origin.y;
			if (Mathf.Abs(shiftX) >= resolution || Mathf.Abs(shiftY) >= resolution)
			{
				origin = wanted;
				RefreshBands();
				EnsureBandHistories();
				ResetTexels();
				return;
			}
			int n = resolution * resolution;
			Shift(band, resolution, shiftX, shiftY, (byte)GroundCover.BandReach);
			Shift(state, resolution, shiftX, shiftY, TexelState.Shared);
			Shift(firstTouch, resolution, shiftX, shiftY, long.MaxValue);
			Shift(oldAt, resolution, shiftX, shiftY, default);
			Shift(oldNext, resolution, shiftX, shiftY, default);
			Shift(youngAt, resolution, shiftX, shiftY, default);
			Shift(youngNext, resolution, shiftX, shiftY, default);
			origin = wanted;
			bandsOrigin = origin;
			// The strips that came into view take their own bands; a band new to the window gets its shared track.
			if (shiftX != 0)
			{
				BandRegion(new RectInt(shiftX > 0 ? resolution - shiftX : 0, 0, Mathf.Abs(shiftX), resolution));
			}
			if (shiftY != 0)
			{
				BandRegion(new RectInt(0, shiftY > 0 ? resolution - shiftY : 0, resolution, Mathf.Abs(shiftY)));
			}
			EnsureBandHistories();
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

		/// <summary>
		/// A square grid's cells moved by (<paramref name="shiftX"/>, <paramref name="shiftY"/>) in place: cell (x, y) takes
		/// what cell (x + shiftX, y + shiftY) held, or <paramref name="fill"/> where that is off the grid.
		/// </summary>
		public static void Shift<T>(T[] cells, int n, int shiftX, int shiftY, T fill)
		{
			int span = n - Mathf.Abs(shiftX);
			int from = shiftX > 0 ? shiftX : 0;
			int to = shiftX < 0 ? -shiftX : 0;
			for (int r = 0; r < n; r++)
			{
				// Rows taken from above are written bottom up, those from below top down, so none is written before it is read.
				int y = shiftY >= 0 ? r : n - 1 - r;
				int source = y + shiftY;
				int row = y * n;
				if (source < 0 || source >= n || span <= 0)
				{
					System.Array.Fill(cells, fill, row, n);
					continue;
				}
				// Within one row the copy keeps what it overlaps.
				System.Array.Copy(cells, source * n + from, cells, row + to, span);
				if (shiftX > 0)
				{
					System.Array.Fill(cells, fill, row + span, shiftX);
				}
				else if (shiftX < 0)
				{
					System.Array.Fill(cells, fill, row, -shiftX);
				}
			}
		}

		/// <summary>A shared track for every band in use that has none yet (one a moved window brought in).</summary>
		private void EnsureBandHistories()
		{
			if (!bgHistory.ContainsKey(generation - 1) || !bgHistory.ContainsKey(generation))
			{
				return;
			}
			foreach (int b in usedBands)
			{
				if (bgHistory[generation - 1][b] == null || bgHistory[generation][b] == null)
				{
					BuildBandHistory(b);
				}
			}
		}

		private void ResetTexels()
		{
			Finish(true);
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
			foreach (int b in usedBands)
			{
				CoverTrack shared = SharedAt(b, f, w);
				sharedPixels[b] = Encode(shared.Cover);
				sharedDepths[b] = EncodeDepth(shared.Depth, depthMax);
			}
			byte most = 0;
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
					pixels[i] = sharedPixels[band[i]];
					depthPixels[i] = sharedDepths[band[i]];
				}
				most = pixels[i].r > most ? pixels[i].r : most;
			}
			mapMostSnow = most / 255f;
			Upload();
		}

		// Each band's shared ground, encoded once a draw.
		private readonly Color32[] sharedPixels = new Color32[GroundCover.BandCount];
		private readonly byte[] sharedDepths = new byte[GroundCover.BandCount];

		private void DrawHeld()
		{
			Color32 pixel = Encode(heldCover);
			byte depthByte = EncodeDepth(heldDepth, depthMax);
			mapMostSnow = Mathf.Clamp01(heldCover.Snow);
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
			band = new byte[n];
			System.Array.Fill(band, (byte)GroundCover.BandReach);
			bandsSettings = null;
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
			bool line = lineOn && lineShare > 0f;
			bool anywhere = (valid && mapMostSnow > 0f) || (line && TerrainTop() > lineStart);
			Shader.SetGlobalVector(SnowLineId, new Vector4(lineStart, lineWhole, anywhere ? 1f : 0f, line ? lineShare : 0f));
		}

		/// <summary>The highest ground of the map's scene, world y (its terrains' tops): whether any of it reaches the snow line.</summary>
		/// <remarks>Looked over again every <see cref="TerrainTopSeconds"/> (terrains stream in); +∞ while none is found, so the gate stays open rather than shut.</remarks>
		private float TerrainTop()
		{
			float now = Time.realtimeSinceStartup;
			if (topScene != scene || now >= terrainTopAt)
			{
				topScene = scene;
				terrainTopAt = now + TerrainTopSeconds;
				terrainTop = float.PositiveInfinity;
				float top = float.NegativeInfinity;
				Terrain.GetActiveTerrains(terrains);
				foreach (Terrain terrain in terrains)
				{
					if (terrain != null && terrain.terrainData != null && terrain.gameObject.scene == scene)
					{
						top = Mathf.Max(top, terrain.GetPosition().y + terrain.terrainData.bounds.max.y);
					}
				}
				terrains.Clear();
				if (!float.IsNegativeInfinity(top))
				{
					terrainTop = top;
				}
			}
			return terrainTop;
		}

		/// <summary>Real seconds between looks at the scene's highest ground.</summary>
		private const float TerrainTopSeconds = 2f;
		private float terrainTopAt;
		private readonly List<Terrain> terrains = new List<Terrain>();
	}
}
