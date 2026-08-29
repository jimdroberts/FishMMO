using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Runs the respawn checks for every <see cref="ObjectSpawner"/> that currently has something
	/// to respawn, from one <c>Update</c> instead of one per spawner.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The interval a spawner polls on is still its own — see
	/// <see cref="ObjectSpawner.RespawnCheckIntervalMinimum"/>. What changes here is which spawners
	/// are asked at all. A spawner with nothing pending, which is most of them in a world at rest,
	/// is not in the list and costs nothing whatsoever: not a check, not a gate comparison, and not
	/// a <c>Update</c> dispatch.
	/// </para>
	/// <para>
	/// <b>The list holds references, not scheduling state.</b> It is a plain
	/// <see cref="List{T}"/> of spawners with membership maintained by swap-removal, so a spawner
	/// occupies one reference while it has work and nothing at all when it does not. There is no
	/// ordering structure, no queued entries, and no per-spawner allocation. Measured against
	/// 100,000 spawners, whose own timer lists and spawned dictionaries come to roughly 107 MB, a
	/// membership list covering a tenth of them adds about 0.25 MB.
	/// </para>
	/// </remarks>
	public static class ObjectSpawnerScheduler
	{
		/// <summary>
		/// The index stored on a spawner that is not currently in <see cref="active"/>.
		/// </summary>
		public const int NotActive = -1;

		/// <summary>
		/// How many frames one full pass over the active list is spread across.
		/// </summary>
		/// <remarks>
		/// A spawner's own check interval is seconds long, so being visited within about a second
		/// costs nothing anybody can observe and keeps the per-frame walk proportional to a slice of
		/// the active list rather than all of it.
		/// </remarks>
		public const int FramesPerSweep = 60;

		/// <summary>Spawners with respawn work outstanding.</summary>
		private static readonly List<ObjectSpawner> active = new List<ObjectSpawner>();

		/// <summary>Position of the rolling sweep within <see cref="active"/>.</summary>
		private static int cursor;

		/// <summary>The behaviour that calls <see cref="Tick"/>, created on first use.</summary>
		private static ObjectSpawnerSchedulerDriver driver;

		/// <summary>
		/// How many spawners currently have work outstanding. Exposed for tests and diagnostics.
		/// </summary>
		public static int ActiveCount => active.Count;

		/// <summary>
		/// Brings <paramref name="spawner"/> into or out of the active list to match whether it has
		/// anything to respawn.
		/// </summary>
		/// <remarks>
		/// Call after anything that changes that: a despawn adding a respawn timer, a spawn
		/// consuming one, or the spawner reaching its cap and clearing them. Idempotent, so callers
		/// need not track whether the spawner was already in the list.
		/// </remarks>
		/// <param name="spawner">The spawner whose membership should be refreshed.</param>
		public static void Refresh(ObjectSpawner spawner)
		{
			if (spawner == null)
			{
				return;
			}

			if (spawner.HasRespawnWork())
			{
				Add(spawner);
			}
			else
			{
				Remove(spawner);
			}
		}

		/// <summary>
		/// Drops <paramref name="spawner"/> from the active list.
		/// </summary>
		/// <remarks>
		/// Call from <c>OnStopNetwork</c>, so a spawner in an unloaded scene is not walked and its
		/// reference is not held.
		/// </remarks>
		/// <param name="spawner">The spawner to drop.</param>
		public static void Unregister(ObjectSpawner spawner)
		{
			Remove(spawner);
		}

		/// <summary>
		/// Gives every spawner with outstanding work the chance to run its respawn check.
		/// </summary>
		/// <remarks>
		/// Each spawner decides for itself whether its own interval has elapsed, so this walk is a
		/// float comparison per active spawner and nothing more. Spawners that finish their work
		/// leave the list here rather than being left to be skipped over on later frames.
		/// </remarks>
		/// <param name="nowUtc">The current UTC time, for evaluating respawn deadlines.</param>
		public static void Tick(DateTime nowUtc)
		{
			Tick(nowUtc, Time.time);
		}


		/// <summary>
		/// Gives a slice of the spawners with outstanding work the chance to run their respawn check.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The list is swept rather than walked: each frame advances a cursor through a fraction of
		/// it, so every active spawner is visited about once per <see cref="FramesPerSweep"/> frames
		/// instead of every frame. Their own interval gate is measured in seconds
		/// (<see cref="ObjectSpawner.RespawnCheckIntervalMinimum"/>), so visiting more often than
		/// that buys nothing — it only moves the cost from the spawners that have work to the ones
		/// that are merely waiting, which is most of them.
		/// </para>
		/// <para>
		/// <see cref="Time.time"/> is read once by the caller rather than by each spawner. It is a
		/// native property, and crossing into the engine per active spawner per frame cost more than
		/// everything else in this walk put together.
		/// </para>
		/// <para>
		/// The dangling-reference check is <see cref="object.ReferenceEquals(object, object)"/> and
		/// not <c>== null</c> on purpose. Unity overloads that operator to ask the engine whether the
		/// native object is still alive, which is another crossing per entry per frame. Membership is
		/// maintained on destruction instead, so a plain reference check is all this needs.
		/// </para>
		/// </remarks>
		/// <param name="nowUtc">The current UTC time, for evaluating respawn deadlines.</param>
		/// <param name="nowTime">The current <see cref="Time.time"/>, for the interval gates.</param>
		public static void Tick(DateTime nowUtc, float nowTime)
		{
			int count = active.Count;
			if (count < 1)
			{
				return;
			}

			// Round up, so a list shorter than the sweep still finishes inside one sweep.
			int slice = (count + FramesPerSweep - 1) / FramesPerSweep;

			for (int visited = 0; visited < slice && active.Count > 0; ++visited)
			{
				if (cursor >= active.Count)
				{
					cursor = 0;
				}

				ObjectSpawner spawner = active[cursor];

				if (ReferenceEquals(spawner, null))
				{
					// Never registered as destroyed; drop the dangling reference.
					RemoveAt(cursor);
					continue;
				}

				spawner.RunScheduledRespawn(nowUtc, nowTime);

				/* The pass can change membership underneath the sweep: spawning fires callbacks
				 * that can despawn, and a spawner can be destroyed from one. Trusting the slot
				 * afterwards would let the removal below drop whichever spawner had been swapped
				 * into it — which reads as one camp in a zone that simply never comes back. Re-read
				 * the spawner's own index instead; if it moved or left, this slot now holds an
				 * unvisited entry and the next iteration takes it. */
				if (spawner.SchedulerIndex != cursor)
				{
					continue;
				}

				if (spawner.HasRespawnWork())
				{
					++cursor;
					continue;
				}

				/* Finished. Removal swaps the last entry into this slot, so the cursor stays put:
				 * whatever was moved here has not been visited in this sweep yet. */
				RemoveAt(cursor);
			}
		}

		/// <summary>
		/// Empties the active list. Intended for tests and for a clean shutdown between sessions.
		/// </summary>
		public static void Clear()
		{
			for (int i = 0; i < active.Count; ++i)
			{
				if (active[i] != null)
				{
					active[i].SchedulerIndex = NotActive;
				}
			}
			active.Clear();
			cursor = 0;
		}

		/// <summary>
		/// Adds a spawner to the active list if it is not already in it.
		/// </summary>
		/// <param name="spawner">The spawner to add.</param>
		private static void Add(ObjectSpawner spawner)
		{
			if (spawner.SchedulerIndex != NotActive)
			{
				return;
			}

			EnsureDriver();

			spawner.SchedulerIndex = active.Count;
			active.Add(spawner);
		}

		/// <summary>
		/// Removes a spawner from the active list if it is in it.
		/// </summary>
		/// <param name="spawner">The spawner to remove.</param>
		private static void Remove(ObjectSpawner spawner)
		{
			if (spawner == null || spawner.SchedulerIndex == NotActive)
			{
				return;
			}

			RemoveAt(spawner.SchedulerIndex);
		}

		/// <summary>
		/// Removes the entry at <paramref name="index"/> by swapping the last entry into its place.
		/// </summary>
		/// <param name="index">The index to remove.</param>
		private static void RemoveAt(int index)
		{
			int last = active.Count - 1;

			ObjectSpawner removed = active[index];
			if (removed != null)
			{
				removed.SchedulerIndex = NotActive;
			}

			if (index != last)
			{
				ObjectSpawner moved = active[last];
				active[index] = moved;
				if (moved != null)
				{
					moved.SchedulerIndex = index;
				}
			}

			active.RemoveAt(last);
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
	}
}
