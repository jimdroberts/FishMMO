using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Puts a scene's baked prop collision into physics only where something can touch it: within
	/// <see cref="ServerRadius"/> of every character on the server, and within <see cref="ClientRadius"/> of the
	/// camera on the client. A chunk (<see cref="CellMetres"/> square) at a time, as one mesh: every collidable prop
	/// in it merged and cooked off the main thread, then handed to one static collider.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why stream.</b> A generated scene has tens of thousands of trees and rocks. Baked into chunk meshes on disk
	/// their collision is every prop's geometry over again, tens of megabytes per scene, all of it in physics all the
	/// time, on a server that hosts many scenes mostly far from any player. Streamed, a prop kind's collision mesh is
	/// shared, a prop is an instance record, and physics holds only the chunks near someone.
	/// </para>
	/// <para>
	/// <b>One collider per chunk.</b> A chunk's props become a single mesh (one per collision layer present), so
	/// physics holds one static actor per chunk rather than one per rock: the merge (<see cref="MergeJob"/>) and the
	/// cooking (<see cref="BakeJob"/>, <see cref="Physics.BakeMesh(int, bool)"/>) both run as jobs, and the main thread
	/// only applies the merged data and assigns the cooked mesh.
	/// </para>
	/// <para>
	/// <b>Radii.</b> The client's covers its view distance (100 m) and a margin, so effects, projectiles and the
	/// camera meet every rock in sight. The server's covers each character's own reach, players and NPCs alike, so an
	/// NPC in view never walks through a rock the client draws. A character that appears has the chunks round it
	/// built at once (<see cref="AddFocus"/>), before its first step; farther chunks follow, nearest first, at most
	/// <see cref="ChunksPerTick"/> started per tick.
	/// </para>
	/// <para>
	/// <b>Held by cell, counted.</b> Which chunks are wanted is kept by <see cref="PropChunkLedger"/>: each focus holds
	/// the chunks round the cell it stands in, recomputed only when it crosses into another cell, and a chunk stays while
	/// any focus holds it. Holding by cell is a superset of the chunks round the focus's exact position, and a chunk is
	/// let go only <see cref="ReleaseMarginMetres"/> beyond the radius, so collision is never in fewer places than a
	/// per-tick recompute round every exact position would put it.
	/// </para>
	/// <para>
	/// <b>Each collider in its own scene.</b> Colliders are created in the prop's scene, so a server running each
	/// world scene in its own physics scene finds them in the right one.
	/// </para>
	/// </remarks>
	public static class PropColliderStreamer
	{
		/// <summary>Chunk side, metres: collision comes and goes a chunk at a time.</summary>
		public const float CellMetres = 32f;

		/// <summary>How far round each character the server keeps prop collision, metres.</summary>
		public static float ServerRadius = 96f;

		/// <summary>How far round the camera the client keeps prop collision, metres: the 100 m view distance and a margin.</summary>
		public static float ClientRadius = 128f;

		/// <summary>How far round a character appearing its chunks are built at once, metres.</summary>
		public static float ImmediateRadius = 40f;

		/// <summary>
		/// How far beyond its radius a focus keeps a chunk it already holds, metres: one chunk, so pacing over a cell edge
		/// does not empty and rebuild the far rim. Only ever keeps more collision.
		/// </summary>
		public static float ReleaseMarginMetres = CellMetres;

		/// <summary>The most chunks started per tick once the chunks right round every focus are built.</summary>
		public const int ChunksPerTick = 24;

		/// <summary>Seconds between updates of which chunks are wanted.</summary>
		public const float TickSeconds = 0.1f;

		private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("PropColliderStreamer.Update");
		private static readonly ProfilerMarker AdvanceMarker = new ProfilerMarker("PropColliderStreamer.AdvanceBuilding");
		private static readonly ProfilerMarker TickMarker = new ProfilerMarker("PropColliderStreamer.Tick");
		private static readonly ProfilerMarker PlaceMarker = new ProfilerMarker("PropColliderStreamer.PlaceFoci");
		private static readonly ProfilerMarker ImmediateMarker = new ProfilerMarker("PropColliderStreamer.Immediate");
		private static readonly ProfilerMarker BudgetMarker = new ProfilerMarker("PropColliderStreamer.Budget");
		private static readonly ProfilerMarker StartMarker = new ProfilerMarker("PropColliderStreamer.StartChunk");

		private sealed class Source
		{
			public ScenePropColliders Owner;
			public Scene Scene;
			public int SceneHandle;
			public Transform Root;
			/// <summary>Per chunk, its collidable props: (set, instance).</summary>
			public readonly Dictionary<long, List<(int set, int index)>> Cells = new Dictionary<long, List<(int, int)>>();
			/// <summary>The distinct collision meshes, read once for the merge jobs.</summary>
			public readonly List<Mesh> Meshes = new List<Mesh>();
			public Mesh.MeshDataArray MeshData;
			public bool HasMeshData;
			/// <summary>Per set and prototype, its mesh's index in <see cref="Meshes"/> (-1 for none) and its layer.</summary>
			public int[][] MeshOf;
			public int[][] LayerOf;
			/// <summary>Which chunks the foci want.</summary>
			public PropChunkLedger Ledger;
			/// <summary>Every wanted chunk, building or built.</summary>
			public readonly Dictionary<long, Chunk> Chunks = new Dictionary<long, Chunk>();
			/// <summary>Wanted chunks not started yet, with the distance that orders them (nearest first).</summary>
			public readonly Dictionary<long, float> Pending = new Dictionary<long, float>();
			/// <summary>Started chunks that are not live yet: the only ones the per-frame advance looks at.</summary>
			public readonly List<Chunk> Building = new List<Chunk>();
			/// <summary>Idle colliders, mesh cleared.</summary>
			public readonly Stack<MeshCollider> Idle = new Stack<MeshCollider>();
		}

		private sealed class Chunk
		{
			public long Cell;
			public int Props;
			public bool Live;
			public readonly List<Build> Builds = new List<Build>();
		}

		/// <summary>One chunk's mesh for one collision layer, on its way to physics.</summary>
		private sealed class Build
		{
			public int Layer;
			public NativeArray<int> PieceMesh;
			public NativeArray<Matrix4x4> PieceMatrix;
			public Mesh.MeshDataArray Output;
			public JobHandle Merge;
			public bool Merging;
			public Mesh Mesh;
			public JobHandle Bake;
			public bool Baking;
			public MeshCollider Collider;
		}

		/// <summary>A point collision is kept round: a character's transform, or the client's camera.</summary>
		private sealed class Focus
		{
			public Transform Transform;
			public float Radius;
			/// <summary>The camera: wanted in every scene, not only its own.</summary>
			public bool AllScenes;
			/// <summary>Removed since the last tick; its holds are let go on the tick, as the old full recompute did.</summary>
			public bool Removed;
			public int SceneHandle;
			public Vector3 Position;
			/// <summary>Every chunk within the immediate radius of its cell is live, so the immediate pass can skip it.</summary>
			public bool Settled;
			public readonly List<(Source source, PropChunkLedger.Hold hold)> Holds = new List<(Source, PropChunkLedger.Hold)>();
		}

		private static readonly List<Source> sources = new List<Source>();
		private static readonly List<Focus> foci = new List<Focus>();
		private static readonly Dictionary<Transform, Focus> fociByTransform = new Dictionary<Transform, Focus>();
		private static Focus cameraFocus;
		private static readonly List<(long cell, float distance)> pointScratch = new List<(long, float)>();
		private static readonly List<(long cell, float distance)> pendingScratch = new List<(long, float)>();
		private static readonly List<long> cellScratch = new List<long>();
		private static readonly System.Comparison<(long cell, float distance)> NearestFirst = (a, b) => a.distance.CompareTo(b.distance);

		// Pools: a chunk's merge inputs, its bookkeeping and its per-layer grouping are reused, not reallocated per chunk.
		private static readonly Stack<Chunk> chunkPool = new Stack<Chunk>();
		private static readonly Stack<Build> buildPool = new Stack<Build>();
		private static readonly Stack<List<(int mesh, Matrix4x4 matrix)>> pieceListPool = new Stack<List<(int, Matrix4x4)>>();
		private static readonly Dictionary<int, List<(int mesh, Matrix4x4 matrix)>> byLayer = new Dictionary<int, List<(int, Matrix4x4)>>();
		private static readonly List<int> layerOrder = new List<int>();
		private static readonly NativeArrayPool<int> meshIndexPool = new NativeArrayPool<int>();
		private static readonly NativeArrayPool<Matrix4x4> matrixPool = new NativeArrayPool<Matrix4x4>();

		private static float nextTick;
		private static Runner runner;

		/// <summary>Props whose collision is in physics, across every scene.</summary>
		public static int ActiveCount { get; private set; }

		/// <summary>Chunk colliders in physics, across every scene.</summary>
		public static int ActiveChunks { get; private set; }

		/// <summary>
		/// Every focus being kept, with where it stands, its radius, scene and how many chunks it holds: for a test or a
		/// probe asking why collision is still somewhere (another character, or the camera, holding it).
		/// </summary>
		public static string DescribeFoci()
		{
			var text = new System.Text.StringBuilder();
			void Describe(Focus focus, string kind)
			{
				int held = 0;
				foreach ((Source _, PropChunkLedger.Hold hold) in focus.Holds)
				{
					held += hold.Cells.Count;
				}
				string name = focus.Transform != null ? focus.Transform.name : "(destroyed)";
				text.Append($"{kind} '{name}' at {focus.Position} r {focus.Radius} scene {focus.SceneHandle}{(focus.Removed ? " removed" : "")}: {held} chunks held\n");
			}
			foreach (Focus focus in foci)
			{
				Describe(focus, "focus");
			}
			if (cameraFocus != null)
			{
				Describe(cameraFocus, "camera");
			}
			return text.ToString();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			sources.Clear();
			foci.Clear();
			fociByTransform.Clear();
			cameraFocus = null;
			chunkPool.Clear();
			buildPool.Clear();
			pieceListPool.Clear();
			byLayer.Clear();
			layerOrder.Clear();
			meshIndexPool.Dispose();
			matrixPool.Dispose();
			ActiveCount = 0;
			ActiveChunks = 0;
			nextTick = 0f;
			runner = null;
		}

		// ── Sources and focus points ──────────────────────────────────

		/// <summary>A scene's prop collision came into play (its <see cref="ScenePropColliders"/> was enabled).</summary>
		public static void Add(ScenePropColliders owner)
		{
			if (!Application.isPlaying || owner == null || sources.Exists(s => s.Owner == owner))
			{
				return;
			}
			var source = new Source { Owner = owner, Scene = owner.gameObject.scene };
			source.SceneHandle = source.Scene.handle;
			var meshIndex = new Dictionary<Mesh, int>();
			source.MeshOf = new int[owner.Sets.Count][];
			source.LayerOf = new int[owner.Sets.Count][];
			for (int s = 0; s < owner.Sets.Count; s++)
			{
				ScenePropCollisionSet set = owner.Sets[s];
				if (set == null)
				{
					continue;
				}
				source.MeshOf[s] = new int[set.Prototypes.Length];
				source.LayerOf[s] = new int[set.Prototypes.Length];
				for (int p = 0; p < set.Prototypes.Length; p++)
				{
					Mesh mesh = set.Prototypes[p].Mesh;
					int index = -1;
					// The merge reads the meshes on worker threads: only readable ones can be read there.
					if (mesh != null && mesh.isReadable && !meshIndex.TryGetValue(mesh, out index))
					{
						meshIndex[mesh] = index = source.Meshes.Count;
						source.Meshes.Add(mesh);
					}
					else if (mesh != null && !mesh.isReadable)
					{
						Debug.LogWarning($"[Props] '{mesh.name}' is not readable, so {set.Source} prototype {p} has no collision.");
					}
					source.MeshOf[s][p] = index;
					source.LayerOf[s][p] = set.Prototypes[p].Layer;
				}
				for (int i = 0; i < set.Instances.Length; i++)
				{
					int prototype = set.Instances[i].Prototype;
					if (prototype < 0 || prototype >= set.Prototypes.Length || source.MeshOf[s][prototype] < 0)
					{
						continue;
					}
					long cell = CellOf(set.Instances[i].Position);
					if (!source.Cells.TryGetValue(cell, out List<(int, int)> list))
					{
						source.Cells[cell] = list = new List<(int, int)>();
					}
					list.Add((s, i));
				}
			}
			if (source.Meshes.Count > 0)
			{
				source.MeshData = Mesh.AcquireReadOnlyMeshData(source.Meshes);
				source.HasMeshData = true;
			}
			source.Ledger = new PropChunkLedger(CellMetres, new HashSet<long>(source.Cells.Keys));
			var root = new GameObject("Streamed Prop Colliders") { hideFlags = HideFlags.DontSave };
			SceneManager.MoveGameObjectToScene(root, source.Scene);
			source.Root = root.transform;
			sources.Add(source);
			EnsureRunner();

			// Every focus now in this scene (and the camera) takes its hold here, and the chunks right round it are built now.
			for (int f = 0; f < foci.Count; f++)
			{
				Focus focus = foci[f];
				if (focus.Removed || focus.Transform == null)
				{
					continue;
				}
				int handle = focus.Transform.gameObject.scene.handle;
				if (handle != focus.SceneHandle)
				{
					// Moved scene since the last tick: it holds in its new scene's sources, this one among them.
					ReattachScene(focus, handle);
				}
				else if (handle == source.SceneHandle)
				{
					focus.Holds.Add((source, new PropChunkLedger.Hold()));
					focus.Settled = false;
				}
				else
				{
					continue;
				}
				PlaceHolds(focus);
			}
			if (cameraFocus != null)
			{
				cameraFocus.Holds.Add((source, new PropChunkLedger.Hold()));
				cameraFocus.Settled = false;
				if (cameraFocus.Transform != null)
				{
					PlaceHolds(cameraFocus);
				}
			}
			for (int s = 0; s < sources.Count; s++)
			{
				Settle(sources[s]);
			}
			for (int f = 0; f < foci.Count; f++)
			{
				if (!foci[f].Removed && foci[f].Transform != null && !foci[f].Settled)
				{
					Immediate(foci[f]);
				}
			}
			if (cameraFocus != null && cameraFocus.Transform != null && !cameraFocus.Settled)
			{
				Immediate(cameraFocus);
			}
		}

		/// <summary>A scene's prop collision left play: every job is finished, every chunk and pool of it destroyed.</summary>
		public static void Remove(ScenePropColliders owner)
		{
			int at = sources.FindIndex(s => s.Owner == owner);
			if (at < 0)
			{
				return;
			}
			Source source = sources[at];
			foreach (long cell in new List<long>(source.Chunks.Keys))
			{
				Empty(source, cell);
			}
			if (source.HasMeshData)
			{
				source.MeshData.Dispose();
				source.HasMeshData = false;
			}
			if (source.Root != null)
			{
				Object.Destroy(source.Root.gameObject);
			}
			for (int f = 0; f < foci.Count; f++)
			{
				DetachSource(foci[f], source);
			}
			if (cameraFocus != null)
			{
				DetachSource(cameraFocus, source);
			}
			sources.RemoveAt(at);
			if (sources.Count == 0)
			{
				meshIndexPool.Dispose();
				matrixPool.Dispose();
			}
		}

		/// <summary>Keeps prop collision within <paramref name="radius"/> of <paramref name="transform"/>; the chunks right round it are built now.</summary>
		public static void AddFocus(Transform transform, float radius)
		{
			if (!Application.isPlaying || transform == null)
			{
				return;
			}
			if (fociByTransform.TryGetValue(transform, out Focus focus))
			{
				if (!focus.Removed)
				{
					return;
				}
				// Removed and added back before the tick let it go: its holds are taken afresh (sources may have come since),
				// and what it held stays wanted, because the ledger empties nothing that is held again before it settles.
				focus.Removed = false;
				focus.Radius = radius;
				ReattachScene(focus, transform.gameObject.scene.handle);
			}
			else
			{
				focus = new Focus { Transform = transform, Radius = radius, SceneHandle = transform.gameObject.scene.handle };
				foci.Add(focus);
				fociByTransform[transform] = focus;
				AttachSources(focus);
			}
			EnsureRunner();
			// Only this focus: the others' chunks are settled by their own adds and ticks.
			focus.Settled = false;
			PlaceHolds(focus);
			for (int s = 0; s < sources.Count; s++)
			{
				Settle(sources[s]);
			}
			Immediate(focus);
		}

		/// <summary>Stops keeping collision for <paramref name="transform"/>.</summary>
		public static void RemoveFocus(Transform transform)
		{
			if (transform is object && fociByTransform.TryGetValue(transform, out Focus focus))
			{
				// Let go on the next tick, as the full recompute did, so a remove and re-add in one frame rebuilds nothing.
				focus.Removed = true;
			}
		}

		private static void AttachSources(Focus focus)
		{
			for (int s = 0; s < sources.Count; s++)
			{
				if (focus.AllScenes || sources[s].SceneHandle == focus.SceneHandle)
				{
					focus.Holds.Add((sources[s], new PropChunkLedger.Hold()));
				}
			}
		}

		private static void DetachSource(Focus focus, Source source)
		{
			for (int i = focus.Holds.Count - 1; i >= 0; i--)
			{
				if (focus.Holds[i].source == source)
				{
					focus.Holds.RemoveAt(i);
				}
			}
		}

		private static void DropHolds(Focus focus)
		{
			for (int i = 0; i < focus.Holds.Count; i++)
			{
				focus.Holds[i].source.Ledger.Drop(focus.Holds[i].hold);
			}
			focus.Holds.Clear();
		}

		/// <summary>A focus moved to another scene: it lets go of the old scene's chunks and holds in the new one's.</summary>
		private static void ReattachScene(Focus focus, int handle)
		{
			DropHolds(focus);
			focus.SceneHandle = handle;
			focus.Settled = false;
			AttachSources(focus);
		}

		/// <summary>Places each of a focus's holds where it stands now; any that moved cell leave it unsettled.</summary>
		private static void PlaceHolds(Focus focus)
		{
			focus.Position = focus.Transform.position;
			for (int i = 0; i < focus.Holds.Count; i++)
			{
				(Source source, PropChunkLedger.Hold hold) = focus.Holds[i];
				if (source.Owner != null && source.Ledger.Place(hold, focus.Position, focus.Radius, ReleaseMarginMetres))
				{
					focus.Settled = false;
				}
			}
		}

		// ── Streaming ─────────────────────────────────────────────────

		private static void Update()
		{
			using (UpdateMarker.Auto())
			{
				// Every frame: chunks whose jobs finished move on. Only the building ones are looked at.
				using (AdvanceMarker.Auto())
				{
					for (int s = 0; s < sources.Count; s++)
					{
						List<Chunk> building = sources[s].Building;
						for (int i = building.Count - 1; i >= 0; i--)
						{
							Chunk chunk = building[i];
							if (!chunk.Live)
							{
								Advance(sources[s], chunk, false);
							}
							if (chunk.Live)
							{
								building[i] = building[building.Count - 1];
								building.RemoveAt(building.Count - 1);
							}
						}
					}
				}
				if (Time.unscaledTime < nextTick)
				{
					return;
				}
				nextTick = Time.unscaledTime + TickSeconds;
				using (TickMarker.Auto())
				{
					Tick();
				}
			}
		}

		/// <summary>
		/// Brings every source's chunks in line with the focus points: foci gone are let go, foci that crossed a cell are
		/// placed again, chunks no focus holds are emptied, the chunks right round every unsettled focus are built and
		/// finished now, then wanted chunks are started nearest first within the budget.
		/// </summary>
		private static void Tick()
		{
			using (PlaceMarker.Auto())
			{
				for (int f = foci.Count - 1; f >= 0; f--)
				{
					Focus focus = foci[f];
					if (focus.Removed || focus.Transform == null)
					{
						DropHolds(focus);
						fociByTransform.Remove(focus.Transform);
						foci.RemoveAt(f);
						continue;
					}
					int handle = focus.Transform.gameObject.scene.handle;
					if (handle != focus.SceneHandle)
					{
						ReattachScene(focus, handle);
					}
					PlaceHolds(focus);
				}
				TrackCamera();
				for (int s = 0; s < sources.Count; s++)
				{
					Settle(sources[s]);
				}
			}

			// Right round every focus first, all of it, finished now; then outward, nearest first, within the budget.
			int started = 0;
			using (ImmediateMarker.Auto())
			{
				for (int f = 0; f < foci.Count; f++)
				{
					if (!foci[f].Settled)
					{
						started += Immediate(foci[f]);
					}
				}
				if (cameraFocus != null && !cameraFocus.Settled)
				{
					started += Immediate(cameraFocus);
				}
			}
			using (BudgetMarker.Auto())
			{
				StartPending(Mathf.Max(0, ChunksPerTick - started));
			}
		}

		/// <summary>The client's camera as a focus of its own: in every scene, at <see cref="ClientRadius"/>.</summary>
		private static void TrackCamera()
		{
#if !UNITY_SERVER
			Camera camera = Camera.main;
			Transform eye = camera != null ? camera.transform : null;
			if (cameraFocus != null && (eye == null || !ReferenceEquals(cameraFocus.Transform, eye)))
			{
				DropHolds(cameraFocus);
				cameraFocus = null;
			}
			if (eye == null)
			{
				return;
			}
			if (cameraFocus == null)
			{
				cameraFocus = new Focus { Transform = eye, AllScenes = true };
				AttachSources(cameraFocus);
			}
			cameraFocus.Radius = ClientRadius;
			PlaceHolds(cameraFocus);
#endif
		}

		/// <summary>
		/// Applies what the ledger recorded since last time: chunks no focus holds any more are emptied, newly held ones
		/// without a chunk wait to be started, nearest first.
		/// </summary>
		private static void Settle(Source source)
		{
			PropChunkLedger ledger = source.Ledger;
			for (int i = 0; i < ledger.Released.Count; i++)
			{
				long cell = ledger.Released[i];
				if (!ledger.IsWanted(cell))
				{
					source.Pending.Remove(cell);
					Empty(source, cell);
				}
			}
			for (int i = 0; i < ledger.Acquired.Count; i++)
			{
				(long cell, float distance) = ledger.Acquired[i];
				if (!ledger.IsWanted(cell) || source.Chunks.ContainsKey(cell))
				{
					continue;
				}
				if (!source.Pending.TryGetValue(cell, out float known) || distance < known)
				{
					source.Pending[cell] = distance;
				}
			}
			ledger.Released.Clear();
			ledger.Acquired.Clear();
		}

		/// <summary>
		/// Builds and finishes, now, every chunk with props within the immediate radius of where a focus stands (the same
		/// per-point rule as before), then marks it settled once every chunk within that radius of its whole cell is live:
		/// it holds those until it leaves the cell, so nothing nearer can be missing until then. Returns the chunks started.
		/// </summary>
		private static int Immediate(Focus focus)
		{
			int started = 0;
			if (focus.Transform == null)
			{
				return 0;
			}
			float reach = Mathf.Min(ImmediateRadius, focus.Radius);
			PropChunkLedger.PointCells(focus.Position, reach, CellMetres, pointScratch);
			bool settled = true;
			for (int h = 0; h < focus.Holds.Count; h++)
			{
				(Source source, PropChunkLedger.Hold hold) = focus.Holds[h];
				if (source.Owner == null)
				{
					continue;
				}
				for (int i = 0; i < pointScratch.Count; i++)
				{
					long cell = pointScratch[i].cell;
					if (!source.Ledger.PropCells.Contains(cell))
					{
						continue;
					}
					if (!source.Chunks.TryGetValue(cell, out Chunk chunk))
					{
						chunk = Start(source, cell);
						if (chunk == null)
						{
							continue;
						}
						started++;
					}
					if (!chunk.Live)
					{
						// Chunks started by an earlier budgeted pass that a character now stands in: finished now as well.
						Advance(source, chunk, true);
					}
				}
				if (hold.Placed)
				{
					source.Ledger.CellsNear(hold.X, hold.Z, reach, cellScratch);
					for (int i = 0; i < cellScratch.Count && settled; i++)
					{
						settled = source.Chunks.TryGetValue(cellScratch[i], out Chunk chunk) && chunk.Live;
					}
				}
				else
				{
					settled = false;
				}
			}
			focus.Settled = settled;
			return started;
		}

		/// <summary>Starts wanted chunks, nearest first, at most <paramref name="budget"/> across every source.</summary>
		private static void StartPending(int budget)
		{
			int started = 0;
			for (int s = 0; s < sources.Count && started < budget; s++)
			{
				Source source = sources[s];
				if (source.Owner == null || source.Pending.Count == 0)
				{
					continue;
				}
				pendingScratch.Clear();
				foreach (KeyValuePair<long, float> pending in source.Pending)
				{
					pendingScratch.Add((pending.Key, pending.Value));
				}
				pendingScratch.Sort(NearestFirst);
				for (int i = 0; i < pendingScratch.Count && started < budget; i++)
				{
					long cell = pendingScratch[i].cell;
					source.Pending.Remove(cell);
					if (Start(source, cell) != null)
					{
						started++;
					}
				}
			}
		}

		/// <summary>Starts merging a chunk's props, one job per collision layer present. Null when it has nothing to merge.</summary>
		private static Chunk Start(Source source, long cell)
		{
			if (!source.HasMeshData || source.Chunks.ContainsKey(cell) || !source.Cells.TryGetValue(cell, out List<(int set, int index)> props))
			{
				return null;
			}
			using (StartMarker.Auto())
			{
				source.Pending.Remove(cell);
				// Grouped by layer in first-seen order, each layer's pieces in prop order: the merged meshes are as before.
				layerOrder.Clear();
				foreach ((int s, int i) in props)
				{
					ScenePropCollisionSet.Instance instance = source.Owner.Sets[s].Instances[i];
					int mesh = source.MeshOf[s][instance.Prototype];
					int layer = source.LayerOf[s][instance.Prototype];
					if (!byLayer.TryGetValue(layer, out List<(int, Matrix4x4)> pieces))
					{
						pieces = pieceListPool.Count > 0 ? pieceListPool.Pop() : new List<(int, Matrix4x4)>();
						byLayer[layer] = pieces;
						layerOrder.Add(layer);
					}
					pieces.Add((mesh, Matrix4x4.TRS(instance.Position, instance.Rotation, instance.Scale)));
				}
				Chunk chunk = chunkPool.Count > 0 ? chunkPool.Pop() : new Chunk();
				chunk.Cell = cell;
				chunk.Props = props.Count;
				chunk.Live = false;
				for (int l = 0; l < layerOrder.Count; l++)
				{
					List<(int mesh, Matrix4x4 matrix)> pieces = byLayer[layerOrder[l]];
					Build build = buildPool.Count > 0 ? buildPool.Pop() : new Build();
					build.Layer = layerOrder[l];
					build.PieceMesh = meshIndexPool.Take(pieces.Count);
					build.PieceMatrix = matrixPool.Take(pieces.Count);
					build.Output = Mesh.AllocateWritableMeshData(1);
					for (int k = 0; k < pieces.Count; k++)
					{
						build.PieceMesh[k] = pieces[k].mesh;
						build.PieceMatrix[k] = pieces[k].matrix;
					}
					build.Merge = new MergeJob
					{
						Sources = source.MeshData,
						PieceMesh = build.PieceMesh,
						PieceMatrix = build.PieceMatrix,
						Count = pieces.Count,
						Output = build.Output,
					}.Schedule();
					build.Merging = true;
					chunk.Builds.Add(build);
					pieces.Clear();
					pieceListPool.Push(pieces);
				}
				byLayer.Clear();
				source.Chunks[cell] = chunk;
				source.Building.Add(chunk);
				JobHandle.ScheduleBatchedJobs();
				return chunk;
			}
		}

		/// <summary>
		/// Moves a chunk's builds on as far as their jobs allow (all the way, waiting on them, when
		/// <paramref name="force"/>): merged data applied to a mesh and its cooking started, then the cooked mesh handed
		/// to a collider. The chunk is live once every build has its collider.
		/// </summary>
		private static void Advance(Source source, Chunk chunk, bool force)
		{
			bool all = true;
			foreach (Build build in chunk.Builds)
			{
				if (build.Merging && (force || build.Merge.IsCompleted))
				{
					build.Merge.Complete();
					build.Merging = false;
					ReturnPieces(build);
					// A fresh mesh per chunk, not a pooled one: a reused mesh would have to give up its earlier cooking to the
					// new data, which a stale cook would not show until something walked through the wrong collision.
					build.Mesh = new Mesh { name = "Prop Chunk Collision", hideFlags = HideFlags.DontSave };
					Mesh.ApplyAndDisposeWritableMeshData(build.Output, build.Mesh, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
					build.Mesh.RecalculateBounds();
					build.Bake = new BakeJob { MeshId = build.Mesh.GetInstanceID() }.Schedule();
					build.Baking = true;
					JobHandle.ScheduleBatchedJobs();
				}
				if (build.Baking && (force || build.Bake.IsCompleted))
				{
					build.Bake.Complete();
					build.Baking = false;
					build.Collider = Take(source, build.Layer);
					// Cooked already, with the collider's own options: assigning only links it.
					build.Collider.sharedMesh = build.Mesh;
					build.Collider.gameObject.SetActive(true);
				}
				all &= build.Collider != null;
			}
			if (all && !chunk.Live)
			{
				chunk.Live = true;
				ActiveCount += chunk.Props;
				ActiveChunks += chunk.Builds.Count;
			}
		}

		/// <summary>A build's merge inputs, back to their pools once the merge that read them is complete.</summary>
		private static void ReturnPieces(Build build)
		{
			meshIndexPool.Return(build.PieceMesh);
			matrixPool.Return(build.PieceMatrix);
			build.PieceMesh = default;
			build.PieceMatrix = default;
		}

		/// <summary>Takes a chunk out of physics: its jobs finished, its meshes destroyed, its colliders back in the pool.</summary>
		private static void Empty(Source source, long cell)
		{
			if (!source.Chunks.TryGetValue(cell, out Chunk chunk))
			{
				return;
			}
			foreach (Build build in chunk.Builds)
			{
				if (build.Merging)
				{
					build.Merge.Complete();
					ReturnPieces(build);
					build.Output.Dispose();
					build.Merging = false;
				}
				if (build.Baking)
				{
					build.Bake.Complete();
					build.Baking = false;
				}
				if (build.Collider != null)
				{
					build.Collider.sharedMesh = null;
					build.Collider.gameObject.SetActive(false);
					source.Idle.Push(build.Collider);
					build.Collider = null;
				}
				if (build.Mesh != null)
				{
					Object.Destroy(build.Mesh);
					build.Mesh = null;
				}
				build.Merge = default;
				build.Bake = default;
				buildPool.Push(build);
			}
			if (chunk.Live)
			{
				ActiveCount -= chunk.Props;
				ActiveChunks -= chunk.Builds.Count;
			}
			// A chunk finished by force this tick can still be in the building list; it must not be advanced once pooled.
			source.Building.Remove(chunk);
			source.Chunks.Remove(cell);
			chunk.Builds.Clear();
			chunk.Live = false;
			chunkPool.Push(chunk);
		}

		private static MeshCollider Take(Source source, int layer)
		{
			MeshCollider collider;
			if (source.Idle.Count > 0)
			{
				collider = source.Idle.Pop();
			}
			else
			{
				var go = new GameObject("Prop Chunk Collider") { hideFlags = HideFlags.DontSave };
				go.SetActive(false);
				go.transform.SetParent(source.Root, false);
				collider = go.AddComponent<MeshCollider>();
				collider.convex = false;
			}
			collider.gameObject.layer = layer;
			return collider;
		}

		private static long CellOf(Vector3 position) => Key(Mathf.FloorToInt(position.x / CellMetres), Mathf.FloorToInt(position.z / CellMetres));

		private static long Key(int x, int z) => PropChunkLedger.Key(x, z);

		// ── Pools ─────────────────────────────────────────────────────

		/// <summary>
		/// Persistent native arrays by power-of-two capacity, so starting a chunk does not allocate its merge inputs. An
		/// array goes back only after the merge reading it is complete; all are freed when the last source leaves.
		/// </summary>
		private sealed class NativeArrayPool<T> where T : struct
		{
			private readonly Stack<NativeArray<T>>[] free = new Stack<NativeArray<T>>[32];

			public NativeArray<T> Take(int count)
			{
				int bucket = Bucket(count);
				Stack<NativeArray<T>> stack = free[bucket];
				if (stack != null && stack.Count > 0)
				{
					return stack.Pop();
				}
				return new NativeArray<T>(1 << bucket, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			}

			public void Return(NativeArray<T> array)
			{
				if (!array.IsCreated)
				{
					return;
				}
				int bucket = Bucket(array.Length);
				(free[bucket] ??= new Stack<NativeArray<T>>()).Push(array);
			}

			public void Dispose()
			{
				foreach (Stack<NativeArray<T>> stack in free)
				{
					while (stack != null && stack.Count > 0)
					{
						NativeArray<T> array = stack.Pop();
						if (array.IsCreated)
						{
							array.Dispose();
						}
					}
				}
			}

			private static int Bucket(int count)
			{
				int bucket = 0;
				while ((1 << bucket) < count && bucket < 30)
				{
					bucket++;
				}
				return bucket;
			}
		}

		// ── Jobs ──────────────────────────────────────────────────────

		/// <summary>
		/// One chunk's collision: every piece's mesh, transformed into the chunk's (world) space, as one positions-only mesh.
		/// Not Burst-compiled: Burst's transform, even with strict float semantics, differed from the managed one in the last
		/// bit of a vertex, and server and clients must stand on the same collision. It runs on a worker either way.
		/// </summary>
		internal struct MergeJob : IJob
		{
			[ReadOnly] public Mesh.MeshDataArray Sources;
			[ReadOnly] public NativeArray<int> PieceMesh;
			[ReadOnly] public NativeArray<Matrix4x4> PieceMatrix;
			/// <summary>Pieces in use: the pooled arrays can be longer.</summary>
			public int Count;
			public Mesh.MeshDataArray Output;

			public void Execute()
			{
				int vertexCount = 0, indexCount = 0;
				for (int p = 0; p < Count; p++)
				{
					Mesh.MeshData source = Sources[PieceMesh[p]];
					vertexCount += source.vertexCount;
					for (int s = 0; s < source.subMeshCount; s++)
					{
						indexCount += source.GetSubMesh(s).indexCount;
					}
				}
				Mesh.MeshData output = Output[0];
				var layout = new NativeArray<VertexAttributeDescriptor>(1, Allocator.Temp);
				layout[0] = new VertexAttributeDescriptor(VertexAttribute.Position);
				output.SetVertexBufferParams(vertexCount, layout);
				layout.Dispose();
				output.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
				NativeArray<Vector3> positions = output.GetVertexData<Vector3>();
				NativeArray<int> indices = output.GetIndexData<int>();
				int vertexAt = 0, indexAt = 0;
				for (int p = 0; p < Count; p++)
				{
					Mesh.MeshData source = Sources[PieceMesh[p]];
					Matrix4x4 matrix = PieceMatrix[p];
					var local = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
					source.GetVertices(local);
					for (int v = 0; v < local.Length; v++)
					{
						positions[vertexAt + v] = matrix.MultiplyPoint3x4(local[v]);
					}
					local.Dispose();
					for (int s = 0; s < source.subMeshCount; s++)
					{
						int count = source.GetSubMesh(s).indexCount;
						var sub = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
						source.GetIndices(sub, s);
						for (int i = 0; i < count; i++)
						{
							indices[indexAt + i] = sub[i] + vertexAt;
						}
						sub.Dispose();
						indexAt += count;
					}
					vertexAt += source.vertexCount;
				}
				output.subMeshCount = 1;
				output.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
			}
		}

		/// <summary>Cooks a chunk's mesh for a (non-convex) mesh collider, off the main thread.</summary>
		private struct BakeJob : IJob
		{
			public int MeshId;

			public void Execute() => Physics.BakeMesh(MeshId, false);
		}

		// ── The tick ──────────────────────────────────────────────────

		private static void EnsureRunner()
		{
			if (runner != null)
			{
				return;
			}
			var go = new GameObject("Prop Collider Streamer") { hideFlags = HideFlags.HideAndDontSave };
			Object.DontDestroyOnLoad(go);
			runner = go.AddComponent<Runner>();
		}

		private sealed class Runner : MonoBehaviour
		{
			private void Update() => PropColliderStreamer.Update();
		}
	}
}
