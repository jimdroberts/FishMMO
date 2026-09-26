using System;
using System.Collections.Generic;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// Runs the respawn checks for every <see cref="SpawnerRuntime"/> that currently has something
	/// to respawn, from one per-frame call instead of one per spawner.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The interval a spawner polls on is still its own — see
	/// <see cref="SpawnerDefinition.RespawnCheckIntervalMinimum"/>. What changes here is which
	/// spawners are asked at all. A spawner with nothing pending, which is most of them in a world
	/// at rest, is not in the list and costs nothing whatsoever: not a check, not a gate comparison,
	/// and not an <c>Update</c> dispatch.
	/// </para>
	/// <para>
	/// <b>The list holds references, not scheduling state.</b> It is a plain
	/// <see cref="List{T}"/> of spawners with membership maintained by swap-removal, so a spawner
	/// occupies one reference while it has work and nothing at all when it does not. There is no
	/// ordering structure, no queued entries, and no per-spawner allocation. Measured against
	/// 100,000 spawners, whose own timer lists and spawned dictionaries come to roughly 107 MB, a
	/// membership list covering a tenth of them adds about 0.25 MB.
	/// </para>
	/// <para>
	/// One per <see cref="SpawnerHost"/>, ticked by <see cref="SpawnerSystem"/>'s per-frame update.
	/// It used to be static with a hidden driver object of its own; an instance is what lets two
	/// servers in one process (a simulation beside the real thing) keep their spawners apart.
	/// </para>
	/// <para>
	/// <b>The clock.</b> Every deadline and check interval its spawners keep is in seconds on
	/// <see cref="Now"/>, a monotonic clock. Respawn deadlines used to be <c>DateTime.UtcNow</c>,
	/// so the host's wall clock being stepped — an NTP correction, a VM resumed — moved every
	/// deadline at once: forward, and every camp in the world respawned on one frame; back, and
	/// none did for as long as the step.
	/// </para>
	/// </remarks>
	public sealed class SpawnerScheduler
	{
		/// <summary>
		/// The index stored on a spawner that is not currently in <see cref="active"/>.
		/// </summary>
		public const int NotActive = -1;

		/// <summary>
		/// Default for <see cref="SpawnsPerFrame"/>.
		/// </summary>
		public const int DefaultSpawnsPerFrame = 8;

		private readonly Func<double> clock;

		private int spawnsPerFrame = DefaultSpawnsPerFrame;

		/// <summary>
		/// Creates a scheduler on the process's monotonic clock, <see cref="MonotonicClock"/>.
		/// </summary>
		public SpawnerScheduler() : this(null)
		{
		}

		/// <summary>
		/// Creates a scheduler on a clock of the caller's choosing.
		/// </summary>
		/// <param name="clock">Seconds, never decreasing. Null uses the process's monotonic clock.</param>
		public SpawnerScheduler(Func<double> clock)
		{
			this.clock = clock ?? (() => MonotonicClock.NowSeconds);
		}

		/// <summary>
		/// Now, in seconds, on the clock every deadline of this scheduler's spawners is measured on.
		/// </summary>
		public double Now => clock();

		/// <summary>
		/// Most objects all of this scheduler's spawners together spawn in one <see cref="Tick"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A spawner drains every due timer when it is visited, which is right for its refill rate
		/// but puts a wiped camp's whole population — pooled retrieval, a ground cast, a NavMesh
		/// warp and a network spawn each — on one frame, and a raid clearing several camps at once
		/// on the same one. The budget spreads that over consecutive frames.
		/// </para>
		/// <para>
		/// <b>It cannot starve anyone.</b> A spawner cut short by the budget keeps the sweep's
		/// cursor and is first next frame, with its interval gate still open, so it carries on
		/// where it stopped rather than waiting out another interval. The first spawner of every
		/// frame therefore always has the whole budget, each such frame spends at least one of
		/// that spawner's finite due timers, and the cursor moves on as soon as it has none left.
		/// At least 1.
		/// </para>
		/// </remarks>
		public int SpawnsPerFrame
		{
			get => spawnsPerFrame;
			set => spawnsPerFrame = Math.Max(1, value);
		}

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
		private readonly List<SpawnerRuntime> active = new List<SpawnerRuntime>();

		/// <summary>Position of the rolling sweep within <see cref="active"/>.</summary>
		private int cursor;

		/// <summary>
		/// How many spawners currently have work outstanding. Exposed for tests and diagnostics.
		/// </summary>
		public int ActiveCount => active.Count;

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
		public void Refresh(SpawnerRuntime spawner)
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
		/// Called when a spawner's scene unloads, so it is not walked and its reference is not held.
		/// </remarks>
		/// <param name="spawner">The spawner to drop.</param>
		public void Unregister(SpawnerRuntime spawner)
		{
			Remove(spawner);
		}

		/// <summary>
		/// Gives a slice of the spawners with outstanding work the chance to run their respawn check.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The list is swept rather than walked: each frame advances a cursor through a fraction of
		/// it, so every active spawner is visited about once per <see cref="FramesPerSweep"/> frames
		/// instead of every frame. Their own interval gate is measured in seconds
		/// (<see cref="SpawnerDefinition.RespawnCheckIntervalMinimum"/>), so visiting more often than
		/// that buys nothing — it only moves the cost from the spawners that have work to the ones
		/// that are merely waiting, which is most of them.
		/// </para>
		/// <para>
		/// The clock is read once by the caller rather than by each spawner, so every spawner in
		/// a sweep judges its deadlines against the same instant.
		/// </para>
		/// <para>
		/// <b>Each spawner is isolated.</b> A pass that throws — a spawnable's <c>OnSpawned</c>, a
		/// brain that cannot be prepared, a network spawn — is reported to that spawner's own
		/// fault log and the sweep carries on. The spawner has already put its timer back and
		/// rolled back the half-made object (see <see cref="SpawnerRuntime.SpawnObject"/>), so it
		/// simply tries again at its next check. Before, the exception left the sweep, skipping
		/// every spawner after it, and did so again at every check of the broken one.
		/// </para>
		/// <para>
		/// Spawning stops for the frame once <see cref="SpawnsPerFrame"/> objects have been
		/// spawned; see that property for why that cannot starve a spawner.
		/// </para>
		/// </remarks>
		/// <param name="now">The current time on <see cref="Now"/>'s clock.</param>
		public void Tick(double now)
		{
			int count = active.Count;
			if (count < 1)
			{
				return;
			}

			// Round up, so a list shorter than the sweep still finishes inside one sweep.
			int slice = (count + FramesPerSweep - 1) / FramesPerSweep;
			int budget = spawnsPerFrame;

			for (int visited = 0; visited < slice && active.Count > 0; ++visited)
			{
				if (budget <= 0)
				{
					// Spent. The cursor stays on the next spawner, which is first in line next frame.
					break;
				}

				if (cursor >= active.Count)
				{
					cursor = 0;
				}

				SpawnerRuntime spawner = active[cursor];

				bool cutShort = false;
				try
				{
					cutShort = spawner.RunScheduledRespawn(now, ref budget);
					spawner.ReportPassSucceeded();
				}
				catch (Exception ex)
				{
					spawner.ReportPassFailed(ex, now);
				}

				/* The pass can change membership underneath the sweep: spawning fires callbacks
				 * that can despawn, and a spawner's scene can be unloaded from one. Trusting the slot
				 * afterwards would let the removal below drop whichever spawner had been swapped
				 * into it — which reads as one camp in a zone that simply never comes back. Re-read
				 * the spawner's own index instead; if it moved or left, this slot now holds an
				 * unvisited entry and the next iteration takes it. */
				if (spawner.SchedulerIndex != cursor)
				{
					continue;
				}

				if (cutShort)
				{
					// The budget ran out mid-pass. Keep the cursor here so this spawner finishes first.
					break;
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
		public void Clear()
		{
			for (int i = 0; i < active.Count; ++i)
			{
				active[i].SchedulerIndex = NotActive;
			}
			active.Clear();
			cursor = 0;
		}

		/// <summary>
		/// Adds a spawner to the active list if it is not already in it.
		/// </summary>
		/// <param name="spawner">The spawner to add.</param>
		private void Add(SpawnerRuntime spawner)
		{
			if (spawner.SchedulerIndex != NotActive)
			{
				return;
			}

			spawner.SchedulerIndex = active.Count;
			active.Add(spawner);
		}

		/// <summary>
		/// Removes a spawner from the active list if it is in it.
		/// </summary>
		/// <param name="spawner">The spawner to remove.</param>
		private void Remove(SpawnerRuntime spawner)
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
		private void RemoveAt(int index)
		{
			int last = active.Count - 1;

			SpawnerRuntime removed = active[index];
			removed.SchedulerIndex = NotActive;

			if (index != last)
			{
				SpawnerRuntime moved = active[last];
				active[index] = moved;
				moved.SchedulerIndex = index;
			}

			active.RemoveAt(last);
		}
	}
}
