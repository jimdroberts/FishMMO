using System;
using System.Collections.Generic;
using FishNet.Managing.Timing;

namespace FishMMO.Shared
{
	/// <summary>
	/// A character the server steps from a <see cref="ServerStepSchedule"/> rather than from its own tick subscription.
	/// </summary>
	internal interface IServerStepped
	{
		/// <summary>Where the schedule holds this member: <see cref="ServerStepSchedule.Unscheduled"/>,
		/// <see cref="ServerStepSchedule.Awake"/>, or the nap bucket it sleeps in. Written by the schedule only.</summary>
		int ScheduleSlot { get; set; }

		/// <summary>Index in the awake list while <see cref="ScheduleSlot"/> is <see cref="ServerStepSchedule.Awake"/>.
		/// Written by the schedule only.</summary>
		int AwakeIndex { get; set; }

		/// <summary>The nap bucket this member always sleeps in: a fixed phase, so a scene's NPCs spread over every tick.</summary>
		int NapPhase { get; }

		/// <summary>The member's pre-tick work.</summary>
		void ServerPreTick();

		/// <summary>The member's step. Returns false when it has been idle long enough to nap.</summary>
		bool ServerStep();

		/// <summary>Called when the schedule puts the member to sleep, after its step.</summary>
		void OnNapped();

		/// <summary>Called when the schedule wakes the member: by request, or for its periodic look.</summary>
		void OnWoken();
	}

	/// <summary>
	/// Steps the server's NPCs from one tick subscription, and lets an NPC that nobody sees and that has nothing to do
	/// sleep out of the tick altogether.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> Every NPC used to hold its own <c>OnPreTick</c> and <c>OnTick</c> subscriptions, so a loaded scene with
	/// nobody in it still called into every NPC twice a tick only to find it idle and unseen. With two world scenes
	/// loaded and empty, that plumbing was most of the scene server's main thread.
	/// </para>
	/// <para>
	/// <b>Napping.</b> A member whose step reports it idle (<see cref="IServerStepped.ServerStep"/> returns false) leaves
	/// the awake list for the nap bucket of its phase. Each tick, one bucket in <see cref="CheckTicks"/> is woken for one
	/// look, so a napping NPC is still looked at once a second; one still idle naps again at once. Anything that gives it
	/// work (an observer, combat, its brain asking for a cast) wakes it at once through <see cref="Wake"/>.
	/// </para>
	/// <para>
	/// <b>Membership changes during a pass</b> are safe: the awake list is walked by index, a member that leaves during its
	/// own step does not advance the walk, and one woken mid-pass is appended and stepped in the same pass.
	/// </para>
	/// </remarks>
	internal sealed class ServerStepSchedule
	{
		/// <summary><see cref="IServerStepped.ScheduleSlot"/> of a member not in any schedule.</summary>
		internal const int Unscheduled = -2;

		/// <summary><see cref="IServerStepped.ScheduleSlot"/> of a member stepped every tick.</summary>
		internal const int Awake = -1;

		/// <summary>Ticks between a napping member's looks: one second at 30 Hz.</summary>
		internal const int CheckTicks = 30;

		private static readonly Dictionary<TimeManager, ServerStepSchedule> schedules = new Dictionary<TimeManager, ServerStepSchedule>();

		private readonly List<IServerStepped> awake = new List<IServerStepped>();
		private readonly List<IServerStepped>[] napping = new List<IServerStepped>[CheckTicks];
		private readonly Func<uint> tickSource;

		/// <summary>Members put to sleep since the process started (for reports).</summary>
		public static long Naps;

		/// <summary>Members woken early by a request since the process started (for reports).</summary>
		public static long Wakes;

		internal ServerStepSchedule(Func<uint> tickSource)
		{
			this.tickSource = tickSource;
			for (int i = 0; i < CheckTicks; i++)
			{
				napping[i] = new List<IServerStepped>();
			}
		}

