using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// The climbables of every loaded scene (<see cref="ScenePointsOfInterest.Climbables"/>), keyed by scene and filed
	/// by horizontal cell, for the climbing movement mode (both peers) and the server's NavMesh links.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Keyed by scene,</b> as <see cref="Weather.WaterQuery"/> is: a scene server runs several world scenes, and
	/// stacked instances of one, at once, each in its own physics scene. A character asks with its own scene, so it only
	/// ever finds the ladders of the world it stands in.
	/// </para>
	/// <para>
	/// <b>A climbable is its index in its scene's list.</b> Server and client load the same asset, so the index means
	/// the same climbable on both, and it is what the reconcile carries (as index + 1; zero for none). The scene's handle
	/// differs between the peers and never goes on the wire.
	/// </para>
	/// </remarks>
	public static class SceneClimbables
	{
		/// <summary>The most climbables a scene may have: the reconcile carries index + 1 in sixteen bits.</summary>
		public const int MaxPerScene = ushort.MaxValue - 1;

		private sealed class Set
		{
			public object Owner;
			public ClimbableIndex Index;
		}

		private static readonly Dictionary<int, Set> scenes = new Dictionary<int, Set>();

		/// <summary>Bumped whenever a scene's climbables come or go, so a reader can cache what it found.</summary>
		public static int Version { get; private set; }

		/// <summary>Offers <paramref name="scene"/>'s climbables. A second owner for the same scene replaces the first.</summary>
		public static void Register(Scene scene, object owner, IReadOnlyList<SceneClimbable> climbables)
		{
			if (climbables == null)
			{
				return;
			}
			scenes[scene.handle] = new Set { Owner = owner, Index = new ClimbableIndex(climbables) };
			Version++;
		}

		/// <summary>Withdraws <paramref name="scene"/>'s climbables, when <paramref name="owner"/> is the one that offered them.</summary>
		public static void Unregister(Scene scene, object owner)
		{
			if (scenes.TryGetValue(scene.handle, out Set set) && ReferenceEquals(set.Owner, owner))
			{
				scenes.Remove(scene.handle);
				Version++;
			}
		}

		/// <summary>Forgets every scene's climbables.</summary>
		public static void Clear()
		{
			scenes.Clear();
			Version++;
		}

		/// <summary>True when <paramref name="scene"/> has any climbables.</summary>
		public static bool Has(Scene scene) => scenes.ContainsKey(scene.handle);

		/// <summary>Every climbable of <paramref name="scene"/>, by index.</summary>
		public static bool TryGetAll(Scene scene, out IReadOnlyList<SceneClimbable> climbables)
		{
			if (scenes.TryGetValue(scene.handle, out Set set))
			{
				climbables = set.Index.Source;
				return true;
			}
			climbables = null;
			return false;
		}

		/// <summary>The climbable at <paramref name="index"/> in <paramref name="scene"/>, when there is one.</summary>
		public static bool TryGet(Scene scene, int index, out SceneClimbable climbable)
		{
			if (index >= 0 && index < MaxPerScene && scenes.TryGetValue(scene.handle, out Set set) && index < set.Index.Source.Count)
			{
				climbable = set.Index.Source[index];
				return true;
			}
			climbable = default;
			return false;
		}

		/// <summary>
		/// The indices of <paramref name="scene"/>'s climbables whose line passes within <paramref name="reach"/> of
		/// <paramref name="at"/> across the ground (heights are the caller's to judge), ascending. False with none.
		/// </summary>
		public static bool Near(Scene scene, Vector3 at, float reach, List<int> into)
		{
			into.Clear();
			return scenes.TryGetValue(scene.handle, out Set set) && set.Index.Query(at, reach, into);
		}
	}

	/// <summary>
	/// One scene's climbables filed by horizontal cell (<see cref="CellMetres"/>): each climbable in every cell its line's
	/// footprint touches. Built once per scene; queries allocate nothing once the scratch has grown.
	/// </summary>
	public sealed class ClimbableIndex
	{
		public const float CellMetres = 16f;

		public readonly IReadOnlyList<SceneClimbable> Source;
		private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();

		public ClimbableIndex(IReadOnlyList<SceneClimbable> climbables)
		{
			Source = climbables;
			int count = Mathf.Min(climbables.Count, SceneClimbables.MaxPerScene);
			for (int i = 0; i < count; i++)
			{
				SceneClimbable c = climbables[i];
				if (!ClimbingRules.IsValid(in c))
				{
					continue;
				}
				float pad = Mathf.Max(0f, c.HalfWidth);
				int x0 = Cell(Mathf.Min(c.Bottom.x, c.Top.x) - pad), x1 = Cell(Mathf.Max(c.Bottom.x, c.Top.x) + pad);
				int z0 = Cell(Mathf.Min(c.Bottom.z, c.Top.z) - pad), z1 = Cell(Mathf.Max(c.Bottom.z, c.Top.z) + pad);
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						long key = Key(x, z);
						if (!cells.TryGetValue(key, out List<int> list))
						{
							cells[key] = list = new List<int>(2);
						}
						list.Add(i);
					}
				}
			}
		}

		/// <summary>
		/// The indices of every climbable filed in the cells under the square of <paramref name="reach"/> round
		/// <paramref name="at"/>, ascending and each once. False with none.
		/// </summary>
		public bool Query(Vector3 at, float reach, List<int> into)
		{
			into.Clear();
			if (float.IsNaN(at.x) || float.IsNaN(at.z) || float.IsNaN(reach) || reach < 0f || reach > 64f)
			{
				return false;
			}
			int x0 = Cell(at.x - reach), x1 = Cell(at.x + reach);
			int z0 = Cell(at.z - reach), z1 = Cell(at.z + reach);
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					if (cells.TryGetValue(Key(x, z), out List<int> list))
					{
						for (int k = 0; k < list.Count; k++)
						{
							if (!into.Contains(list[k]))
							{
								into.Add(list[k]);
							}
						}
					}
				}
			}
			// Ascending, so whichever reads them (the attach rule picks the nearest, ties to the lowest) reaches the same answer on every peer.
			into.Sort();
			return into.Count > 0;
		}

		private static int Cell(float metres) => Mathf.FloorToInt(metres / CellMetres);

		private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
	}
}
