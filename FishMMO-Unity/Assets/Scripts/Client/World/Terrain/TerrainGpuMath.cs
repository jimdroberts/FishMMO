using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The CPU mirror of FishTerrainInstancing.compute and the bookkeeping around it, free of GPU objects
	/// so it can be tested: the path choice, the slot layout, the append guard, the per-instance cull, and
	/// the instance-buffer range allocator.
	/// </summary>
	public static class TerrainGpuMath
	{
		/// <summary>Views per level: 0 the camera (drawn with shadows off), 1 the shadow casters (drawn shadows-only).</summary>
		public const int Views = 2;

		/// <summary>
		/// True when the GPU-driven path can run: compute shaders, structured buffers readable from the vertex
		/// stage (the indirect shaders read two), and the compute asset present. WebGL2 has neither, and
		/// takes the RenderMeshInstanced fallback.
		/// </summary>
		public static bool ChooseGpu(bool supportsComputeShaders, int maxComputeBufferInputsVertex, bool computeLoaded)
		{
			return supportsComputeShaders && maxComputeBufferInputsVertex >= 2 && computeLoaded;
		}

		/// <summary>The slot of (model's first slot, level, view).</summary>
		public static int Slot(int slotBase, int level, int view) => slotBase + level * Views + view;

		/// <summary>
		/// Lays the slots out in the visible buffer: each slot's base is the sum of the capacities before it.
		/// Returns the total capacity (the visible buffer's length).
		/// </summary>
		public static int LayoutSlots(IReadOnlyList<int> capacities, int[] bases)
		{
			int total = 0;
			for (int i = 0; i < capacities.Count; i++)
			{
				bases[i] = total;
				total += Mathf.Max(0, capacities[i]);
			}
			return total;
		}

		/// <summary>
		/// <c>_FishCommandBases</c>: for each indirect command (its index is the call's startCommand, which Unity
		/// hands the shader as <c>unity_BaseCommandID</c>), the base in the visible buffer of the slot it draws.
		/// </summary>
		public static uint[] CommandBases(IReadOnlyList<uint> commandSlots, int[] slotBases)
		{
			var bases = new uint[Mathf.Max(1, commandSlots.Count)];
			for (int c = 0; c < commandSlots.Count; c++)
			{
				bases[c] = (uint)slotBases[commandSlots[c]];
			}
			return bases;
		}

		/// <summary>
		/// The kernel's append: takes the next index of a slot's counter, and writes only when it is inside the
		/// capacity. Returns the index written, or −1 when the slot is full (the entry is dropped).
		/// </summary>
		public static int Append(ref uint counter, uint capacity)
		{
			uint at = counter++;
			return at < capacity ? (int)at : -1;
		}

		/// <summary>The instance count a command is finalised with: never more than the slot holds.</summary>
		public static uint Finalize(uint counter, uint capacity) => counter < capacity ? counter : capacity;

		/// <summary>The sphere is at least partly inside every plane.</summary>
		public static bool SphereVisible(Plane[] planes, Vector3 centre, float radius)
		{
			for (int k = 0; k < planes.Length; k++)
			{
				if (planes[k].GetDistanceToPoint(centre) < -radius)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>The sphere swept by <paramref name="length"/> along <paramref name="direction"/> is at least partly inside every plane.</summary>
		public static bool SweptSphereVisible(Plane[] planes, Vector3 centre, float radius, Vector3 direction, float length)
		{
			for (int k = 0; k < planes.Length; k++)
			{
				float d0 = planes[k].GetDistanceToPoint(centre);
				float d1 = d0 + Vector3.Dot(planes[k].normal, direction) * length;
				if (Mathf.Max(d0, d1) < -radius)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>One entry the kernel appends.</summary>
		public struct Emit
		{
			public int Level;
			public int View;
			public float Fade;
		}

		/// <summary>
		/// The kernel's per-instance decision (FishCull), for one instance matrix and model: what it appends,
		/// in order, into <paramref name="emits"/>. <paramref name="light"/> xyz is the sun's travel, w the
		/// shadow distance (0 no shadows); <paramref name="lod"/> x the screen factor, y orthographic, z the
		/// maximum LOD level.
		/// </summary>
		public static void Cull(in Matrix4x4 m, in FishModelData model, float drawDistance, float lodBias, Vector3 eye, Plane[] planes,
			Vector4 light, Vector3 lod, List<Emit> emits)
		{
			emits.Clear();
			Vector3 position = m.GetColumn(3);
			if ((position - eye).sqrMagnitude > drawDistance * drawDistance)
			{
				return;
			}
			float sx = ((Vector3)m.GetColumn(0)).magnitude, sy = ((Vector3)m.GetColumn(1)).magnitude, sz = ((Vector3)m.GetColumn(2)).magnitude;
			float factor = lod.x * lodBias;
			float worldSize = model.Size * Mathf.Max(sx, sy);
			bool orthographic = lod.y > 0.5f;
			float height = TerrainTreeMath.RelativeHeight(worldSize, Vector3.Distance(m.MultiplyPoint3x4(model.LocalReference), eye), factor, orthographic);
			int count = (int)model.LevelCount;
			var transitions = new float[count];
			var widths = new float[count];
			for (int i = 0; i < count; i++)
			{
				transitions[i] = model.Transitions[i];
				widths[i] = model.FadeWidths[i];
			}
			int level = TerrainTreeMath.SelectLodFaded(height, transitions, widths, (int)lod.z, out float fade, out int partner, out float partnerFade);
			if (level < 0)
			{
				return;
			}
			Vector3 centre = m.MultiplyPoint3x4(model.SphereCentre);
			float radius = model.SphereRadius * Mathf.Max(sx, Mathf.Max(sy, sz));
			if (SphereVisible(planes, centre, radius))
			{
				emits.Add(new Emit { Level = level, View = 0, Fade = fade });
				if (partner >= 0)
				{
					emits.Add(new Emit { Level = partner, View = 0, Fade = partnerFade });
				}
			}
			float shadowDistance = light.w;
			uint mask = model.ShadowMask;
			bool levelCasts = (mask & (1u << level)) != 0, partnerCasts = partner >= 0 && (mask & (1u << partner)) != 0;
			if (shadowDistance > 0f && (levelCasts || partnerCasts) && Vector3.Distance(centre, eye) <= shadowDistance + radius)
			{
				Vector3 d = light;
				float length = Mathf.Min(shadowDistance, 2f * radius / Mathf.Max(-d.y, 0.1f));
				if (SweptSphereVisible(planes, centre, radius, d, length))
				{
					if (levelCasts)
					{
						emits.Add(new Emit { Level = level, View = 1, Fade = fade });
					}
					if (partnerCasts)
					{
						emits.Add(new Emit { Level = partner, View = 1, Fade = partnerFade });
					}
				}
			}
		}

		/// <summary>
		/// The levels a run can reach (bit per level), from the largest relative height its instances can have
		/// (<paramref name="nearHeight"/>) to the smallest (<paramref name="farHeight"/>), fade partners included.
		/// Commands of levels outside every run's mask are not issued. 0 when the whole run is culled.
		/// </summary>
		public static int LevelMask(float nearHeight, float farHeight, float[] transitions, float[] widths, int maximumLodLevel)
		{
			int first = TerrainTreeMath.SelectLodFaded(nearHeight, transitions, widths, maximumLodLevel, out _, out _, out _);
			if (first < 0)
			{
				return 0;
			}
			int last = TerrainTreeMath.SelectLodFaded(farHeight, transitions, widths, maximumLodLevel, out _, out int partner, out _);
			last = last < 0 ? transitions.Length - 1 : Mathf.Max(last, partner);
			int mask = 0;
			for (int l = first; l <= last; l++)
			{
				mask |= 1 << l;
			}
			return mask;
		}

		/// <summary>
		/// Adds the span [<paramref name="lo"/>, <paramref name="hi"/>) to a sorted list of disjoint dirty spans,
		/// merging every span it overlaps or comes within <paramref name="gap"/> of. Far-apart changes stay
		/// separate uploads instead of one upload of everything between them.
		/// </summary>
		public static void AddDirty(List<Vector2Int> spans, int lo, int hi, int gap)
		{
			if (hi <= lo)
			{
				return;
			}
			int i = 0;
			while (i < spans.Count && spans[i].y + gap < lo)
			{
				i++;
			}
			while (i < spans.Count && spans[i].x - gap <= hi)
			{
				lo = Mathf.Min(lo, spans[i].x);
				hi = Mathf.Max(hi, spans[i].y);
				spans.RemoveAt(i);
			}
			spans.Insert(i, new Vector2Int(lo, hi));
		}

		/// <summary>Work items for <paramref name="count"/> consecutive instances: one per 64.</summary>
		public static int WorkItems(int count) => count <= 0 ? 0 : (count + FishWork.MaxInstances - 1) / FishWork.MaxInstances;

		/// <summary>The (x, y) thread-group counts for <paramref name="items"/> work items (65535 per dimension).</summary>
		public static Vector2Int CullGroups(int items)
		{
			if (items <= 0)
			{
				return Vector2Int.zero;
			}
			int y = (items + 65534) / 65535;
			return new Vector2Int(Mathf.Min(items, 65535), y);
		}
	}

	/// <summary>
	/// First-fit ranges in one big buffer (the GPU instance buffer): allocate, free with coalescing, grow.
	/// Allocation-free once its lists have grown.
	/// </summary>
	public sealed class RangeAllocator
	{
		private readonly List<Vector2Int> free = new List<Vector2Int>(); // x offset, y length, sorted by offset

		public int Capacity { get; private set; }
		public int Used { get; private set; }

		public RangeAllocator(int capacity)
		{
			Grow(capacity);
		}

		/// <summary>Adds room at the end (merged into a trailing free range).</summary>
		public void Grow(int capacity)
		{
			if (capacity <= Capacity)
			{
				return;
			}
			Release(Capacity, capacity - Capacity, false);
			Capacity = capacity;
		}

		/// <summary>The offset of a new range of <paramref name="count"/>, or −1 when nothing free is that long.</summary>
		public int Allocate(int count)
		{
			if (count <= 0)
			{
				return -1;
			}
			for (int i = 0; i < free.Count; i++)
			{
				Vector2Int f = free[i];
				if (f.y < count)
				{
					continue;
				}
				if (f.y == count)
				{
					free.RemoveAt(i);
				}
				else
				{
					free[i] = new Vector2Int(f.x + count, f.y - count);
				}
				Used += count;
				return f.x;
			}
			return -1;
		}

		/// <summary>Returns a range, merging it with free neighbours.</summary>
		public void Free(int offset, int count)
		{
			Release(offset, count, true);
		}

		private void Release(int offset, int count, bool used)
		{
			if (count <= 0)
			{
				return;
			}
			if (used)
			{
				Used -= count;
			}
			int i = 0;
			while (i < free.Count && free[i].x < offset)
			{
				i++;
			}
			free.Insert(i, new Vector2Int(offset, count));
			if (i + 1 < free.Count && free[i].x + free[i].y == free[i + 1].x)
			{
				free[i] = new Vector2Int(free[i].x, free[i].y + free[i + 1].y);
				free.RemoveAt(i + 1);
			}
			if (i > 0 && free[i - 1].x + free[i - 1].y == free[i].x)
			{
				free[i - 1] = new Vector2Int(free[i - 1].x, free[i - 1].y + free[i].y);
				free.RemoveAt(i);
			}
		}

		/// <summary>The longest free range.</summary>
		public int LargestFree()
		{
			int best = 0;
			foreach (Vector2Int f in free)
			{
				best = Mathf.Max(best, f.y);
			}
			return best;
		}
	}
}
