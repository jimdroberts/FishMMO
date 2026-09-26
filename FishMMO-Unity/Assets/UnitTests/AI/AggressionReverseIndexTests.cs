using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Shared.Core;
using FishMMO.UnitTests.Harness;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the dispatcher's reverse index — character ID to the NPCs whose tables track it —
	/// that heal and kill events are routed through (hot-path audit, 2026-09-25: M5).
	/// </summary>
	/// <remarks>
	/// Heal and kill events used to walk every registered NPC state: every scene instance on the
	/// process and every pooled, inactive NPC, for each heal-over-time tick. The index has to stay
	/// exact through every way a table gains or loses an entry, or an NPC stops hearing about a
	/// heal on the player it is fighting. These drive the tables directly, which is what their
	/// hooks report from; the handlers' own spawn guard needs a network object a fixture cannot
	/// stand up.
	/// </remarks>
	[TestFixture]
	public class AggressionReverseIndexTests
	{
		private const long PLAYER = 5001L;
		private const long HEALER = 5002L;

		private readonly List<AggressionState> found = new List<AggressionState>();

		[SetUp]
		public void SetUp()
		{
			AggressionDispatcher.Clear();
			found.Clear();
		}

		[TearDown]
		public void TearDown() => AggressionDispatcher.Clear();

		private static AggressionState NewNpc(long id)
		{
			return new AggressionState(new StubCharacter { ID = id });
		}

		private List<AggressionState> Trackers(long characterId)
		{
			found.Clear();
			AggressionDispatcher.CollectTrackers(characterId, found);
			return found;
		}

		[Test]
		public void AnEntry_IndexesItsNpc_UntilTheEntryGoes()
		{
			AggressionState orc = NewNpc(1);
			AggressionState wolf = NewNpc(2);

			Assert.AreEqual(0, Trackers(PLAYER).Count, "nobody tracks a player nobody has fought");

			orc.Controller.RecordDamage(PLAYER, 10);
			wolf.Controller.RecordDamage(PLAYER, 10);
			CollectionAssert.AreEquivalent(new[] { orc, wolf }, Trackers(PLAYER));

			orc.Controller.RemoveEntry(PLAYER);
			CollectionAssert.AreEqual(new[] { wolf }, Trackers(PLAYER), "a kill or a give-up removes one NPC");

			wolf.Clear();
			Assert.AreEqual(0, Trackers(PLAYER).Count, "a cleared table (leash, arrival, end of fight, pool) is out of the index");
		}

		[Test]
		public void AStaleEntry_LeavesTheIndex_WhenTheTableExpiresIt()
		{
			AggressionState orc = NewNpc(1);
			orc.Configure(1f, 0.6f, 5f, 1000f, 0f, 0f);

			orc.Controller.RecordDamage(PLAYER, 10);
			Assert.AreEqual(1, Trackers(PLAYER).Count);

			orc.Tick(1f);
			Assert.IsFalse(orc.HasAggression, "decayed to nothing past a zero timeout");
			Assert.AreEqual(0, Trackers(PLAYER).Count, "expiry must reach the index too");
		}

		[Test]
		public void AHealGathersBothParties_WithEachNpcOnce()
		{
			AggressionState orc = NewNpc(1);
			AggressionState wolf = NewNpc(2);
			AggressionState bear = NewNpc(3);

			orc.Controller.RecordDamage(PLAYER, 10);
			orc.Controller.RecordDamage(HEALER, 10);
			wolf.Controller.RecordDamage(HEALER, 10);

			found.Clear();
			AggressionDispatcher.CollectTrackers(PLAYER, found);
			AggressionDispatcher.CollectTrackers(HEALER, found);

			CollectionAssert.AreEquivalent(new[] { orc, wolf }, found,
				"the orc tracks both and is asked once; the bear tracks neither and is not asked");
			CollectionAssert.DoesNotContain(found, bear);
		}

		[Test]
		public void AnUnregisteredNpc_IsNeverIndexed()
		{
			AggressionState orc = NewNpc(1);
			orc.Controller.RecordDamage(PLAYER, 10);

			orc.Destroy();
			Assert.AreEqual(0, Trackers(PLAYER).Count, "destroying a state takes it out of the index");
			Assert.AreEqual(0, AggressionDispatcher.RegisteredCount);

			orc.Controller.RecordDamage(PLAYER, 10);
			Assert.AreEqual(0, Trackers(PLAYER).Count, "a table nothing dispatches to has nothing to be found for");
		}

		[Test]
		public void UnregisteringOne_KeepsTheOthersDispatchable()
		{
			List<AggressionState> npcs = new List<AggressionState>();
			for (int i = 0; i < 5; ++i)
			{
				AggressionState npc = NewNpc(100 + i);
				npc.Controller.RecordDamage(PLAYER, 10);
				npcs.Add(npc);
			}

			npcs[1].Destroy();
			npcs[3].Destroy();

			Assert.AreEqual(3, AggressionDispatcher.RegisteredCount, "unregistering drops exactly the two");
			CollectionAssert.AreEquivalent(new[] { npcs[0], npcs[2], npcs[4] }, Trackers(PLAYER));
		}

		[Test]
		public void HighestThreatSearch_ConsidersOnlyTheIndexedNpcs()
		{
			StubCharacter subject = new StubCharacter { ID = PLAYER };
			AggressionState orc = NewNpc(1);
			AggressionState wolf = NewNpc(2);
			NewNpc(3);

			orc.Controller.RecordDamage(PLAYER, 10);
			wolf.Controller.RecordDamage(PLAYER, 40);

			Assert.IsTrue(AggressionDispatcher.TryFindHighestThreatAgainst(subject, null, out ICharacter best));
			Assert.AreSame(wolf.Character, best);

			wolf.Clear();
			Assert.IsTrue(AggressionDispatcher.TryFindHighestThreatAgainst(subject, null, out best));
			Assert.AreSame(orc.Character, best);
		}
	}
}
