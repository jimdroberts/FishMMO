using System.Collections.Generic;
using FishMMO.Shared;
using FishNet.Serializing;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using UnityLogAssert = UnityEngine.TestTools.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regressions for the defects the 2026-08-29 follow-up audit found: a coalesced combat report
	/// settling only one of the predictions it stood for, two <see cref="ModifierSource"/> keys that
	/// collided with themselves, an unframed reconcile snapshot, and a non-atomic item id counter.
	/// </summary>
	/// <remarks>
	/// Each of these is a case where the shipped code produced a plausible-looking wrong answer
	/// rather than an error, so the tests assert the arithmetic rather than the absence of a throw.
	/// </remarks>
	[TestFixture]
	public class CombatAuditFollowUpTests
	{
		#region Coalesced reports settle every prediction they stand for.

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

		/// <summary>
		/// <see cref="CombatEventBroadcast.Occurrences"/> survives the wire.
		/// </summary>
		[Test]
		public void CombatEventBroadcast_RoundTripsOccurrences()
		{
			CombatEventBroadcast sent = new CombatEventBroadcast()
			{
				TargetObjectID = 11,
				SourceObjectID = 22,
				Amount = 33,
				Kind = (byte)CombatEventKind.Damage,
				DamageTemplateID = 44,
				Occurrences = 3,
			};

			Writer writer = new Writer();
			writer.WriteCombatEventBroadcast(sent);
			Reader reader = new Reader(writer.GetArraySegment(), null);
			CombatEventBroadcast received = reader.ReadCombatEventBroadcast();

			LogAssert.AreEqual(sent.TargetObjectID, received.TargetObjectID, "Target survives.");
			LogAssert.AreEqual(sent.SourceObjectID, received.SourceObjectID, "Source survives.");
			LogAssert.AreEqual(sent.Amount, received.Amount, "Amount survives.");
			LogAssert.AreEqual(sent.DamageTemplateID, received.DamageTemplateID, "Damage type survives.");
			LogAssert.AreEqual(3, received.Occurrences, "The occurrence count survives.");
		}

		/// <summary>
		/// A message written with a default (zero) count reads back as one.
		/// </summary>
		/// <remarks>
		/// Confirming zero predictions would grey out a hit that landed, which is the exact failure
		/// the field was added to remove — so the clamp is on both the write and the read rather
		/// than trusting every producer to remember.
		/// </remarks>
		[Test]
		public void CombatEventBroadcast_DefaultOccurrences_ReadsAsOne()
		{
			CombatEventBroadcast sent = new CombatEventBroadcast()
			{
				TargetObjectID = 1,
				SourceObjectID = 2,
				Amount = 5,
				Kind = (byte)CombatEventKind.Heal,
			};

			Writer writer = new Writer();
			writer.WriteCombatEventBroadcast(sent);
			Reader reader = new Reader(writer.GetArraySegment(), null);

			LogAssert.AreEqual(1, reader.ReadCombatEventBroadcast().Occurrences,
				"A producer that leaves Occurrences default must still settle the one prediction its " +
				"hit produced, rather than settling none.");
		}

		#endregion

		#region Modifier source keys no longer collide with themselves.

		/// <summary>
		/// A dungeon's sheet-wide resource multiplier and a named per-template scalar are two
		/// contributors, not one.
		/// </summary>
		/// <remarks>
		/// Both used to be keyed <c>DungeonScaling</c> with id zero, and <c>SetSource</c> STATES a
		/// contribution rather than adding to it — so a resource singled out for extra scaling
		/// silently replaced the group multiplier and came out weaker than a resource nobody had
		/// mentioned. The in-code comment asserted the opposite ("scaled twice, and that is
		/// intended"), which is what makes this a defect rather than a design choice.
		/// </remarks>
		[Test]
		public void DungeonScaling_GroupAndNamedEntries_Compound()
		{
			CharacterAttribute attribute = MakeLedgerAttribute();

			// Base 100. Group multiplier x2 contributes +100; a named x1.5 on the same template +50.
			attribute.SetSource(ModifierSource.DungeonScaling(), 100);
			attribute.SetSource(ModifierSource.DungeonScaling(templateID: 77), 50);

			LogAssert.AreEqual(2, attribute.ModifierSourceCount,
				"The sheet-wide multiplier and the named scalar are separate contributors.");
			LogAssert.AreEqual(150, attribute.ExternalModifier,
				"They must ADD. Sharing one key made the named entry replace the group one, so a " +
				"boss singled out for extra health ended up with less than its packmates.");
		}

		/// <summary>
		/// Two entries of an NPC's authored bonus list do not collide, whatever they name.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>AttributeBonuses</c> is an authored list and nothing stops it naming one template
		/// twice — a designer splitting a roll into a flat part and a scalar part, say. Keying the
		/// contribution by the TEMPLATE was introduced to fix that and did not: two entries naming
		/// one template produce one key, so the second still overwrote the first and half the roll
		/// still vanished. The entry's position in the list is the part that differs, and it is now
		/// half of the key.
		/// </para>
		/// <para>
		/// The second case below is the one that regressed, and it is the case the previous fix's
		/// own comment described.
		/// </para>
		/// </remarks>
		[Test]
		public void NpcBonus_IsKeyedPerTemplateAndPerEntry()
		{
			CharacterAttribute attribute = MakeLedgerAttribute();

			// Different templates, different entries — the case that already worked.
			attribute.SetSource(ModifierSource.NpcBonus(1, 0), 10);
			attribute.SetSource(ModifierSource.NpcBonus(2, 1), 20);

			LogAssert.AreEqual(2, attribute.ModifierSourceCount, "Two named bonuses are two entries.");
			LogAssert.AreEqual(30, attribute.ExternalModifier, "And they sum.");

			attribute.ClearSource(ModifierSource.NpcBonus(1, 0));
			LogAssert.AreEqual(20, attribute.ExternalModifier,
				"Releasing one named bonus must leave the other, which is the point of keying them.");

			// The SAME template named twice — a flat roll and a scalar roll on one attribute.
			CharacterAttribute split = MakeLedgerAttribute();
			split.SetSource(ModifierSource.NpcBonus(7, 0), 10);
			split.SetSource(ModifierSource.NpcBonus(7, 1), 25);

			LogAssert.AreEqual(2, split.ModifierSourceCount,
				"One template named twice is TWO contributions. Keyed by the template alone they " +
				"share a key, and SetSource states a contribution rather than adding to one — so " +
				"the second silently replaced the first.");
			LogAssert.AreEqual(35, split.ExternalModifier,
				"Both halves of the designer's roll must land.");
		}

		/// <summary>
		/// The packing that separates two entries naming one template does not bleed between them.
		/// </summary>
		/// <remarks>
		/// Template and index share one 64-bit id, template in the high word. A negative index or a
		/// template large enough to overlap would make two distinct entries collide again, which is
		/// invisible until a designer writes the list that triggers it.
		/// </remarks>
		[Test]
		public void NpcBonus_PackingKeepsTemplateAndEntryDistinct()
		{
			LogAssert.AreNotEqual(ModifierSource.NpcBonus(1, 0), ModifierSource.NpcBonus(0, 1),
				"Template 1 entry 0 and template 0 entry 1 must not pack to the same id.");
			LogAssert.AreNotEqual(ModifierSource.NpcBonus(2, 0), ModifierSource.NpcBonus(1, int.MaxValue),
				"A large index must not carry into the template's half of the key.");
			LogAssert.AreEqual(ModifierSource.NpcBonus(9, 3), ModifierSource.NpcBonus(9, 3),
				"The same template and entry must key the same contribution, or nothing can release it.");
		}

		/// <summary>
		/// The kinds still separate ids drawn from different spaces.
		/// </summary>
		/// <remarks>
		/// Giving <c>DungeonScaling</c> and <c>NpcBonus</c> real ids puts them in the same position
		/// as <c>Item</c> and <c>Buff</c>: small positive numbers from unrelated spaces. The KIND is
		/// what stops template 5's dungeon scaling from overwriting buff 5's contribution.
		/// </remarks>
		[Test]
		public void ModifierKinds_SeparateIdenticalIds()
		{
			CharacterAttribute attribute = MakeLedgerAttribute();

			attribute.SetSource(ModifierSource.DungeonScaling(5), 1);
			attribute.SetSource(ModifierSource.NpcBonus(5, 0), 2);
			attribute.SetSource(ModifierSource.Buff(5), 4);
			attribute.SetSource(ModifierSource.Item(5), 8);
			attribute.SetSource(ModifierSource.Region(5), 16);

			LogAssert.AreEqual(5, attribute.ModifierSourceCount,
				"Five kinds sharing id 5 are five contributors; the kind is half of the key.");
			LogAssert.AreEqual(31, attribute.ExternalModifier, "And all five sum.");
		}

		#endregion

		#region The reconcile snapshot is framed.

		/// <summary>
		/// An absolute reconcile snapshot round-trips and leaves the reader exactly at its end.
		/// </summary>
		/// <remarks>
		/// The frame exists so a rejected array count cannot misalign the shared <c>StateUpdate</c>
		/// reader for the behaviours after this one. That only works if the ordinary path consumes
		/// the frame exactly, so the trailing sentinel below is the real assertion.
		/// </remarks>
		[Test]
		public void ReconcileSnapshot_IsFramed_AndConsumedExactly()
		{
			CharacterReconcileData data = new CharacterReconcileData()
			{
				AbilityID = 7,
				RemainingTicks = 11,
				Seed = 13,
				PackedFlagsAndSlot = CharacterReconcileData.Pack(3, 5),
				RngS0 = 1, RngS1 = 2, RngS2 = 3, RngS3 = 4,
				Sequence = 9,
				Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 1, StartTick = 2, DurationTicks = 3 } },
			};

			Writer writer = new Writer();
			writer.WriteCharacterReconcileData(data);
			// A sentinel standing in for whatever behaviour reads next out of the shared reader.
			writer.WriteInt32(SnapshotSentinel);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterReconcileData read = reader.ReadCharacterReconcileData();

			LogAssert.AreEqual(data.AbilityID, read.AbilityID, "AbilityID survives the frame.");
			LogAssert.AreEqual(data.Seed, read.Seed, "Seed survives the frame.");
			LogAssert.AreEqual(data.Sequence, read.Sequence, "The chain sequence survives the frame.");
			LogAssert.AreEqual(1, read.Cooldowns.Length, "The cooldown array survives the frame.");

			LogAssert.AreEqual(SnapshotSentinel, reader.ReadInt32(),
				"The reader must sit exactly at the end of the framed snapshot, so the next " +
				"behaviour in the shared StateUpdate reader decodes from the right offset.");
		}

		/// <summary>
		/// A snapshot whose array count cannot be trusted still leaves the reader at the frame end.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>This is what the frame is for.</b> The clean round trip above passes with or without it
		/// — an unframed writer and an unframed reader agree perfectly right up until something goes
		/// wrong. The failure the frame prevents only appears on the abort path: the reader rejects a
		/// count it cannot trust, and has no way to drain past it because the per-entry sizes it
		/// would need to skip are derived from the very count it just rejected. Before the frame it
		/// bare-returned, and every predicted behaviour after this one in the same
		/// <c>StateUpdate</c> decoded from the wrong offset.
		/// </para>
		/// <para>
		/// The corrupt count is planted by writing a valid snapshot and overwriting the cooldown
		/// count in place, so the frame length stays honest — which is exactly the situation a
		/// truncated or version-skewed sender produces.
		/// </para>
		/// </remarks>
		[Test]
		public void ReconcileSnapshot_AbortingOnABadCount_StillLeavesTheReaderAtTheFrameEnd()
		{
			CharacterReconcileData data = new CharacterReconcileData()
			{
				AbilityID = 7,
				Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 1, StartTick = 2, DurationTicks = 3 } },
			};

			Writer writer = new Writer();
			writer.WriteCharacterReconcileData(data);
			writer.WriteInt32(SnapshotSentinel);

			byte[] bytes = new byte[writer.Length];
			System.Array.Copy(writer.GetArraySegment().Array, writer.GetArraySegment().Offset, bytes, 0, writer.Length);

			/* Find the cooldown count — the first ushort of 1 after the fixed-size head — and drive it
			 * past MaxArrayEntries so IsValidArrayCount rejects it. Located by writing a second
			 * snapshot with an empty array and diffing, which avoids hard-coding a field layout this
			 * test has no business knowing. */
			Writer empty = new Writer();
			empty.WriteCharacterReconcileData(new CharacterReconcileData() { AbilityID = 7 });
			byte[] emptyBytes = new byte[empty.Length];
			System.Array.Copy(empty.GetArraySegment().Array, empty.GetArraySegment().Offset, emptyBytes, 0, empty.Length);

			int countOffset = -1;
			for (int i = 0; i + 1 < emptyBytes.Length && i + 1 < bytes.Length; ++i)
			{
				if (bytes[i] == 1 && bytes[i + 1] == 0 && emptyBytes[i] == 0 && emptyBytes[i + 1] == 0)
				{
					countOffset = i;
					break;
				}
			}
			LogAssert.IsTrue(countOffset >= 0, "Could not locate the cooldown count to corrupt.");

			// 0xFFFF — far past the writer's 4096 cap, so the reader must reject it.
			bytes[countOffset] = 0xFF;
			bytes[countOffset + 1] = 0xFF;

			/* The reject path logs an error by design — that is the "say so once instead of failing
			 * invisibly" half of the contract — so the runner is told not to fail on it. */
			UnityLogAssert.ignoreFailingMessages = true;
			Reader reader = new Reader(new System.ArraySegment<byte>(bytes), null);
			try
			{
				reader.ReadCharacterReconcileData();
			}
			finally
			{
				UnityLogAssert.ignoreFailingMessages = false;
			}

			LogAssert.AreEqual(SnapshotSentinel, reader.ReadInt32(),
				"After rejecting an untrustworthy array count the reader must seek to the end of the " +
				"frame, so the behaviour that reads next still decodes from the right offset. Without " +
				"the frame the abort left it mid-snapshot and corrupted every later behaviour.");
		}

		#endregion

		#region One item identity.

		/// <summary>
		/// An item's identity is issued once and cannot be reassigned.
		/// </summary>
		/// <remarks>
		/// The id keys the item's contribution to a character's attributes. Moving it while a
		/// contribution is live would leave the old entry unreleasable — the unequip would look for
		/// the new key, find nothing, and the bonus would stay applied for the rest of the session.
		/// Refusing is the only safe answer, so it is refused rather than accommodated.
		/// </remarks>
		[Test]
		public void ItemIdentity_IsIssuedOnceAndCannotBeReassigned()
		{
			Item item = new Item(itemTemplate, 1u);
			LogAssert.AreEqual(0L, item.ID,
				"An item built at runtime has no identity until the database issues one.");

			LogAssert.IsTrue(item.AssignPersistentID(4242L),
				"The first identity must be accepted.");
			LogAssert.AreEqual(4242L, item.ID, "The issued identity must stick.");

			LogAssert.IsFalse(item.AssignPersistentID(4242L),
				"Re-applying the SAME identity is a no-op, not a change.");
			LogAssert.AreEqual(4242L, item.ID, "A no-op must not move the id.");

			/* The refusal is reported through FishMMO.Logging, which does not route into Unity's log
			 * in EditMode, so there is no Expect to pair with it. The return value and the unchanged
			 * id are the contract; the log line is for whoever has to work out why. */
			LogAssert.IsFalse(item.AssignPersistentID(9999L),
				"Reassigning a live identity must be refused.");
			LogAssert.AreEqual(4242L, item.ID, "A refused reassignment must leave the id alone.");

			LogAssert.IsFalse(item.AssignPersistentID(0L),
				"Zero is the unassigned sentinel and is never an identity to issue.");
			LogAssert.IsFalse(item.AssignPersistentID(-1L),
				"A negative identity is not one the database can have issued.");
		}

		/// <summary>
		/// A generated item's seed follows its identity, so its attributes survive a relog.
		/// </summary>
		/// <remarks>
		/// <c>Item.Initialize</c> derives a generated item's seed from its id. An item built at
		/// runtime has no id, so it used to roll from seed 0 — and the reload after logout derived a
		/// real seed from the id the database had meanwhile assigned and rolled a DIFFERENT set of
		/// attributes. The player's looted weapon changed its stats overnight. Deriving the seed when
		/// the identity arrives makes the two agree.
		/// </remarks>
		[Test]
		public void ItemSeed_FollowsTheIdentity_SoAttributesSurviveAReload()
		{
			// What the item will look like after a reload: constructed straight from its id.
			Item reloaded = new Item(4242L, seed: 0, itemTemplate, 1u);

			// What it looks like in the session it was created in.
			Item created = new Item(itemTemplate, 1u);
			created.AssignPersistentID(4242L);

			LogAssert.AreEqual(Item.DeriveSeed(4242L), Item.DeriveSeed(4242L),
				"The derivation must be a pure function of the identity.");
			LogAssert.AreNotEqual(0, Item.DeriveSeed(4242L),
				"A real identity must produce a real seed, or every item generates from zero.");
			LogAssert.AreEqual(0, Item.DeriveSeed(0L),
				"There is nothing to derive from an unassigned identity.");

			if (created.IsGenerated && reloaded.IsGenerated)
			{
				LogAssert.AreEqual(reloaded.Generator.Seed, created.Generator.Seed,
					"An item created this session and the same item reloaded next session must roll " +
					"from the same seed, or its attributes change behind the player's back.");
			}
		}

		/// <summary>
		/// Assigning an identity re-keys a contribution the item has already applied.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the case that makes the single identity safe. An item can be equipped before its
		/// first persist returns — loot a sword, equip it, and the write-back lands a moment later —
		/// so its ledger entry is written under key zero. Moving the field alone would strand it:
		/// the release on unequip looks for the new key and finds nothing.
		/// </para>
		/// <para>
		/// Asserted on the ledger rather than on the total, because the total is the thing that
		/// stays the same. A stranded entry is invisible until the item is removed, which is exactly
		/// why it needs a test rather than a look.
		/// </para>
		/// </remarks>
		[Test]
		public void AssigningAnIdentity_ReKeysALiveContribution()
		{
			CharacterAttribute attribute = MakeLedgerAttribute();

			// The contribution an unwritten item applies: keyed by the zero identity.
			attribute.SetSource(ModifierSource.Item(0L), 25);
			LogAssert.AreEqual(25, attribute.ExternalModifier,
				"The unwritten item's bonus must be applied.");
			LogAssert.AreEqual(25, attribute.GetSourceValue(ModifierSource.Item(0L)),
				"...and recorded against the zero identity.");

			// What AssignPersistentID does around the field write: release, then restate.
			attribute.ClearSource(ModifierSource.Item(0L));
			attribute.SetSource(ModifierSource.Item(4242L), 25);

			LogAssert.AreEqual(25, attribute.ExternalModifier,
				"The re-key must not move the character's total — the same item is still equipped.");
			LogAssert.AreEqual(0, attribute.GetSourceValue(ModifierSource.Item(0L)),
				"The entry under the old key must be gone, or nothing can ever release it.");
			LogAssert.AreEqual(25, attribute.GetSourceValue(ModifierSource.Item(4242L)),
				"...and it must be recorded under the identity the item now has.");

			// The release the unequip will issue now finds it.
			attribute.ClearSource(ModifierSource.Item(4242L));
			LogAssert.AreEqual(0, attribute.ExternalModifier,
				"Unequipping after the re-key must release the bonus.");
			LogAssert.AreEqual(0, attribute.ModifierSourceCount,
				"...and leave no contributor behind.");
		}

		#endregion

		// ── Fixture ─────────────────────────────────────────────────────────────────

		private CharacterAttributeTemplate ledgerTemplate;
		private UnityEngine.GameObject ledgerHost;
		private BaseItemTemplate itemTemplate;

		[SetUp]
		public void CreateFixture()
		{
			ledgerTemplate = UnityEngine.ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			ledgerTemplate.name = "FollowUpLedgerAttribute";
			ledgerTemplate.InitialValue = 100;
			ledgerTemplate.AddToCache(ledgerTemplate.name);

			ledgerHost = new UnityEngine.GameObject("FollowUpLedgerHost");

			itemTemplate = UnityEngine.ScriptableObject.CreateInstance<FollowUpItemTemplate>();
			itemTemplate.name = "FollowUpItem";
			itemTemplate.AddToCache(itemTemplate.name);
		}

		[TearDown]
		public void DestroyFixture()
		{
			if (ledgerTemplate != null)
			{
				ledgerTemplate.RemoveFromCache();
				UnityEngine.Object.DestroyImmediate(ledgerTemplate);
				ledgerTemplate = null;
			}
			if (itemTemplate != null)
			{
				itemTemplate.RemoveFromCache();
				UnityEngine.Object.DestroyImmediate(itemTemplate);
				itemTemplate = null;
			}
			if (ledgerHost != null)
			{
				UnityEngine.Object.DestroyImmediate(ledgerHost);
				ledgerHost = null;
			}
		}

		/// <summary>An attribute wired to a real controller, for ledger arithmetic.</summary>
		private CharacterAttribute MakeLedgerAttribute()
		{
			CharacterAttributeController controller = ledgerHost.GetComponent<CharacterAttributeController>()
				?? ledgerHost.AddComponent<CharacterAttributeController>();
			return new CharacterAttribute(controller, ledgerTemplate.ID, 100, 0);
		}

		/// <summary>Stands in for whatever behaviour reads next out of the shared state reader.</summary>
		private const int SnapshotSentinel = 0x5A5A5A5;

		/// <summary><see cref="BaseItemTemplate"/> is abstract; this is the smallest concrete one.</summary>
		private sealed class FollowUpItemTemplate : BaseItemTemplate { }
	}
}
