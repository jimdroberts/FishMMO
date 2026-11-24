using System;
using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Prediction
{
	/// <summary>
	/// The server's NPC step schedule: one subscription steps every awake NPC, an idle unseen one naps out of the tick and
	/// is looked at once every <see cref="ServerStepSchedule.CheckTicks"/>, and a request wakes it at once.
	/// </summary>
	[TestFixture]
	public class ServerStepScheduleTests
	{
		private sealed class Member : IServerStepped
		{
			public int ScheduleSlot { get; set; } = ServerStepSchedule.Unscheduled;
			public int AwakeIndex { get; set; }
			public int NapPhase { get; set; }
			public bool Idle;
			public int PreTicks, Steps, Naps, Wakes;
			public Action DuringStep;

			public void ServerPreTick() => ++PreTicks;

			public bool ServerStep()
			{
				++Steps;
				DuringStep?.Invoke();
				return !Idle;
			}

			public void OnNapped() => ++Naps;
			public void OnWoken() => ++Wakes;
		}

		private uint tick;
		private ServerStepSchedule schedule;

		[SetUp]
		public void SetUp()
		{
			tick = 1000;
			schedule = new ServerStepSchedule(() => tick);
		}

		private void RunTick()
		{
			schedule.PreTick();
			schedule.Tick();
			++tick;
		}

		[Test]
		public void AwakeMembers_PreTickAndStep_EveryTick()
		{
			var a = new Member();
			var b = new Member();
			schedule.Add(a);
			schedule.Add(b);
			for (int i = 0; i < 5; i++)
			{
				RunTick();
			}
			LogAssert.AreEqual(5, a.PreTicks);
			LogAssert.AreEqual(5, a.Steps);
			LogAssert.AreEqual(5, b.Steps);
			LogAssert.AreEqual(2, schedule.AwakeCount);
		}

		[Test]
		public void AnIdleMember_Naps_AndIsLookedAtOncePerCycle()
		{
			var idle = new Member { Idle = true, NapPhase = 7 };
			schedule.Add(idle);
			RunTick();
			LogAssert.AreEqual(1, idle.Naps, "it naps after the step that reported it idle");
			LogAssert.AreEqual(0, schedule.AwakeCount);
			LogAssert.AreEqual(1, schedule.NappingCount);

			int stepsBefore = idle.Steps;
			for (int i = 0; i < ServerStepSchedule.CheckTicks * 3; i++)
			{
				RunTick();
			}
			LogAssert.AreEqual(stepsBefore + 3, idle.Steps, "one look per cycle, not one per tick");
			LogAssert.AreEqual(1, schedule.NappingCount, "still idle at each look: back to sleep");
		}

		[Test]
		public void TheLook_LandsOnTheMembersOwnPhase_SoNappersSpreadOverTheCycle()
		{
			var members = new List<Member>();
			for (int phase = 0; phase < ServerStepSchedule.CheckTicks; phase++)
			{
				var m = new Member { Idle = true, NapPhase = phase };
				members.Add(m);
				schedule.Add(m);
			}
			RunTick(); // every one naps
			for (int i = 0; i < ServerStepSchedule.CheckTicks; i++)
			{
				int before = 0;
				foreach (Member m in members)
				{
					before += m.Steps;
				}
				RunTick();
				int after = 0;
				foreach (Member m in members)
				{
					after += m.Steps;
				}
				LogAssert.AreEqual(1, after - before, "exactly one napper looked at per tick");
			}
		}

		[Test]
		public void Wake_StepsAMemberAgain_EveryTick()
		{
			var m = new Member { Idle = true, NapPhase = 3 };
			schedule.Add(m);
			RunTick();
			LogAssert.AreEqual(1, schedule.NappingCount);

			m.Idle = false;
			LogAssert.IsTrue(schedule.Wake(m));
			LogAssert.AreEqual(1, m.Wakes);
			LogAssert.IsFalse(schedule.Wake(m), "waking an awake member does nothing");
			int steps = m.Steps;
			for (int i = 0; i < 4; i++)
			{
				RunTick();
			}
			LogAssert.AreEqual(steps + 4, m.Steps);
			LogAssert.AreEqual(0, schedule.NappingCount);
		}

		[Test]
		public void AMemberWokenMidPass_StepsInTheSamePass()
		{
			var sleeper = new Member { Idle = true, NapPhase = 20 }; // not the bucket the next tick looks at
			var waker = new Member();
			schedule.Add(sleeper);
			schedule.Add(waker);
			RunTick();
			LogAssert.AreEqual(1, schedule.NappingCount);

			sleeper.Idle = false;
			waker.DuringStep = () => schedule.Wake(sleeper);
			int steps = sleeper.Steps;
			schedule.PreTick();
			schedule.Tick();
			LogAssert.AreEqual(steps + 1, sleeper.Steps, "appended during the pass, and stepped in it");
		}

		[Test]
		public void AMemberLeavingDuringItsOwnStep_DoesNotCostTheNextOneItsStep()
		{
			var a = new Member();
			var b = new Member();
			var c = new Member();
			schedule.Add(a);
			schedule.Add(b);
			schedule.Add(c);
			a.DuringStep = () => schedule.Remove(a);
			schedule.PreTick();
			schedule.Tick();
			LogAssert.AreEqual(1, b.Steps);
			LogAssert.AreEqual(1, c.Steps);
			LogAssert.AreEqual(2, schedule.AwakeCount);
			LogAssert.AreEqual(ServerStepSchedule.Unscheduled, a.ScheduleSlot);
		}

		[Test]
		public void RemovingANapper_TakesItOutOfItsBucket()
		{
			var m = new Member { Idle = true, NapPhase = 2 };
			schedule.Add(m);
			RunTick();
			schedule.Remove(m);
			LogAssert.AreEqual(0, schedule.NappingCount);
			int steps = m.Steps;
			for (int i = 0; i < ServerStepSchedule.CheckTicks * 2; i++)
			{
				RunTick();
			}
			LogAssert.AreEqual(steps, m.Steps, "a removed member is never stepped again");
		}

		[Test]
		public void NapThreshold_IsOneSecondOfIdleSteps()
		{
			LogAssert.IsFalse(CharacterPredictionController.ShouldNap(CharacterPredictionController.NapAfterIdleSteps - 1));
			LogAssert.IsTrue(CharacterPredictionController.ShouldNap(CharacterPredictionController.NapAfterIdleSteps));
		}
	}
}
