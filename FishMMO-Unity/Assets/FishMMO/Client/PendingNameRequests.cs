using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's name requests of one kind: who is waiting on which ID, which IDs are due to be
	/// sent, and when each was last sent. Pure bookkeeping: no network, no Unity, main thread only.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Coalesced, not sent per ask.</b> Asking for a name used to send one request per ID the
	/// moment it was asked for, so opening a 100-member roster sent 100 requests in one frame
	/// (hot-path audit M18). An ask now only queues its ID; <see cref="TakeBatch"/> hands out the
	/// queue a batch at a time, and the caller sends each batch as one request.
	/// </para>
	/// <para>
	/// <b>Sent again on a later ask, never on its own.</b> The server answers a request over its
	/// connection's budget with nothing, and a request sent to a scene server the client has since
	/// left is never answered either. An ID still waiting <see cref="RetrySeconds"/> after it was
	/// sent is queued again by the next ask for it, so a lost request costs one retry window and
	/// not the session. Nothing re-sends by timer: a name nobody asks for again is not worth the
	/// traffic.
	/// </para>
	/// <para>
	/// <b>"No such entity" is an answer.</b> <see cref="ResolveMissing"/> drops the callbacks that
	/// were waiting (they captured UI rows and characters, and would otherwise be held for the
	/// session while every later ask appended to the chain) and holds the ID's retry window, so a
	/// caller that asks for a deleted character every frame costs one request per window.
	/// </para>
	/// </remarks>
	public sealed class PendingNameRequests
	{
		private readonly Dictionary<long, Action<string>> waiting = new Dictionary<long, Action<string>>();
		private readonly Dictionary<long, double> sentAt = new Dictionary<long, double>();
		private readonly Queue<long> queue = new Queue<long>();
		private readonly HashSet<long> queued = new HashSet<long>();

		/// <summary>
		/// Seconds after it was sent (or answered "no such entity") before the next ask for an
		/// ID sends it again.
		/// </summary>
		public double RetrySeconds { get; }

		/// <summary>IDs something is waiting on.</summary>
		public int WaitingCount => waiting.Count;

		/// <summary>IDs due to go out in the next batch.</summary>
		public int QueuedCount => queued.Count;

		/// <summary>
		/// Creates an empty set of requests.
		/// </summary>
		/// <param name="retrySeconds">See <see cref="RetrySeconds"/>.</param>
		public PendingNameRequests(double retrySeconds)
		{
			RetrySeconds = retrySeconds > 0.0 ? retrySeconds : 0.0;
		}

		/// <summary>
		/// Records an ask for an ID's name, and queues the ID to be sent if it is due.
		/// </summary>
		/// <param name="id">The ID whose name is wanted.</param>
		/// <param name="callback">Receives the name when it arrives. Combined with any already waiting.</param>
		/// <param name="now">The current time, in seconds.</param>
		/// <returns>True if the ID was queued by this ask.</returns>
		public bool Request(long id, Action<string> callback, double now)
		{
			waiting[id] = waiting.TryGetValue(id, out Action<string> chain) ? chain + callback : callback;

			if (queued.Contains(id))
			{
				return false;
			}
			if (sentAt.TryGetValue(id, out double last) && now - last < RetrySeconds)
			{
				return false;
			}

			queued.Add(id);
			queue.Enqueue(id);
			return true;
		}

		/// <summary>
		/// Takes up to <paramref name="max"/> queued IDs to send now, and stamps each as sent.
		/// The rest stay queued, in order, for the next call.
		/// </summary>
		/// <param name="now">The current time, in seconds.</param>
		/// <param name="max">Most IDs to take.</param>
		/// <param name="into">Receives the IDs. Not cleared first.</param>
		/// <returns>The number taken.</returns>
		public int TakeBatch(double now, int max, List<long> into)
		{
			int taken = 0;
			while (taken < max && queue.Count > 0)
			{
				long id = queue.Dequeue();

				// Answered (or dropped) while it waited in the queue: nothing left to ask for.
				if (!queued.Remove(id) || !waiting.ContainsKey(id))
				{
					continue;
				}

				sentAt[id] = now;
				into.Add(id);
				++taken;
			}
			return taken;
		}

		/// <summary>
		/// A name arrived: hands back everything that was waiting on it, and forgets the ID.
		/// </summary>
		/// <param name="id">The ID answered.</param>
		/// <param name="callbacks">Everything waiting on it, or null.</param>
		/// <returns>True if anything was waiting.</returns>
		public bool Resolve(long id, out Action<string> callbacks)
		{
			sentAt.Remove(id);
			queued.Remove(id);
			if (waiting.TryGetValue(id, out callbacks))
			{
				waiting.Remove(id);
				return true;
			}
			return false;
		}

		/// <summary>
		/// The server looked and there is no such entity: drops what was waiting without calling
		/// it, and holds the ID's retry window from now.
		/// </summary>
		/// <param name="id">The ID answered.</param>
		/// <param name="now">The current time, in seconds.</param>
		/// <returns>True if anything was waiting.</returns>
		public bool ResolveMissing(long id, double now)
		{
			queued.Remove(id);
			sentAt[id] = now;
			return waiting.Remove(id);
		}

		/// <summary>
		/// Forgets everything.
		/// </summary>
		public void Clear()
		{
			waiting.Clear();
			sentAt.Clear();
			queue.Clear();
			queued.Clear();
		}
	}
}
