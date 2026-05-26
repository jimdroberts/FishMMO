using System;
using System.Threading;
using FishNet.Connection;
using FishMMO.Logging;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Shared utility for main-thread queue drain and enqueue operations.
	/// Eliminates copy-paste boilerplate across server systems (D1).
	///
	/// <para><b>Back-pressure observability:</b> the underlying
	/// <see cref="MainThreadQueueData"/> enforces a hard capacity cap (currently
	/// 10,000 pending actions) and returns <c>false</c> from <c>TryEnqueue</c>
	/// when full. Most call sites discard that bool because there is no useful
	/// off-thread fallback — the response broadcast is already on the main
	/// thread by contract. To keep the loss visible to operators, every
	/// rejected enqueue is counted here in a per-queue-type drop counter and
	/// surfaced through a rate-limited warning. A sustained non-zero drop rate
	/// indicates the main thread is stalling long enough for async DB workers
	/// to saturate the queue (10K * 16 ms drain budget ≈ several seconds of
	/// hang) and is a stronger DoS / GC-pause signal than any individual
	/// client time-out.</para>
	/// </summary>
	public static class MainThreadQueueHelper
	{
		/// <summary>
		/// Minimum interval between dropped-enqueue warnings per queue type.
		/// Chosen long enough to avoid log spam under a sustained overload but
		/// short enough that a transient drop burst still produces at least one
		/// log line.
		/// </summary>
		private static readonly long WarnIntervalTicks = TimeSpan.FromSeconds(5).Ticks;

		/// <summary>
		/// Per-queue-type drop tracker. Generic-typed static state gives us a
		/// zero-allocation, lock-free counter per concrete <typeparamref name="TQueue"/>
		/// without a <c>ConcurrentDictionary&lt;Type,…&gt;</c> lookup on the
		/// hot path.
		/// </summary>
		private static class DropTracker<TQueue>
		{
			/// <summary>Running total of rejected enqueues since process start.</summary>
			public static long Dropped;
			/// <summary>Last warning timestamp (DateTime.UtcNow.Ticks) for rate limiting.</summary>
			public static long LastWarnTicks;
		}

		/// <summary>
		/// Drains the main-thread queue, processing up to maxActions (or all if drainAll is true).
		/// </summary>
		/// <typeparam name="TQueue">The specific IMainThreadQueueData subtype for this system.</typeparam>
		/// <param name="server">Server instance (null-safe).</param>
		/// <param name="maxActions">Maximum actions to drain per call.</param>
		/// <param name="drainAll">If true, drains all queued actions regardless of maxActions.</param>
		public static void Drain<TQueue>(
			IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server,
			int maxActions,
			bool drainAll)
			where TQueue : class, IMainThreadQueueData
		{
			if (server?.DataContainerRegistry.TryGet<TQueue>(out var queueData) == true)
			{
				if (drainAll)
				{
					queueData.Drain();
				}
				else
				{
					queueData.Drain(maxActions);
				}
			}
		}

		/// <summary>
		/// Enqueues an action on the main-thread queue. Returns false if the queue is at capacity or unavailable.
		/// On rejection, increments a per-queue-type drop counter and emits a rate-limited warning so
		/// operators can detect sustained main-thread back-pressure that would otherwise present only as
		/// silent client time-outs.
		/// </summary>
		/// <typeparam name="TQueue">The specific IMainThreadQueueData subtype for this system.</typeparam>
		/// <param name="server">Server instance (null-safe).</param>
		/// <param name="action">The action to enqueue.</param>
		/// <returns>True if enqueued successfully; false if queue full or unavailable.</returns>
		public static bool TryEnqueue<TQueue>(
			IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server,
			Action action)
			where TQueue : class, IMainThreadQueueData
		{
			if (server?.DataContainerRegistry.TryGet<TQueue>(out var queueData) == true)
			{
				if (queueData.TryEnqueue(action))
				{
					return true;
				}
				RecordDrop<TQueue>(reasonQueueFull: true);
				return false;
			}
			RecordDrop<TQueue>(reasonQueueFull: false);
			return false;
		}

		/// <summary>
		/// Returns the running total of rejected enqueues for the specified queue type
		/// since process start. Intended for metrics scrape / health endpoints.
		/// </summary>
		public static long GetDroppedCount<TQueue>() where TQueue : class, IMainThreadQueueData
			=> Interlocked.Read(ref DropTracker<TQueue>.Dropped);

		private static void RecordDrop<TQueue>(bool reasonQueueFull)
			where TQueue : class, IMainThreadQueueData
		{
			long total = Interlocked.Increment(ref DropTracker<TQueue>.Dropped);

			long nowTicks = DateTime.UtcNow.Ticks;
			long last = Interlocked.Read(ref DropTracker<TQueue>.LastWarnTicks);
			if (nowTicks - last < WarnIntervalTicks)
			{
				return;
			}

			// CAS so only one thread emits the warning in any given window. Other
			// concurrent rejections in the same window still increment the counter
			// and will be reflected in the next log line's total.
			if (Interlocked.CompareExchange(ref DropTracker<TQueue>.LastWarnTicks, nowTicks, last) != last)
			{
				return;
			}

			string queueName = typeof(TQueue).Name;
			string reason = reasonQueueFull
				? "queue at capacity (main thread is not draining fast enough)"
				: "queue data container unavailable";
			Log.Warning("MainThreadQueueHelper",
				$"Dropped enqueue for {queueName}: {reason}. Total dropped since start: {total}. " +
				"Sustained drops indicate main-thread stalls (GC, scene load, lock contention) " +
				"and will surface as unanswered client requests.");
		}
	}
}
