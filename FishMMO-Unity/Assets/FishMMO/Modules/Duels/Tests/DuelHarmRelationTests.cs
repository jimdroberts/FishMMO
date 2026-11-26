using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.UnitTests.Harness;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Who may strike whom in a duel, through the real <see cref="HarmRule"/>: nobody during the countdown, the two sides
	/// while it is fought, the finishing blow spared, and the alliance agreeing.
	/// </summary>
	[TestFixture]
	public class DuelHarmRelationTests
	{
		private readonly List<GameObject> hosts = new List<GameObject>();
		private PlayerCharacter first;
		private PlayerCharacter second;
		private PlayerCharacter bystander;

		private PlayerCharacter Make(long id)
		{
			GameObject host = new GameObject($"DuelProbe{id}");
			host.SetActive(false);
			hosts.Add(host);
			PlayerCharacter character = host.AddComponent<PlayerCharacter>();
			character.ID = id;
			return character;
		}

		[SetUp]
		public void SetUp()
		{
			// A PlayerCharacter stood up by hand brings its RequireComponents, and FishNet logs about them in edit mode.
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			DuelRegistry.Clear();
			HarmRule.Rediscover();
			first = Make(101);
			second = Make(202);
			bystander = Make(303);
		}

		[TearDown]
		public void TearDown()
		{
			DuelRegistry.Clear();
			foreach (GameObject host in hosts)
			{
				if (host != null)
				{
					Object.DestroyImmediate(host);
				}
			}
			hosts.Clear();
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		private static DuelHarmRelation Relation()
		{
			foreach (IHarmRelation relation in HarmRule.Relations)
			{
				if (relation is DuelHarmRelation duel)
				{
					return duel;
				}
			}
			return null;
		}

		[Test]
		public void TheRelation_IsInstalled_AheadOfCompanionsAndFactions()
		{
			DuelHarmRelation relation = Relation();
			LogAssert.IsNotNull(relation, "the Duels assembly declares its relation");
			LogAssert.IsTrue(relation.Order < CompanionHarmRelation.RelationOrder, "asked before companions, so partymates and guildmates may duel");
		}

		[Test]
		public void WithoutADuel_TheRelationHasNoSay()
		{
			LogAssert.AreEqual(HarmJudgement.Undecided, Relation().Judge(first, second), "no duel, no say");
			LogAssert.IsFalse(HarmRule.TrySpare(first, second, out _), "and no blow is spared");
			LogAssert.IsFalse(HarmRule.TryResolveAlliance(first, second, out _), "and the alliance is left to the factions");
		}

		[Test]
		public void DuringTheCountdown_NeitherMayStrike()
		{
			DuelRegistry.Publish(7, first.ID, second.ID, false);
			LogAssert.AreEqual(HarmJudgement.Refuse, Relation().Judge(first, second), "no jump on the start");
			LogAssert.IsFalse(HarmRule.MayHarm(first, second, out _), "the harm rule refuses");
			LogAssert.IsFalse(HarmRule.MayHarm(second, first, out _), "both ways");
			LogAssert.IsTrue(HarmRule.TryResolveAlliance(first, second, out FactionAllianceLevel level) && level == FactionAllianceLevel.Neutral,
				"and neither is anyone's enemy yet");
		}

		[Test]
		public void WhileFought_TheTwoMayStrike_AndTheFinishingBlowIsSpared()
		{
			DuelRegistry.Publish(7, first.ID, second.ID, true);
			LogAssert.IsTrue(HarmRule.MayHarm(first, second, out _), "the sides may harm each other");
			LogAssert.IsTrue(HarmRule.MayHarm(second, first, out _), "both ways");
			LogAssert.IsTrue(HarmRule.TrySpare(first, second, out IHarmRelation sparedBy) && sparedBy is DuelHarmRelation,
				"a blow that would kill is spared, by the duel");
			LogAssert.IsTrue(HarmRule.TryResolveAlliance(first, second, out FactionAllianceLevel level) && level == FactionAllianceLevel.Enemy,
				"they are each other's enemies, so targeting agrees");
		}

		[Test]
		public void ADuel_ChangesNothingForAnyoneElse()
		{
			DuelRegistry.Publish(7, first.ID, second.ID, true);
			LogAssert.AreEqual(HarmJudgement.Undecided, Relation().Judge(first, bystander), "a bystander is not in it");
			LogAssert.AreEqual(HarmJudgement.Undecided, Relation().Judge(bystander, second), "either way");
			LogAssert.IsFalse(HarmRule.TrySpare(bystander, second, out _), "and a bystander's blow is never spared");
		}

		[Test]
		public void AnNpcSharingADuellistsId_IsNeverTakenForThem()
		{
			// An NPC's id is not a character id; the two can coincide.
			DuelRegistry.Publish(7, first.ID, second.ID, true);
			StubCharacter npcLookingLikeFirst = new StubCharacter { ID = first.ID };
			StubCharacter npcLookingLikeSecond = new StubCharacter { ID = second.ID };
			LogAssert.AreEqual(HarmJudgement.Undecided, Relation().Judge(npcLookingLikeFirst, npcLookingLikeSecond), "only players duel");
			LogAssert.IsFalse(Relation().Spares(npcLookingLikeFirst, second), "nothing is spared for them");
		}

		[Test]
		public void TheSparedBlow_EndsTheDuel_ThroughTheRegistry()
		{
			DuelRegistry.Publish(7, first.ID, second.ID, true);
			long winner = 0, loser = 0;
			System.Action<long, long> listener = (w, l) => { winner = w; loser = l; };
			DuelRegistry.OnFinishingBlow += listener;
			try
			{
				LogAssert.IsTrue(HarmRule.TrySpare(first, second, out IHarmRelation sparedBy), "spared");
				HarmRule.NoteSpared(sparedBy, first, second);
			}
			finally
			{
				DuelRegistry.OnFinishingBlow -= listener;
			}
			LogAssert.AreEqual(first.ID, winner, "the striker wins");
			LogAssert.AreEqual(second.ID, loser, "the one left standing on a point loses");
		}

		[Test]
		public void TheRegistry_HoldsOneDuelPerCharacter()
		{
			DuelRegistry.Publish(7, first.ID, second.ID, true);
			DuelRegistry.Publish(8, first.ID, bystander.ID, false);
			LogAssert.IsFalse(DuelRegistry.TryGetOpponent(second.ID, out _, out _), "a newer duel replaces the old one for both its sides");
			LogAssert.IsTrue(DuelRegistry.TryGetOpponent(first.ID, out long opponent, out bool live) && opponent == bystander.ID && !live, "the new one stands");
			DuelRegistry.Remove(8);
			LogAssert.AreEqual(0, DuelRegistry.Count, "removing a duel removes both sides");
		}
	}
}
