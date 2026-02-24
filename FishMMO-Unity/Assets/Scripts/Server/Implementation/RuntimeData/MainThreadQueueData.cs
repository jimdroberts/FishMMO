using System;
using System.Collections.Generic;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Base runtime data container providing a thread-safe main-thread action queue.
	/// Async worker threads enqueue actions via Enqueue(), and the main thread
	/// drains them via Drain() each frame (typically in OnLateUpdate).
	/// 
	/// Each system that needs main-thread marshalling should have its own concrete
	/// subclass so the DataContainerRegistry creates separate instances per system.
	/// </summary>
	public abstract class MainThreadQueueData : RuntimeDataContainer, IMainThreadQueueData
	{
		private readonly Queue<Action> _queue = new Queue<Action>();
		private readonly object _lock = new object();

		/// <summary>
		/// Reusable scratch list for draining actions, avoiding per-call allocation.
		/// Only accessed from the main thread via Drain(), so no synchronization needed.
		/// </summary>
		private readonly List<Action> drainBuffer = new List<Action>();

		/// <summary>
		/// No additional initialization needed — queue is ready at construction.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears any pending actions without executing them.
		/// Called during server data reset.
		/// </summary>
		public override void Clear()
		{
			lock (_lock)
			{
				_queue.Clear();
			}
		}

		/// <summary>
		/// Drains remaining actions and releases resources.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Drain();
		}

		/// <summary>
		/// Defense-in-depth: maximum pending actions before new enqueues are rejected.
		/// </summary>
		private const int MaxQueueCapacity = 10000;

		/// <inheritdoc/>
		public bool TryEnqueue(Action action)
		{
			lock (_lock)
			{
				if (_queue.Count >= MaxQueueCapacity)
				{
					return false;
				}
				_queue.Enqueue(action);
				return true;
			}
		}

		/// <inheritdoc/>
		public void Drain()
		{
			Drain(int.MaxValue);
		}

		/// <inheritdoc/>
		public int Drain(int maxActions)
		{
			if (maxActions <= 0)
			{
				return 0;
			}

			drainBuffer.Clear();
			lock (_lock)
			{
				if (_queue.Count == 0)
				{
					return 0;
				}

				int count = Math.Min(maxActions, _queue.Count);
				for (int i = 0; i < count; ++i)
				{
					drainBuffer.Add(_queue.Dequeue());
				}
			}

			for (int i = 0; i < drainBuffer.Count; i++)
			{
				drainBuffer[i].Invoke();
			}

			int drained = drainBuffer.Count;
			drainBuffer.Clear();
			return drained;
		}
	}
}