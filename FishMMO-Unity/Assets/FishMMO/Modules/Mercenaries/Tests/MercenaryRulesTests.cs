using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Database.Data;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>The Mercenaries module's rules (issue #135): who may hire and return, the seat, the gear slots, the seat count.</summary>
	[TestFixture]
	public class MercenaryRulesTests
	{
		[Test]
		public void Hiring_IsOneAtATime_TheLeadersInAParty_AndPaidFor()
		{
			LogAssert.AreEqual(MercenaryResult.Done, MercenaryRules.CheckHire(false, 0, PartyRank.None, 500, 500), "no party: hiring forms one");
			LogAssert.AreEqual(MercenaryResult.Done, MercenaryRules.CheckHire(false, 7, PartyRank.Leader, 900, 500), "the leader hires into the party");
			LogAssert.AreEqual(MercenaryResult.NotLeader, MercenaryRules.CheckHire(false, 7, PartyRank.Member, 900, 500), "a member may not");
			LogAssert.AreEqual(MercenaryResult.AlreadyHaveOne, MercenaryRules.CheckHire(true, 0, PartyRank.None, 900, 500), "one at a time, resting or out");
			LogAssert.AreEqual(MercenaryResult.CannotAfford, MercenaryRules.CheckHire(false, 0, PartyRank.None, 499, 500), "the fee is paid in full");
			LogAssert.AreEqual(MercenaryResult.Done, MercenaryRules.CheckHire(false, 0, PartyRank.None, 0, 0), "a free kit needs no money");
		}

		[Test]
		public void Returning_NeedsARestingMercenary_AndTheLeadersSay()
		{
			LogAssert.AreEqual(MercenaryResult.NoMercenary, MercenaryRules.CheckReturn(false, false, 0, PartyRank.None));
			LogAssert.AreEqual(MercenaryResult.AlreadyOut, MercenaryRules.CheckReturn(true, false, 0, PartyRank.None));
			LogAssert.AreEqual(MercenaryResult.NotLeader, MercenaryRules.CheckReturn(true, true, 3, PartyRank.Member));
			LogAssert.AreEqual(MercenaryResult.Done, MercenaryRules.CheckReturn(true, true, 3, PartyRank.Leader));
			LogAssert.AreEqual(MercenaryResult.Done, MercenaryRules.CheckReturn(true, true, 0, PartyRank.None), "no party: returning forms one");
		}

		[Test]
		public void TheSeat_IsKeptOnlyInThePartyItWasTakenIn()
		{
			LogAssert.IsTrue(MercenaryRules.KeepsSeat(5, 5));
			LogAssert.IsFalse(MercenaryRules.KeepsSeat(5, 0), "the owner left: the seat went with the membership row");
			LogAssert.IsFalse(MercenaryRules.KeepsSeat(5, 6), "the owner joined another party");
			LogAssert.IsFalse(MercenaryRules.KeepsSeat(0, 0), "no seat is no seat");
		}

		[Test]
		public void APartyCountsMembersAndSeatedMercenaries()
		{
			var rows = new List<CharacterPartyData>
			{
				new CharacterPartyData(1, 1, 10, 3, 1, 1f),
				new CharacterPartyData(2, 1, 11, 3, 0, 1f, 77, "Brannoc"),
				new CharacterPartyData(3, 1, 12, 3, 0, 1f),
			};
			LogAssert.AreEqual(4, CharacterPartyData.CountSeats(rows), "three members and one mercenary take four seats");
			LogAssert.AreEqual(0, CharacterPartyData.CountSeats(null));
			LogAssert.AreEqual(rows[1].MercenaryKitID, rows[1].WithVersion(9).MercenaryKitID, "a version bump keeps the seat");
		}

		[Test]
		public void ACompanionSeat_IsKeyedByItsNegatedOwner()
		{
			long key = PartySeatCompanions.RosterKey(42);
			LogAssert.IsTrue(PartySeatCompanions.IsCompanionKey(key));
			LogAssert.IsFalse(PartySeatCompanions.IsCompanionKey(42), "a character ID is never a companion key");
			LogAssert.AreEqual(42L, PartySeatCompanions.OwnerOf(key));
			LogAssert.AreEqual(0L, PartySeatCompanions.OwnerOf(42));
		}

		[Test]
		public void GearGoesInTheSlotItsTemplateNames_AndNothingElseIsGear()
		{
			ArmorTemplate chest = ScriptableObject.CreateInstance<ArmorTemplate>();
			chest.Slot = ItemSlot.Chest;
			try
			{
				LogAssert.AreEqual((int)ItemSlot.Chest, MercenaryRules.GearSlotFor(chest));
				LogAssert.AreEqual(-1, MercenaryRules.GearSlotFor(null), "no template is not gear");
				LogAssert.AreEqual(Enum.GetNames(typeof(ItemSlot)).Length, MercenaryRules.GearSlotCount, "one gear slot per equipment socket");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(chest);
			}
		}

		[Test]
		public void AMercenary_WearsEverySocketButTheSaddle()
		{
			foreach (ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
			{
				LogAssert.AreEqual(slot != ItemSlot.MountSaddle, MercenaryRules.Wears(slot), $"{slot}");
			}
		}

		[Test]
		public void ASaddle_IsNotMercenaryGear()
		{
			ArmorTemplate saddle = ScriptableObject.CreateInstance<ArmorTemplate>();
			saddle.Slot = ItemSlot.MountSaddle;
			try
			{
				LogAssert.AreEqual(-1, MercenaryRules.GearSlotFor(saddle), "a saddle is not mercenary gear: giving one is refused");
				LogAssert.IsFalse(MercenaryRules.Fits(new Item(1, 0, saddle, 1), (int)ItemSlot.MountSaddle), "nor does it fit its own socket");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(saddle);
			}
		}

		[Test]
		public void EveryAnswerButDone_SaysSomething()
		{
			foreach (MercenaryResult result in Enum.GetValues(typeof(MercenaryResult)))
			{
				string text = MercenaryRules.Describe(result, "Brannoc");
				if (result == MercenaryResult.Done)
				{
					LogAssert.AreEqual(string.Empty, text);
				}
				else
				{
					LogAssert.IsFalse(string.IsNullOrWhiteSpace(text), $"{result} is explained to the player");
				}
			}
		}

		[Test]
		public void Names_AreTrimmedAndCut_ToTheColumn()
		{
			LogAssert.AreEqual("Mercenary", MercenaryRules.CleanName("  "));
			LogAssert.AreEqual("Brannoc", MercenaryRules.CleanName(" Brannoc "));
			LogAssert.AreEqual(MercenaryRules.MaxNameLength, MercenaryRules.CleanName(new string('x', 80)).Length);
		}

		[Test]
		public void ModuleContainers_CannotTakeACoreContainersPlace()
		{
			Assert.Throws<ArgumentException>(() => ModuleItemContainers.Register(InventoryType.Bank, c => null));
			ModuleItemContainers.Register(InventoryType.MercenaryEquipment, c => null);
			bool found = false;
			for (int i = 0; i < ModuleItemContainers.Count; ++i)
			{
				found |= ModuleItemContainers.TypeAt(i) == InventoryType.MercenaryEquipment;
			}
			LogAssert.IsTrue(found, "the mercenary gear is registered once");
			// Put the module's own registration back for whatever runs next.
			MercenariesModule.InstallSeams();
		}
	}
}
