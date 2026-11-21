using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The combat report's coalescer and what a refused hit reports: the pins of the observer messaging proofs and the
	/// combat audits (2026-08-31 and their follow-ups) that drive the Combat module's types, moved out of the core fixtures
	/// with the damage controller (2026-11-20). The core fixtures keep the delivery rules and the source pins.
	/// </summary>
	[TestFixture]
	public class CombatEventCoalescerTests
	{
		#region Coalescing (observer messaging proofs)

		/// <summary>
		/// Past the entry cap the total is preserved rather than the overflow being dropped.
		/// </summary>
		/// <remarks>
		/// A wide area-of-effect tick can involve more distinct sources than the cap. Losing the
		/// attribution of the tail is acceptable; losing the damage from the displayed total is not,
		/// because the number would then disagree with the health bar beside it.
		/// </remarks>
		[Test]
		public void Coalescer_BeyondCapKeepsTheTotal()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			int expectedTotal = 0;
			for (int i = 1; i <= CombatEventCoalescer.MaxEntries + 5; ++i)
			{
				coalescer.Add(sourceObjectID: i, CombatEventKind.Damage, damageTemplateID: 1, amount: i);
				expectedTotal += i;
			}

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			Assert.LessOrEqual(flushed.Count, CombatEventCoalescer.MaxEntries);
			int total = 0;
			for (int i = 0; i < flushed.Count; ++i)
			{
				total += flushed[i].Amount;
			}
			Assert.AreEqual(expectedTotal, total, "Damage folded into the overflow bucket must still be reported.");
		}

		/// <summary>Nothing landed means nothing is queued.</summary>
		[Test]
		public void Coalescer_IgnoresNonPositiveAmounts()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(1, CombatEventKind.Damage, 0, 0);
			coalescer.Add(1, CombatEventKind.Damage, 0, -5);

			Assert.AreEqual(0, coalescer.Count);
		}

		/// <summary>Different sources, kinds and damage types stay separate numbers.</summary>
		[Test]
		public void Coalescer_KeepsDistinctSourcesKindsAndTypesApart()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(1, CombatEventKind.Damage, 3, 10);
			coalescer.Add(2, CombatEventKind.Damage, 3, 10);
			coalescer.Add(1, CombatEventKind.Damage, 4, 10);
			coalescer.Add(1, CombatEventKind.Heal, 0, 10);

			Assert.AreEqual(4, coalescer.Count);
		}

		/// <summary>Repeated hits from one source in a tick become a single number.</summary>
		/// <remarks>
		/// A multi-hit ability, or several damage-over-time stacks expiring on the same tick, would
		/// otherwise put one message on the wire per hit and stack that many labels on top of each
		/// other over the victim.
		/// </remarks>
		[Test]
		public void Coalescer_MergesSameSourceAndType()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(sourceObjectID: 7, CombatEventKind.Damage, damageTemplateID: 3, amount: 10);
			coalescer.Add(sourceObjectID: 7, CombatEventKind.Damage, damageTemplateID: 3, amount: 5);

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			Assert.AreEqual(1, flushed.Count);
			Assert.AreEqual(15, flushed[0].Amount);
			Assert.AreEqual(0, coalescer.Count, "Flush must empty the buffer so the next tick starts clean.");
		}

		/// <summary>
		/// Heals are normalised to no damage type, so two heals from one source always merge.
		/// </summary>
		[Test]
		public void Coalescer_NormalisesDamageTypeOnHeals()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(1, CombatEventKind.Heal, damageTemplateID: 3, amount: 4);
			coalescer.Add(1, CombatEventKind.Heal, damageTemplateID: 9, amount: 6);

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			Assert.AreEqual(1, flushed.Count);
			Assert.AreEqual(10, flushed[0].Amount);
			Assert.AreEqual(0, flushed[0].DamageTemplateID);
		}

		#endregion

		#region Occurrences (combat audit follow-up)

		/// <summary>A single unmerged hit reports one occurrence, not zero.</summary>
		/// <remarks>
		/// The overwhelmingly common case. Zero here would stop every ordinary hit confirming.
		/// </remarks>
		[Test]
		public void Coalescer_ASingleHit_ReportsOneOccurrence()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(7, CombatEventKind.Damage, 3, 10);

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			LogAssert.AreEqual(1, flushed.Count, "One hit is one entry.");
			LogAssert.AreEqual(1, flushed[0].Occurrences, "A lone hit stands for exactly itself.");
		}

		/// <summary>
		/// A merged entry counts its hits as well as summing their amounts.
		/// </summary>
		/// <remarks>
		/// The count is the whole fix. The server merges every hit sharing a (source, kind, type)
		/// within one tick into one message — right for display, and wrong for confirmation, because
		/// the caster drew one predicted label per hit. Without a count the report could only ever
		/// settle one of them.
		/// </remarks>
		[Test]
		public void Coalescer_CountsMergedHits_AsWellAsSummingThem()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(7, CombatEventKind.Damage, 3, 10);
			coalescer.Add(7, CombatEventKind.Damage, 3, 15);
			coalescer.Add(7, CombatEventKind.Damage, 3, 5);

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			LogAssert.AreEqual(1, flushed.Count, "Three hits of one kind and type merge into one entry.");
			LogAssert.AreEqual(30, flushed[0].Amount, "The amounts sum.");
			LogAssert.AreEqual(3, flushed[0].Occurrences,
				"The entry must record that it stands for THREE hits. The caster predicted three " +
				"labels; a report claiming one would settle one and leave the other two to grey " +
				"themselves out as denied, marking hits that landed as hits that were refused.");
		}

		/// <summary>
		/// Hits that differ in source, kind or damage type stay separate and each count themselves.
		/// </summary>
		/// <remarks>
		/// The merge key has to keep meaning what it meant, or the count would be measuring the
		/// wrong population. Two damage types from one attacker are two labels on screen and must
		/// remain two reports.
		/// </remarks>
		[Test]
		public void Coalescer_DistinctKeys_KeepSeparateCounts()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(7, CombatEventKind.Damage, 3, 10);
			coalescer.Add(7, CombatEventKind.Damage, 3, 10);
			coalescer.Add(7, CombatEventKind.Damage, 4, 10);
			coalescer.Add(9, CombatEventKind.Damage, 3, 10);
			coalescer.Add(7, CombatEventKind.Heal, 0, 10);

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			LogAssert.AreEqual(4, flushed.Count, "Four distinct (source, kind, type) keys.");
			int totalOccurrences = 0;
			for (int i = 0; i < flushed.Count; ++i)
			{
				totalOccurrences += flushed[i].Occurrences;
			}
			LogAssert.AreEqual(5, totalOccurrences,
				"Every hit added must be accounted for by exactly one entry's count — a hit whose " +
				"count is lost is a predicted label that never gets settled.");
		}

		#endregion

		#region Kinds and overflow (combat audit 2026-08-31 and its third round)

		/// <summary>
		/// A DoT tick and a direct hit of the same type from the same source must never merge:
		/// the kind is the coalescer's key and the prediction pairing's key, and folding them
		/// together is exactly what let a DoT report consume a projectile's pending prediction.
		/// </summary>
		[Test]
		public void Coalescer_PeriodicDamage_IsItsOwnKind()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(5, CombatEventKind.Damage, 7, 10);
			coalescer.Add(5, CombatEventKind.PeriodicDamage, 7, 4);

			LogAssert.AreEqual(2, coalescer.Count,
				"Same source, same type — but a direct hit and a DoT tick are different kinds and must not merge.");

			List<CombatEventCoalescer.Entry> entries = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(entries);
			foreach (CombatEventCoalescer.Entry entry in entries)
			{
				if (entry.Kind == CombatEventKind.PeriodicDamage)
				{
					LogAssert.AreEqual(7, entry.DamageTemplateID,
						"Periodic damage keeps its damage type — the client colours DoT numbers by it.");
				}
			}
		}

		/// <summary>Overflow prefers a same-kind host even when the damage type differs.</summary>
		[Test]
		public void Coalescer_Overflow_FoldsIntoASameKindHost()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			for (int source = 1; source <= CombatEventCoalescer.MaxEntries - 1; ++source)
			{
				coalescer.Add(source, CombatEventKind.Heal, 0, 10);
			}
			coalescer.Add(50, CombatEventKind.Damage, 5, 10);
			LogAssert.AreEqual(CombatEventCoalescer.MaxEntries, coalescer.Count, "Precondition: full, with one damage entry.");

			coalescer.Add(99, CombatEventKind.Damage, 6, 25);

			List<CombatEventCoalescer.Entry> entries = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(entries);
			bool foundDamageHost = false;
			foreach (CombatEventCoalescer.Entry entry in entries)
			{
				if (entry.Kind != CombatEventKind.Damage)
				{
					continue;
				}
				foundDamageHost = true;
				LogAssert.AreEqual(35, entry.Amount, "The overflow damage folds into the damage entry, not a heal.");
				LogAssert.AreEqual(2, entry.Occurrences, "Both damage hits are counted where damage predictions settle.");
			}
			LogAssert.IsTrue(foundDamageHost, "The damage host entry must survive the flush.");
		}

		/// <summary>
		/// Overflow used to fold into entry 0 whatever its kind, so a victim's ninth damage stream
		/// inflated a heal number and settled the wrong kind's predictions. With no same-kind host
		/// the hit is dropped instead.
		/// </summary>
		[Test]
		public void Coalescer_Overflow_NeverMergesDamageIntoAHeal()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			for (int source = 1; source <= CombatEventCoalescer.MaxEntries; ++source)
			{
				coalescer.Add(source, CombatEventKind.Heal, 0, 10);
			}
			LogAssert.AreEqual(CombatEventCoalescer.MaxEntries, coalescer.Count, "Precondition: the table is full of heals.");

			coalescer.Add(99, CombatEventKind.Damage, 5, 25);

			List<CombatEventCoalescer.Entry> entries = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(entries);
			int healTotal = 0;
			foreach (CombatEventCoalescer.Entry entry in entries)
			{
				LogAssert.AreEqual(CombatEventKind.Heal, entry.Kind, "No entry may change kind to absorb overflow.");
				healTotal += entry.Amount;
				LogAssert.AreEqual(1, entry.Occurrences, "No heal entry may count the dropped damage hit against heal predictions.");
			}
			LogAssert.AreEqual(10 * CombatEventCoalescer.MaxEntries, healTotal,
				"The damage overflow must be dropped, not folded into a heal number.");
		}

		#endregion

		#region Refusals (combat refusal reports)

		[Test]
		public void TheCoalescer_KeepsARefusal_WithAZeroAmount_AndCountsIt()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(7, CombatEventKind.Evade, 3, 0);
			coalescer.Add(7, CombatEventKind.Evade, 3, 250);
			coalescer.Add(7, CombatEventKind.Damage, 3, 0);

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			Assert.AreEqual(1, flushed.Count, "a zero-amount DAMAGE entry is still dropped; the refusal is kept");
			Assert.AreEqual(CombatEventKind.Evade, flushed[0].Kind);
			Assert.AreEqual(0, flushed[0].Amount, "a refusal cannot be made to carry an amount");
			Assert.AreEqual(3, flushed[0].DamageTemplateID, "the refused hit's type survives the merge");
			Assert.AreEqual(2, flushed[0].Occurrences, "both refused hits settle a prediction each");
		}

		[Test]
		public void TheCoalescer_NeverMergesARefusalIntoLandedDamage()
		{
			CombatEventCoalescer coalescer = new CombatEventCoalescer();
			coalescer.Add(7, CombatEventKind.Damage, 3, 40);
			coalescer.Add(7, CombatEventKind.Immune, 3, 0);

			List<CombatEventCoalescer.Entry> flushed = new List<CombatEventCoalescer.Entry>();
			coalescer.Flush(flushed);

			Assert.AreEqual(2, flushed.Count);
			foreach (CombatEventCoalescer.Entry entry in flushed)
			{
				if (entry.Kind == CombatEventKind.Damage)
				{
					Assert.AreEqual(40, entry.Amount);
					Assert.AreEqual(1, entry.Occurrences);
				}
				else
				{
					Assert.AreEqual(CombatEventKind.Immune, entry.Kind);
					Assert.AreEqual(0, entry.Amount);
				}
			}
		}

		[Test]
		public void WhatARefusedHitReports()
		{
			// immortal, evading, alive -> reported?, kind
			AssertRefusal(false, false, true, false, default);
			AssertRefusal(false, true, true, true, CombatEventKind.Evade);
			AssertRefusal(true, false, true, true, CombatEventKind.Immune);
			AssertRefusal(true, true, true, true, CombatEventKind.Evade);
			// A corpse is immortal only so it cannot die twice; a hit on it is not an immunity.
			AssertRefusal(true, false, false, false, default);
			AssertRefusal(false, true, false, false, default);
		}

		private static void AssertRefusal(bool immortal, bool evading, bool alive, bool reported, CombatEventKind kind)
		{
			bool result = CharacterDamageController.TryResolveRefusalReport(immortal, evading, alive, out CombatEventKind resolved);
			Assert.AreEqual(reported, result, $"immortal={immortal} evading={evading} alive={alive}");
			if (reported)
			{
				Assert.AreEqual(kind, resolved, $"immortal={immortal} evading={evading} alive={alive}");
			}
		}

		#endregion
	}
}
