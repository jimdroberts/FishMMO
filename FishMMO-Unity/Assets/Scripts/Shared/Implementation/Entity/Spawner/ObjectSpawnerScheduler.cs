using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Drives every <see cref="ObjectSpawner"/>'s respawn checks from one place, ordered by when
	/// each spawner next needs looking at.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Spawners used to poll themselves: every spawner ran <c>Update</c> every frame and called
	/// <c>TryRespawn</c>, which returned immediately when there was nothing to do. Correct, but the
	/// cost is paid per spawner per frame whether or not anything is pending, so it scales with how
	/// much world exists rather than with how much of it is happening. A world with a hundred
	/// thousand spawners spent milliseconds per frame discovering there was nothing to do.
	/// </para>
	/// <para>
	/// This keeps the spawners in a min-heap keyed by their next wake time, so a frame in which
	/// nothing is due costs one <see cref="DateTime"/> comparison for the entire scene regardless of
	/// how many spawners exist. Work happens only when a deadline actually arrives.
	/// </para>
	/// <para>
	/// <b>Staleness is handled by version stamp, not by removal.</b> A heap cannot cheaply move an
	/// entry that is already in it, and a spawner's next wake time changes constantly — every death
	/// adds a timer that may fall before the one currently scheduled. Rather than search the heap,
	/// <see cref="Reschedule"/> bumps the spawner's stamp and pushes a fresh entry; any older entry
	/// for that spawner is recognised as stale when it surfaces and dropped. That makes rescheduling
	/// O(log n) with no search, at the cost of a heap that can hold several entries per spawner.
	/// The heap is drained of stale entries as they surface, so the excess is bounded by how often
	/// spawners reschedule rather than growing without limit.
	/// </para>
	/// </remarks>
	public static class ObjectSpawnerScheduler
	{
		/// <summary>
		/// One scheduled wake for one spawner.
		/// </summary>
		private struct Entry
		{
			/// <summary>When this spawner should next be looked at.</summary>
			public DateTime DueUtc;

			/// <summary>The spawner to wake.</summary>
			public ObjectSpawner Spawner;

			/// <summary>
			/// The spawner's stamp at the moment this entry was pushed. When it no longer matches
			/// the spawner's current stamp this entry has been superseded and must be discarded.
			/// </summary>
			public int Stamp;
		}

		/// <summary>Binary min-heap of pending wakes, ordered by <see cref="Entry.DueUtc"/>.</summary>
		private static readonly List<Entry> heap = new List<Entry>();

		/// <summary>The behaviour that calls <see cref="Tick"/>, created on first use.</summary>
		private static ObjectSpawnerSchedulerDriver driver;

		/// <summary>
		/// Number of wakes currently queued, including entries that have been superseded but not
		/// yet surfaced. Exposed for tests and diagnostics.
		/// </summary>
		public static int QueuedCount => heap.Count;

		/// <summary>
		/// Queues <paramref name="spawner"/> to be looked at when it next needs it, superseding any
		/// wake already queued for it.
		/// </summary>
		/// <remarks>
		/// Call after anything that changes when the spawner next has work: a despawn adding a
		/// respawn timer, a spawn consuming one, or the spawner reaching its cap and clearing them.
		/// A spawner with nothing pending is simply not queued, which is what makes an idle world
		/// free rather than merely cheap.
		/// </remarks>
		/// <param name="spawner">The spawner to reschedule.</param>
		public static void Reschedule(ObjectSpawner spawner)
		{
			if (spawner == null)
			{
				return;
			}

			// Supersede whatever was queued for this spawner, whether or not it needs a new wake.
			unchecked
			{
				++spawner.SchedulerStamp;
			}

			if (!spawner.TryGetNextWakeUtc(out DateTime dueUtc))
			{
				return;
			}

			EnsureDriver();
			Push(new Entry
			{
				DueUtc = dueUtc,
				Spawner = spawner,
				Stamp = spawner.SchedulerStamp,
			});
		}

		/// <summary>
		/// Drops <paramref name="spawner"/> from the schedule.
		/// </summary>
		/// <remarks>
		/// Bumping the stamp is the removal: entries already in the heap become stale and are
		/// discarded when they surface. Call from <c>OnStopNetwork</c> so a spawner in an unloaded
		/// scene is never woken.
		/// </remarks>
		/// <param name="spawner">The spawner to drop.</param>
		public static void Unregister(ObjectSpawner spawner)
		{
			if (spawner == null)
			{
				return;
			}

			unchecked
			{
				++spawner.SchedulerStamp;
			}
		}

		/// <summary>
		/// Wakes every spawner whose scheduled time has arrived.
		/// </summary>
		/// <remarks>
		/// The common case is that the earliest entry is not yet due, which costs one comparison for
		/// the whole scene. Each woken spawner reschedules itself, so a spawner that is blocked by a
		/// respawn condition comes back for another attempt rather than being forgotten.
		/// </remarks>
		/// <param name="nowUtc">The current UTC time.</param>
		public static void Tick(DateTime nowUtc)
		{
			/* Each pass pops one entry and re-queues at most one, so a well-behaved schedule cannot
			 * exceed the entries present when the tick began. The budget matters because a spawner
			 * that reschedules itself to a time already past would otherwise be popped forever
			 * inside one frame: the clock does not advance during a tick, so nothing would end the
			 * loop. Running out of budget degrades that spawner to being handled next frame - the
			 * poll this replaced - rather than hanging the server. */
			int budget = heap.Count + 1;

			while (heap.Count > 0 && heap[0].DueUtc <= nowUtc && budget-- > 0)
			{
				Entry entry = Pop();

				ObjectSpawner spawner = entry.Spawner;
				if (spawner == null)
				{
					// Destroyed without unregistering. Dropping the entry is the whole cleanup.
					continue;
				}

				if (entry.Stamp != spawner.SchedulerStamp)
				{
					// Superseded by a later Reschedule, or unregistered. A fresher entry either
					// already exists or the spawner deliberately has none.
					continue;
				}

				spawner.RunScheduledRespawn(nowUtc);

				/* Unconditionally, and after the work rather than before it. The spawner may have
				 * consumed a timer, been blocked by a condition, or hit its cap, and only it knows
				 * which — asking it again is how a blocked spawner gets a retry instead of falling
				 * out of the schedule for good. */
				Reschedule(spawner);
			}
		}

		/// <summary>
		/// Clears the schedule. Intended for tests and for a clean shutdown between play sessions.
		/// </summary>
		public static void Clear()
		{
			heap.Clear();
		}

		/// <summary>
		/// Creates the driver behaviour if it does not exist yet.
		/// </summary>
		private static void EnsureDriver()
		{
			if (driver != null)
			{
				return;
			}

			// Not created in EditMode: tests drive Tick directly, and a hidden object left in an
			// edit-mode scene would be saved into it.
			if (!Application.isPlaying)
			{
				return;
			}

			GameObject host = new GameObject(nameof(ObjectSpawnerSchedulerDriver))
			{
				hideFlags = HideFlags.HideAndDontSave,
			};

			UnityEngine.Object.DontDestroyOnLoad(host);
			driver = host.AddComponent<ObjectSpawnerSchedulerDriver>();
		}

		/// <summary>
		/// Inserts an entry, sifting it up to its ordered position.
		/// </summary>
		/// <param name="entry">The entry to insert.</param>
		private static void Push(Entry entry)
		{
			heap.Add(entry);

			int index = heap.Count - 1;
			while (index > 0)
			{
				int parent = (index - 1) / 2;
				if (heap[parent].DueUtc <= heap[index].DueUtc)
				{
					break;
				}

				Entry swap = heap[parent];
				heap[parent] = heap[index];
				heap[index] = swap;
				index = parent;
			}
		}

		/// <summary>
		/// Removes and returns the earliest entry, sifting the last entry down into its place.
		/// </summary>
		/// <returns>The entry with the earliest due time.</returns>
		private static Entry Pop()
		{
			Entry top = heap[0];

			int last = heap.Count - 1;
			heap[0] = heap[last];
			heap.RemoveAt(last);

			int index = 0;
			while (true)
			{
				int left = (2 * index) + 1;
				int right = left + 1;
				int smallest = index;

				if (left < heap.Count && heap[left].DueUtc < heap[smallest].DueUtc)
				{
					smallest = left;
				}
				if (right < heap.Count && heap[right].DueUtc < heap[smallest].DueUtc)
				{
					smallest = right;
				}
				if (smallest == index)
				{
					break;
				}

				Entry swap = heap[smallest];
				heap[smallest] = heap[index];
				heap[index] = swap;
				index = smallest;
			}

			return top;
		}
	}
}
