using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene's baked emitters (<see cref="ScenePointsOfInterest.Emitters"/>: chimneys, fires, forges, lanterns), offered
	/// to the client's site lights and hearth smoke while the scene is loaded.
	/// </summary>
	/// <remarks>Client only: the object carries <see cref="ClientOnlyObject"/>, so server builds strip it.</remarks>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(ClientOnlyObject))]
	public sealed class SceneSiteEmitters : MonoBehaviour
	{
		public const string ObjectName = "Site Emitters";

		[Tooltip("The scene's points of interest, whose Emitters this offers.")]
		public ScenePointsOfInterest Points;

		private static readonly List<SceneSiteEmitters> active = new List<SceneSiteEmitters>();

		/// <summary>The enabled scenes' emitter sets.</summary>
		public static IReadOnlyList<SceneSiteEmitters> Active => active;

		/// <summary>Bumped whenever a set comes or goes, so a reader can cache what it found.</summary>
		public static int Version { get; private set; }

		/// <summary>
		/// Lights from outside any site (2026-10-25, I6i): a cloud world's bridge-end and landing-stage lights (SceneCloudWorld), which
		/// stand in no point of interest. Offered by <see cref="Near"/> after the sets', each read in full (a few dozen at most).
		/// </summary>
		private static readonly List<(Object Owner, IReadOnlyList<SceneEmitter> Emitters)> extra = new List<(Object, IReadOnlyList<SceneEmitter>)>();

		/// <summary>Offers an owner's lights to <see cref="Near"/> (replacing any it offered before).</summary>
		public static void AddLights(Object owner, IReadOnlyList<SceneEmitter> emitters)
		{
			RemoveLights(owner);
			if (owner != null && emitters != null && emitters.Count > 0)
			{
				extra.Add((owner, emitters));
				Version++;
			}
		}

		/// <summary>Withdraws an owner's lights.</summary>
		public static void RemoveLights(Object owner)
		{
			if (extra.RemoveAll(e => e.Owner == owner || e.Owner == null) > 0)
			{
				Version++;
			}
		}

		/// <summary>This scene's emitters (never null).</summary>
		public IReadOnlyList<SceneEmitter> Emitters => Points != null && Points.Emitters != null ? Points.Emitters : (IReadOnlyList<SceneEmitter>)System.Array.Empty<SceneEmitter>();

		private void OnEnable()
		{
			if (!active.Contains(this))
			{
				active.Add(this);
				Version++;
			}
			// The caves' animals' places, for the ambient life: client-only data, offered by this client-only object.
			if (Points != null && Points.CaveLife != null)
			{
				SceneCaveLife.Register(gameObject.scene, this, Points.CaveLife);
			}
		}

		private void OnDisable()
		{
			if (active.Remove(this))
			{
				Version++;
			}
			SceneCaveLife.Unregister(gameObject.scene, this);
		}

		/// <summary>
		/// The point of interest whose ground covers a place (within its radius and <paramref name="margin"/> more,
		/// horizontally), the nearest centre where several do; false outside every one. Sites on the sea or lake floor are
		/// left out: their ground is under the water.
		/// </summary>
		public static bool SiteAt(Vector3 at, float margin, out PointOfInterestKindInfo site)
		{
			site = default;
			float best = float.MaxValue;
			bool found = false;
			foreach (SceneSiteEmitters set in active)
			{
				if (set == null || set.Points == null || set.Points.Points == null)
				{
					continue;
				}
				foreach (PointOfInterestRecord point in set.Points.Points)
				{
					float dx = point.Position.x - at.x, dz = point.Position.z - at.z;
					float d2 = dx * dx + dz * dz;
					float reach = point.Radius + margin;
					if (d2 > reach * reach || d2 >= best)
					{
						continue;
					}
					PointOfInterestKindInfo info = point.Info;
					if (info.Has(PointOfInterestTraits.Underwater))
					{
						continue;
					}
					best = d2;
					site = info;
					found = true;
				}
			}
			return found;
		}

		/// <summary>Whether a point is inside any enabled scene's room (a building's inside: its walls and roof over it).</summary>
		public static bool InRoom(Vector3 at)
		{
			foreach (SceneSiteEmitters set in active)
			{
				if (set == null || set.Points == null || set.Points.Rooms == null)
				{
					continue;
				}
				foreach (SceneRoom room in set.Points.Rooms)
				{
					Vector3 d = at - room.Centre;
					if (Mathf.Abs(d.y) > room.HalfSize.y + 0.5f || d.x * d.x + d.z * d.z > room.HalfSize.sqrMagnitude + 1f)
					{
						continue;
					}
					Vector3 q = Quaternion.Inverse(Quaternion.Euler(0f, room.Yaw, 0f)) * d;
					if (Mathf.Abs(q.x) <= room.HalfSize.x + 0.3f && Mathf.Abs(q.z) <= room.HalfSize.z + 0.3f)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>Every enabled scene's rooms whose middles are within a distance of a point, nearest first, at most <paramref name="max"/>.</summary>
		public static void NearRooms(Vector3 at, float reach, int max, List<SceneRoom> into)
		{
			into.Clear();
			found.Clear();
			float r2 = reach * reach;
			for (int s = 0; s < active.Count; s++)
			{
				SceneSiteEmitters set = active[s];
				if (set == null || set.Points == null || set.Points.Rooms == null)
				{
					continue;
				}
				List<SceneRoom> rooms = set.Points.Rooms;
				for (int i = 0; i < rooms.Count; i++)
				{
					float d2 = (rooms[i].Centre - at).sqrMagnitude;
					if (d2 <= r2)
					{
						found.Add((d2, s, i));
					}
				}
			}
			found.Sort(NearestFirst);
			for (int i = 0; i < found.Count && i < max; i++)
			{
				into.Add(active[found[i].set].Points.Rooms[found[i].index]);
			}
		}

		/// <summary>Every enabled scene's emitters of some kinds within a distance of a point, nearest first, at most <paramref name="max"/>.</summary>
		/// <remarks>
		/// In play, read through each set's cell index (<see cref="EmitterIndex"/>): only the cells within reach are
		/// looked at, and each emitter in them is measured exactly as before, so the result is the same set. Equal
		/// distances are ordered by set and emitter, where the old sort left them in no particular order.
		/// </remarks>
		public static void Near(Vector3 at, float reach, System.Func<SceneEmitterKind, bool> kinds, int max, List<SceneEmitter> into)
		{
			using (Markers.Near.Auto())
			{
				into.Clear();
				found.Clear();
				float r2 = reach * reach;
				for (int s = 0; s < active.Count; s++)
				{
					SceneSiteEmitters set = active[s];
					if (set == null)
					{
						continue;
					}
					IReadOnlyList<SceneEmitter> emitters = set.Emitters;
					EmitterIndex index = Application.isPlaying ? set.IndexFor(emitters) : null;
					if (index == null || !index.Query(at, reach, candidates))
					{
						// Edit mode (the cut can change an emitter in place), or a reach covering most of the set: every one.
						candidates.Clear();
						for (int i = 0; i < emitters.Count; i++)
						{
							candidates.Add(i);
						}
					}
					for (int c = 0; c < candidates.Count; c++)
					{
						int i = candidates[c];
						SceneEmitter e = emitters[i];
						if (kinds != null && !kinds(e.Kind))
						{
							continue;
						}
						float d2 = (e.Position - at).sqrMagnitude;
						if (d2 <= r2)
						{
							found.Add((d2, s, i));
						}
					}
				}
				// The lights from outside any site (a cloud world's), as sets after the sites'.
				for (int s = 0; s < extra.Count; s++)
				{
					IReadOnlyList<SceneEmitter> emitters = extra[s].Emitters;
					for (int i = 0; i < emitters.Count; i++)
					{
						SceneEmitter e = emitters[i];
						if (kinds != null && !kinds(e.Kind))
						{
							continue;
						}
						float d2 = (e.Position - at).sqrMagnitude;
						if (d2 <= r2)
						{
							found.Add((d2, active.Count + s, i));
						}
					}
				}
				found.Sort(NearestFirst);
				for (int i = 0; i < found.Count && i < max; i++)
				{
					int set = found[i].set;
					into.Add(set < active.Count ? active[set].Emitters[found[i].index] : extra[set - active.Count].Emitters[found[i].index]);
				}
			}
		}

		/// <summary>In a class of its own so the component's statics stay free of native calls until a query runs.</summary>
		private static class Markers
		{
			public static readonly Unity.Profiling.ProfilerMarker Near = new Unity.Profiling.ProfilerMarker("SceneSiteEmitters.Near");
		}

		/// <summary>Scratch for the nearest-first queries: (squared distance, set, index), and one set's candidate indices.</summary>
		private static readonly List<(float d2, int set, int index)> found = new List<(float, int, int)>();
		private static readonly List<int> candidates = new List<int>();

		/// <summary>Nearest first; equal distances by set, then index, so the order never depends on the sort.</summary>
		private static readonly System.Comparison<(float d2, int set, int index)> NearestFirst = (a, b) =>
		{
			int byDistance = a.d2.CompareTo(b.d2);
			if (byDistance != 0)
			{
				return byDistance;
			}
			int bySet = a.set.CompareTo(b.set);
			return bySet != 0 ? bySet : a.index.CompareTo(b.index);
		};

		private EmitterIndex index;

		/// <summary>This set's cell index, rebuilt when its emitter list is replaced or changes length.</summary>
		private EmitterIndex IndexFor(IReadOnlyList<SceneEmitter> emitters)
		{
			if (index == null || !ReferenceEquals(index.Source, emitters) || index.Count != emitters.Count)
			{
				index = new EmitterIndex(emitters);
			}
			return index;
		}

		/// <summary>
		/// One set's emitters filed by horizontal cell (<see cref="CellMetres"/>): each cell's emitters are a run of
		/// <see cref="order"/>, found through <see cref="runs"/>. Built once per set; queries allocate nothing.
		/// </summary>
		internal sealed class EmitterIndex
		{
			public const float CellMetres = 32f;

			/// <summary>Padding round a query's square, metres: far more than float rounding at world coordinates.</summary>
			private const float Margin = 1f;

			public readonly IReadOnlyList<SceneEmitter> Source;
			public readonly int Count;
			private readonly int[] order;
			private readonly Dictionary<long, (int start, int count)> runs = new Dictionary<long, (int, int)>();

			public EmitterIndex(IReadOnlyList<SceneEmitter> emitters)
			{
				Source = emitters;
				Count = emitters.Count;
				order = new int[Count];
				var keys = new long[Count];
				for (int i = 0; i < Count; i++)
				{
					order[i] = i;
					keys[i] = Key(Mathf.FloorToInt(emitters[i].Position.x / CellMetres), Mathf.FloorToInt(emitters[i].Position.z / CellMetres));
				}
				System.Array.Sort(keys, order);
				for (int i = 0; i < Count;)
				{
					int start = i;
					while (i < Count && keys[i] == keys[start])
					{
						i++;
					}
					runs[keys[start]] = (start, i - start);
				}
			}

			/// <summary>
			/// The indices of every emitter that could be within <paramref name="reach"/> (3D) of <paramref name="at"/>:
			/// all in the cells under the padded square round it. False when that square has more cells than the set has
			/// runs (the caller then reads every emitter, which is no slower).
			/// </summary>
			public bool Query(Vector3 at, float reach, List<int> into)
			{
				into.Clear();
				float r = reach + Margin;
				if (float.IsNaN(r) || float.IsInfinity(r) || r > 1e6f)
				{
					return false;
				}
				int x0 = Mathf.FloorToInt((at.x - r) / CellMetres), x1 = Mathf.FloorToInt((at.x + r) / CellMetres);
				int z0 = Mathf.FloorToInt((at.z - r) / CellMetres), z1 = Mathf.FloorToInt((at.z + r) / CellMetres);
				if (((long)x1 - x0 + 1) * ((long)z1 - z0 + 1) > runs.Count)
				{
					return false;
				}
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						if (runs.TryGetValue(Key(x, z), out (int start, int count) run))
						{
							for (int k = 0; k < run.count; k++)
							{
								into.Add(order[run.start + k]);
							}
						}
					}
				}
				return true;
			}

			private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
		}
	}
}
