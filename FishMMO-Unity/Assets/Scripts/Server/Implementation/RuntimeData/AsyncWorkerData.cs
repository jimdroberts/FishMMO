using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FishMMO.Logging;
using FishMMO.Server.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Centralized async work queue backed by System.Threading.Channels.
	///
	/// Replaces fire-and-forget <c>_ = SomeAsync(...)</c> across all server systems with
	/// a bounded, backpressure-aware work queue processed by a fixed number of worker loops.
	///
	/// Design:
	/// <list type="bullet">
	///   <item>Multiple channels: one per worker for entity-keyed ordering via consistent hashing.</item>
	///   <item>Bounded capacity with DropWrite full-mode returns false on Enqueue (backpressure).</item>
	///   <item>Workers are long-lived async loops consuming from their assigned channel.</item>
	///   <item>Graceful shutdown via CancellationToken; workers drain remaining items on cancel.</item>
	/// </list>
	///
	/// Usage:
	/// <code>
	/// // Simple (round-robin to any worker):
	/// asyncWorkerData.Enqueue(() => PersistInventoryAsync(dto));
	///
	/// // Entity-keyed (same characterID always goes to the same worker):
	/// asyncWorkerData.Enqueue(() => SaveCharacterAsync(charData), characterID);
	/// </code>
	///
	/// Systems declare dependency via:
	/// <c>[RequiresDataContainer(typeof(AsyncWorkerData))]</c>
	///
	/// <para><b>Pool isolation:</b> Total capacity = workerCount × channelCapacity.
	/// Default 8 × 2048 = 16,384 slots. A single saturated system cannot starve
	/// others because entity-keyed enqueues are hash-routed to specific workers
	/// and round-robin enqueues are distributed evenly. Increase workerCount for
	/// high-load deployments; increase channelCapacity to absorb bursts.</para>
	/// </summary>
	public class AsyncWorkerData : RuntimeDataContainer, IAsyncWorkerData
	{
		/// <summary>
		/// Number of worker loops. Configurable per-deployment via Inspector.
		/// Increase for high-load servers to prevent cross-system starvation.
		/// </summary>
		[SerializeField]
		private int workerCount = 8;

		/// <summary>
		/// Bounded capacity per worker channel. Backpressure kicks in when full.
		/// Total pool capacity = workerCount × channelCapacity. Configurable.
		/// </summary>
		[SerializeField]
		private int channelCapacity = 2048;

		/// <summary>Array of worker channels, one per worker loop.</summary>
		private Channel<AsyncWorkItem>[] channels;
		/// <summary>Array of running worker tasks.</summary>
		private Task[] workerTasks;
		/// <summary>CancellationTokenSource for signalling worker shutdown.</summary>
		private CancellationTokenSource cts;
		/// <summary>Total count of completed work items across all workers.</summary>
		private long completedCount;
		/// <summary>Round-robin counter for distributing work across workers.</summary>
		private long roundRobinCounter;

		/// <inheritdoc/>
		public int PendingCount
		{
			get
			{
				if (channels == null) return 0;
				int count = 0;
				for (int i = 0; i < channels.Length; i++)
				{
					count += channels[i].Reader.Count;
				}
				return count;
			}
		}

		/// <inheritdoc/>
		public long CompletedCount => Interlocked.Read(ref completedCount);

		/// <summary>
		/// Initializes channels and starts worker loops.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			workerCount = Mathf.Max(1, workerCount);
			channelCapacity = Mathf.Max(256, channelCapacity);

			cts = new CancellationTokenSource();
			channels = new Channel<AsyncWorkItem>[workerCount];
			workerTasks = new Task[workerCount];

			for (int i = 0; i < workerCount; i++)
			{
				channels[i] = Channel.CreateBounded<AsyncWorkItem>(new BoundedChannelOptions(channelCapacity)
				{
					FullMode = BoundedChannelFullMode.DropWrite,
					SingleReader = true,
					SingleWriter = false
				});

				int workerId = i;
				workerTasks[i] = Task.Run(() => WorkerLoopAsync(workerId, cts.Token));
			}

			_ = Log.Debug("AsyncWorkerData", $"Initialized ({workerCount} workers, {channelCapacity} capacity per channel, {workerCount * channelCapacity} total slots)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		public bool Enqueue(Func<Task> work, string callerName = null)
		{
			if (channels == null || work == null) return false;

			// Round-robin across workers
			long index = Interlocked.Increment(ref roundRobinCounter);
			int workerIndex = (int)(((index % workerCount) + workerCount) % workerCount);
			return channels[workerIndex].Writer.TryWrite(new AsyncWorkItem(work, callerName));
		}

		/// <inheritdoc/>
		public bool Enqueue(Func<Task> work, long entityKey, string callerName = null)
		{
			if (channels == null || work == null) return false;

			// Consistent hash: same entityKey always maps to the same worker
			int workerIndex = (int)(((entityKey % workerCount) + workerCount) % workerCount);

			return channels[workerIndex].Writer.TryWrite(new AsyncWorkItem(work, callerName));
		}

		/// <summary>
		/// Long-lived worker loop that processes work items from its assigned channel.
		/// Drains remaining items after cancellation before exiting.
		/// </summary>
		private async Task WorkerLoopAsync(int workerId, CancellationToken ct)
		{
			var channel = channels[workerId];

			try
			{
				await foreach (AsyncWorkItem item in channel.Reader.ReadAllAsync(ct))
				{
					try
					{
						await item.Work();
						Interlocked.Increment(ref completedCount);
					}
					catch (Exception ex)
					{
						Interlocked.Increment(ref completedCount);
						string caller = item.CallerName ?? "unknown";
						await Log.Error("AsyncWorkerData", $"Worker {workerId} failed executing work from '{caller}': {ex}");
					}
				}
			}
			catch (OperationCanceledException)
			{
				// Expected during shutdown
			}

			// Drain remaining items after cancellation
			while (channel.Reader.TryRead(out AsyncWorkItem remaining))
			{
				try
				{
					await remaining.Work();
					Interlocked.Increment(ref completedCount);
				}
				catch (Exception ex)
				{
					Interlocked.Increment(ref completedCount);
					string caller = remaining.CallerName ?? "unknown";
					await Log.Error("AsyncWorkerData", $"Worker {workerId} drain failed for '{caller}': {ex}");
				}
			}

			await Log.Debug("AsyncWorkerData", $"Worker {workerId} shutdown complete.");
		}

		/// <summary>
		/// Clears all pending items from all channels without executing them.
		/// </summary>
		public override void Clear()
		{
			if (channels == null) return;

			for (int i = 0; i < channels.Length; i++)
			{
				while (channels[i].Reader.TryRead(out _)) { }
			}
		}

		/// <summary>
		/// Signals cancellation to all workers and waits for graceful drain.
		/// </summary>
		protected override void OnDeinitialize()
		{
			if (cts != null)
			{
				cts.Cancel();

				if (channels != null)
				{
					for (int i = 0; i < channels.Length; i++)
					{
						channels[i].Writer.TryComplete();
					}
				}

				if (workerTasks != null)
				{
					try
					{
						Task.WaitAll(workerTasks, TimeSpan.FromSeconds(10));
					}
					catch (AggregateException) { /* workers may have faulted */ }
				}

				long pending = PendingCount;
				long completed = CompletedCount;
				_ = Log.Debug("AsyncWorkerData", $"Deinitialized (Completed={completed}, Remaining={pending})");

				cts.Dispose();
				cts = null;
			}

			channels = null;
			workerTasks = null;
		}

		/// <summary>
		/// Represents a single unit of async work queued to a worker channel.
		/// </summary>
		private readonly struct AsyncWorkItem
		{
			public readonly Func<Task> Work;
			public readonly string CallerName;

			public AsyncWorkItem(Func<Task> work, string callerName)
			{
				Work = work;
				CallerName = callerName;
			}
		}
	}
}