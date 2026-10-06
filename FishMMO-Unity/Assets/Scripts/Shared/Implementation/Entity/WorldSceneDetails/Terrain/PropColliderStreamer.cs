using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
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

		/// <summary>The most chunks started per tick once the chunks right round every focus are built.</summary>
		public const int ChunksPerTick = 24;

		/// <summary>Seconds between updates of which chunks are wanted.</summary>
		public const float TickSeconds = 0.1f;

		private sealed class Source
		{
			public ScenePropColliders Owner;
			public Scene Scene;
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
			/// <summary>Every wanted chunk, building or built.</summary>
			public readonly Dictionary<long, Chunk> Chunks = new Dictionary<long, Chunk>();
			/// <summary>Idle colliders, mesh cleared.</summary>
			public readonly Stack<MeshCollider> Idle = new Stack<MeshCollider>();
		}

		private sealed class Chunk
		{
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

		private static readonly List<Source> sources = new List<Source>();
		private static readonly List<(Transform transform, float radius)> foci = new List<(Transform, float)>();
		private static readonly HashSet<long> wanted = new HashSet<long>();
		private static readonly List<(long cell, float distance)> missing = new List<(long, float)>();
		private static readonly List<long> scratch = new List<long>();
		private static float nextTick;
		private static Runner runner;

		/// <summary>Props whose collision is in physics, across every scene.</summary>
		public static int ActiveCount { get; private set; }

		/// <summary>Chunk colliders in physics, across every scene.</summary>
		public static int ActiveChunks { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			sources.Clear();
			foci.Clear();
			wanted.Clear();
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
			var root = new GameObject("Streamed Prop Colliders") { hideFlags = HideFlags.DontSave };
			SceneManager.MoveGameObjectToScene(root, source.Scene);
			source.Root = root.transform;
			sources.Add(source);
			EnsureRunner();
			Fill(ImmediateRadius, int.MaxValue, true);
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
			sources.RemoveAt(at);
		}

		/// <summary>Keeps prop collision within <paramref name="radius"/> of <paramref name="transform"/>; the chunks right round it are built now.</summary>
		public static void AddFocus(Transform transform, float radius)
		{
			if (!Application.isPlaying || transform == null || foci.Exists(f => f.transform == transform))
			{
				return;
			}
			foci.Add((transform, radius));
			EnsureRunner();
			Fill(ImmediateRadius, int.MaxValue, true);
		}

		/// <summary>Stops keeping collision for <paramref name="transform"/>.</summary>
		public static void RemoveFocus(Transform transform)
		{
			foci.RemoveAll(f => f.transform == transform);
		}

		// ── Streaming ─────────────────────────────────────────────────

		private static void Update()
		{
			// Every frame: chunks whose jobs finished move on.
			foreach (Source source in sources)
			{
				foreach (Chunk chunk in source.Chunks.Values)
				{
					if (!chunk.Live)
					{
						Advance(source, chunk, false);
					}
				}
			}
			if (Time.unscaledTime < nextTick)
			{
				return;
			}
			nextTick = Time.unscaledTime + TickSeconds;
			foci.RemoveAll(f => f.transform == null);
			// Right round every focus first, all of it, finished now; then outward, nearest first, within the budget.
			int started = Fill(ImmediateRadius, int.MaxValue, true);
			Fill(-1f, Mathf.Max(0, ChunksPerTick - started), false);
		}

		/// <summary>
		/// Brings every source's chunks in line with the focus points: chunks no focus wants are emptied, wanted chunks
		/// are started nearest first, at most <paramref name="budget"/>. <paramref name="reach"/> limits the wanted
		/// chunks to that distance (the immediate pass, whose chunks are finished before it returns when
		/// <paramref name="finish"/>); a negative reach uses each focus's own radius and also empties the chunks no
		/// longer wanted. Returns the chunks started.
		/// </summary>
		private static int Fill(float reach, int budget, bool finish)
		{
			int started = 0;
			Camera camera = null;
#if !UNITY_SERVER
			camera = Camera.main;
#endif
			foreach (Source source in sources)
			{
				if (source.Owner == null)
				{
					continue;
				}
				wanted.Clear();
				missing.Clear();
				foreach ((Transform transform, float radius) in foci)
				{
					if (transform != null && transform.gameObject.scene == source.Scene)
					{
						Want(source, transform.position, reach >= 0f ? Mathf.Min(reach, radius) : radius);
					}
				}
				if (camera != null)
				{
					Want(source, camera.transform.position, reach >= 0f ? Mathf.Min(reach, ClientRadius) : ClientRadius);
				}
				if (reach < 0f)
				{
					scratch.Clear();
					foreach (long cell in source.Chunks.Keys)
					{
						if (!wanted.Contains(cell))
						{
							scratch.Add(cell);
						}
					}
					foreach (long cell in scratch)
					{
						Empty(source, cell);
					}
				}
				missing.Sort((a, b) => a.distance.CompareTo(b.distance));
				foreach ((long cell, float _) in missing)
				{
					if (started >= budget)
					{
						break;
					}
					Chunk chunk = Start(source, cell);
					if (chunk != null)
					{
						started++;
						if (finish)
						{
							Advance(source, chunk, true);
						}
					}
				}
				if (finish)
				{
					// Chunks started by an earlier budgeted pass that a character now stands in: finished now as well.
					foreach (long cell in wanted)
					{
						if (source.Chunks.TryGetValue(cell, out Chunk chunk) && !chunk.Live)
						{
							Advance(source, chunk, true);
						}
					}
				}
			}
			return started;
		}

		/// <summary>Marks every chunk with props within <paramref name="radius"/> of <paramref name="at"/> as wanted, and the unstarted ones as missing.</summary>
		private static void Want(Source source, Vector3 at, float radius)
		{
			int x0 = Mathf.FloorToInt((at.x - radius) / CellMetres), x1 = Mathf.FloorToInt((at.x + radius) / CellMetres);
			int z0 = Mathf.FloorToInt((at.z - radius) / CellMetres), z1 = Mathf.FloorToInt((at.z + radius) / CellMetres);
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					// Distance from the point to the chunk's square.
					float dx = Mathf.Max(0f, Mathf.Max(x * CellMetres - at.x, at.x - (x + 1) * CellMetres));
					float dz = Mathf.Max(0f, Mathf.Max(z * CellMetres - at.z, at.z - (z + 1) * CellMetres));
					float distance = Mathf.Sqrt(dx * dx + dz * dz);
					if (distance > radius)
					{
						continue;
					}
					long cell = Key(x, z);
					if (!source.Cells.ContainsKey(cell) || !wanted.Add(cell))
					{
						continue;
					}
					if (!source.Chunks.ContainsKey(cell))
					{
						missing.Add((cell, distance));
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
			var byLayer = new Dictionary<int, List<(int mesh, Matrix4x4 matrix)>>();
			foreach ((int s, int i) in props)
			{
				ScenePropCollisionSet.Instance instance = source.Owner.Sets[s].Instances[i];
				int mesh = source.MeshOf[s][instance.Prototype];
				int layer = source.LayerOf[s][instance.Prototype];
				if (!byLayer.TryGetValue(layer, out List<(int, Matrix4x4)> pieces))
				{
					byLayer[layer] = pieces = new List<(int, Matrix4x4)>();
				}
				pieces.Add((mesh, Matrix4x4.TRS(instance.Position, instance.Rotation, instance.Scale)));
			}
			var chunk = new Chunk { Props = props.Count };
			foreach (KeyValuePair<int, List<(int mesh, Matrix4x4 matrix)>> group in byLayer)
			{
				var build = new Build
				{
					Layer = group.Key,
					PieceMesh = new NativeArray<int>(group.Value.Count, Allocator.Persistent),
					PieceMatrix = new NativeArray<Matrix4x4>(group.Value.Count, Allocator.Persistent),
					Output = Mesh.AllocateWritableMeshData(1),
				};
				for (int k = 0; k < group.Value.Count; k++)
				{
					build.PieceMesh[k] = group.Value[k].mesh;
					build.PieceMatrix[k] = group.Value[k].matrix;
				}
				build.Merge = new MergeJob { Sources = source.MeshData, PieceMesh = build.PieceMesh, PieceMatrix = build.PieceMatrix, Output = build.Output }.Schedule();
				build.Merging = true;
				chunk.Builds.Add(build);
			}
			source.Chunks[cell] = chunk;
			JobHandle.ScheduleBatchedJobs();
			return chunk;
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
					build.PieceMesh.Dispose();
					build.PieceMatrix.Dispose();
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
					build.PieceMesh.Dispose();
					build.PieceMatrix.Dispose();
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
			}
			if (chunk.Live)
			{
				ActiveCount -= chunk.Props;
				ActiveChunks -= chunk.Builds.Count;
			}
			source.Chunks.Remove(cell);
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

		private static long Key(int x, int z) => ((long)x << 32) | (uint)z;

		// ── Jobs ──────────────────────────────────────────────────────

		/// <summary>One chunk's collision: every piece's mesh, transformed into the chunk's (world) space, as one positions-only mesh.</summary>
		private struct MergeJob : IJob
		{
			[ReadOnly] public Mesh.MeshDataArray Sources;
			[ReadOnly] public NativeArray<int> PieceMesh;
			[ReadOnly] public NativeArray<Matrix4x4> PieceMatrix;
			public Mesh.MeshDataArray Output;

			public void Execute()
			{
				int vertexCount = 0, indexCount = 0;
				for (int p = 0; p < PieceMesh.Length; p++)
				{
					Mesh.MeshData source = Sources[PieceMesh[p]];
					vertexCount += source.vertexCount;
					for (int s = 0; s < source.subMeshCount; s++)
					{
						indexCount += source.GetSubMesh(s).indexCount;
					}
				}
				Mesh.MeshData output = Output[0];
				output.SetVertexBufferParams(vertexCount, new VertexAttributeDescriptor(VertexAttribute.Position));
				output.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
				NativeArray<Vector3> positions = output.GetVertexData<Vector3>();
				NativeArray<int> indices = output.GetIndexData<int>();
				int vertexAt = 0, indexAt = 0;
				for (int p = 0; p < PieceMesh.Length; p++)
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