		/// <summary>The schedule for <paramref name="timeManager"/>, subscribed to its ticks on first use.</summary>
		public static ServerStepSchedule For(TimeManager timeManager)
		{
			if (timeManager == null)
			{
				return null;
			}
			if (!schedules.TryGetValue(timeManager, out ServerStepSchedule schedule))
			{
				schedule = new ServerStepSchedule(() => timeManager.LocalTick);
				schedules[timeManager] = schedule;
				timeManager.OnPreTick += schedule.PreTick;
				timeManager.OnTick += schedule.Tick;
			}
			return schedule;
		}

		/// <summary>Members stepped every tick.</summary>
		public int AwakeCount => awake.Count;

		/// <summary>Members asleep.</summary>
		public int NappingCount
		{
			get
			{
				int count = 0;
				for (int i = 0; i < CheckTicks; i++)
				{
					count += napping[i].Count;
				}
				return count;
			}
		}

		/// <summary>The bucket a member of <paramref name="phase"/> sleeps in.</summary>
		internal static int BucketFor(int phase) => (int)((uint)phase % CheckTicks);

		/// <summary>Adds a member, awake.</summary>
		public void Add(IServerStepped member)
		{
			if (member == null || member.ScheduleSlot != Unscheduled)
			{
				return;
			}
			AddAwake(member);
		}

		/// <summary>Removes a member wherever it is.</summary>
		public void Remove(IServerStepped member)
		{
			if (member == null)
			{
				return;
			}
			int slot = member.ScheduleSlot;
			if (slot == Awake)
			{
				RemoveAwakeAt(member.AwakeIndex);
			}
			else if (slot >= 0)
			{
				napping[slot].Remove(member);
			}
			member.ScheduleSlot = Unscheduled;
		}

		/// <summary>Wakes a napping member at once. Returns false when it was not napping.</summary>
		public bool Wake(IServerStepped member)
		{
			if (member == null || member.ScheduleSlot < 0)
			{
				return false;
			}
			napping[member.ScheduleSlot].Remove(member);
			AddAwake(member);
			++Wakes;
			member.OnWoken();
			return true;
		}

		/// <summary>The bucket due this tick wakes for its look, then every awake member's pre-tick runs.</summary>
		internal void PreTick()
		{
			List<IServerStepped> due = napping[(int)(tickSource() % CheckTicks)];
			for (int i = 0; i < due.Count; i++)
			{
				AddAwake(due[i]);
			}
			// Woken after the whole bucket is awake: OnWoken may not wake or remove anything here, but stay safe if it does.
			for (int i = due.Count - 1; i >= 0; i--)
			{
				IServerStepped member = due[i];
				due.RemoveAt(i);
				member.OnWoken();
			}

			for (int i = 0; i < awake.Count; i++)
			{
				awake[i].ServerPreTick();
			}
		}

		/// <summary>Every awake member steps; one that reports itself idle naps.</summary>
		internal void Tick()
		{
			uint tick = tickSource();
			for (int i = 0; i < awake.Count;)
			{
				IServerStepped member = awake[i];
				bool keepStepping = member.ServerStep();
				if (i >= awake.Count || !ReferenceEquals(awake[i], member))
				{
					// It left (or was moved) during its own step: whatever sits at i now has not stepped yet.
					continue;
				}
				if (keepStepping)
				{
					++i;
					continue;
				}
				RemoveAwakeAt(i);
				int bucket = BucketFor(member.NapPhase);
				member.ScheduleSlot = bucket;
				napping[bucket].Add(member);
				++Naps;
				member.OnNapped();
			}
		}

		private void AddAwake(IServerStepped member)
		{
			member.ScheduleSlot = Awake;
			member.AwakeIndex = awake.Count;
			awake.Add(member);
		}

		private void RemoveAwakeAt(int index)
		{
			if (index < 0 || index >= awake.Count)
			{
				return;
			}
			int last = awake.Count - 1;
			if (index != last)
			{
				IServerStepped moved = awake[last];
				awake[index] = moved;
				moved.AwakeIndex = index;
			}
			awake.RemoveAt(last);
		}
	}
}
