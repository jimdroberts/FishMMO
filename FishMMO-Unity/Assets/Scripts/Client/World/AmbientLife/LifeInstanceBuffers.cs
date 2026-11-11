using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The instance buffers <see cref="AmbientLifeSystem"/> and <see cref="SeaLifeSystem"/> draw from: one
	/// <see cref="GraphicsBuffer"/> of <see cref="LifeInstanceRecord"/>s per camera kind (the game camera and the scene view),
	/// each kind's animals a run of it at a fixed place, read by the kind's procedural draw.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A kind's run never moves.</b> Kind k's run starts at k × <see cref="Slot"/> (<see cref="LifeInstancePacking.RunStart"/>),
	/// so the start every kind's material holds (<c>_LifeInstanceBase</c>, in UnityPerMaterial for the SRP Batcher) stays the
	/// same frame after frame; only the draw's instance count changes. The runs used to be packed end to end, so a kind's start
	/// moved whenever any kind before it gained or lost an animal, and in the browser the material value reached the draw a frame
	/// late: for that frame the kind read another kind's run, and every animal of it and of every kind after it blinked out
	/// together, birds and rabbits alike, most often while things moved.
	/// </para>
	/// <para>
	/// <see cref="Slot"/> grows by doubling to hold the longest run (<see cref="LifeInstancePacking.Grown"/>) and never shrinks, so
	/// the layout settles in the first frames and a frame allocates nothing. Each kind's run is uploaded on its own (one
	/// <see cref="GraphicsBuffer.SetData(System.Array, int, int, int)"/> per drawn kind), never the empty space between runs.
	/// </para>
	/// <para>
	/// A buffer per camera kind, not one for both: the game and scene-view cameras draw different animals in one frame, and
	/// a buffer written again for the second camera could be read by the first camera's draws should the device move the
	/// upload ahead of them. Main thread only.
	/// </para>
	/// </remarks>
	public sealed class LifeInstanceBuffers
	{
		/// <summary>The smallest run a kind is given.</summary>
		public const int MinimumSlot = 32;

		private readonly string label;
		private readonly GraphicsBuffer[] buffers = new GraphicsBuffer[2];

		/// <summary>This camera's records, kind k's at <see cref="LifeInstancePacking.RunStart"/>(k, <see cref="Slot"/>), packed by the caller before <see cref="Upload"/>.</summary>
		public LifeInstanceRecord[] Records = new LifeInstanceRecord[MinimumSlot];

		/// <summary>Records each kind's run holds: kind k's run starts at k × Slot.</summary>
		public int Slot { get; private set; } = MinimumSlot;

		/// <param name="label">The buffers' name, for the frame debugger and the memory profiler.</param>
		public LifeInstanceBuffers(string label)
		{
			this.label = label;
		}

		/// <summary>
		/// Lays <see cref="Records"/> out as <paramref name="kinds"/> runs, each long enough for <paramref name="longestRun"/>:
		/// <see cref="Slot"/> grows (it never shrinks) only when a run outgrows it.
		/// </summary>
		public void ReserveRuns(int kinds, int longestRun)
		{
			Slot = LifeInstancePacking.Grown(Slot, longestRun, MinimumSlot);
			int needed = Mathf.Max(1, kinds) * Slot;
			if (Records.Length < needed)
			{
				System.Array.Resize(ref Records, needed);
			}
		}

		/// <summary>
		/// Uploads each kind's run (the first <paramref name="counts"/>[k] records from its start) to this camera kind's buffer
		/// and returns it; null when there is nothing to draw.
		/// </summary>
		public GraphicsBuffer Upload(Camera camera, int kinds, int[] counts)
		{
			bool any = false;
			for (int k = 0; k < kinds; k++)
			{
				if (counts[k] > 0)
				{
					any = true;
					break;
				}
			}
			if (!any)
			{
				return null;
			}
			int slot = camera != null && camera.cameraType == CameraType.SceneView ? 1 : 0;
			GraphicsBuffer buffer = buffers[slot];
			int needed = Mathf.Max(1, kinds) * Slot;
			if (buffer == null || !buffer.IsValid() || buffer.count < needed)
			{
				buffer?.Release();
				buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, needed, LifeInstanceRecord.Stride)
				{
					name = slot == 0 ? label + " (game)" : label + " (scene view)",
				};
				buffers[slot] = buffer;
			}
			for (int k = 0; k < kinds; k++)
			{
				int count = Mathf.Min(counts[k], Slot);
				if (count > 0)
				{
					int start = LifeInstancePacking.RunStart(k, Slot);
					buffer.SetData(Records, start, start, count);
				}
			}
			return buffer;
		}

		/// <summary>Frees both buffers (the records array is kept; it holds no engine memory).</summary>
		public void Release()
		{
			for (int i = 0; i < buffers.Length; i++)
			{
				buffers[i]?.Release();
				buffers[i] = null;
			}
		}
	}
}
