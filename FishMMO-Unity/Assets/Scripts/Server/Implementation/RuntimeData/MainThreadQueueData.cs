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
		public override void Deinitialize()
		{
			Drain();
		}

		/// <inheritdoc/>
		public void Enqueue(Action action)
		{
			lock (_lock)
			{
				_queue.Enqueue(action);
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

			List<Action> actions;
			lock (_lock)
			{
				if (_queue.Count == 0)
				{
					return 0;
				}

				int count = Math.Min(maxActions, _queue.Count);
				actions = new List<Action>(count);
				for (int i = 0; i < count; ++i)
				{
					actions.Add(_queue.Dequeue());
				}
			}

			for (int i = 0; i < actions.Count; i++)
			{
				actions[i].Invoke();
			}

			return actions.Count;
		}
	}
}