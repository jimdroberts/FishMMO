using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The instance buffers <see cref="AmbientLifeSystem"/> and <see cref="SeaLifeSystem"/> draw from: one
	/// <see cref="GraphicsBuffer"/> of <see cref="LifeInstanceRecord"/>s per camera kind (the game camera and the scene view),
	/// filled by one <see cref="GraphicsBuffer.SetData(System.Array, int, int, int)"/> per camera and read by every kind's
	/// procedural draw.
	/// </summary>
	/// <remarks>
	/// A buffer per camera kind, not one for both: the game and scene-view cameras draw different animals in one frame, and
	/// a buffer written again for the second camera could be read by the first camera's draws should the device move the
	/// upload ahead of them. The records array and the buffers grow by doubling (<see cref="LifeInstancePacking.Grown"/>) and
	/// never shrink, so a frame allocates nothing. Main thread only.
	/// </remarks>
	public sealed class LifeInstanceBuffers
	{
		private const int MinimumCapacity = 256;

		private readonly string label;
		private readonly GraphicsBuffer[] buffers = new GraphicsBuffer[2];

		/// <summary>This camera's records, packed by the caller before <see cref="Upload"/>.</summary>
		public LifeInstanceRecord[] Records = new LifeInstanceRecord[MinimumCapacity];

		/// <param name="label">The buffers' name, for the frame debugger and the memory profiler.</param>
		public LifeInstanceBuffers(string label)
		{
			this.label = label;
		}

		/// <summary>Makes room for <paramref name="count"/> records in <see cref="Records"/>, keeping those already packed.</summary>
		public void Reserve(int count)
		{
			int capacity = LifeInstancePacking.Grown(Records.Length, count, MinimumCapacity);
			if (capacity != Records.Length)
			{
				System.Array.Resize(ref Records, capacity);
			}
		}

		/// <summary>
		/// Uploads the first <paramref name="count"/> records to this camera kind's buffer and returns it; null when there is
		/// nothing to draw.
		/// </summary>
		public GraphicsBuffer Upload(Camera camera, int count)
		{
			if (count <= 0)
			{
				return null;
			}
			int slot = camera != null && camera.cameraType == CameraType.SceneView ? 1 : 0;
			GraphicsBuffer buffer = buffers[slot];
			int capacity = LifeInstancePacking.Grown(buffer != null ? buffer.count : 0, count, MinimumCapacity);
			if (buffer == null || !buffer.IsValid() || buffer.count != capacity)
			{
				buffer?.Release();
				buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, LifeInstanceRecord.Stride)
				{
					name = slot == 0 ? label + " (game)" : label + " (scene view)",
				};
				buffers[slot] = buffer;
			}
			buffer.SetData(Records, 0, 0, count);
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
