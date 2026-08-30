using FishMMO.Shared;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The three-term contract a <see cref="CharacterAttribute"/> holds, and the ledger that
	/// produces the middle one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>base + formula + external = final.</b> The BASE value is the unbuffed number the database
	/// stores. The EXTERNAL modifier is the sum of the ledger — items, buffs, regions, NPC scaling,
	/// and the server's authoritative residual. The FORMULA modifier is derived from child
	/// attributes and is recomputed on every graph pass, which is why it is a separate term rather
	/// than a ledger entry: it has no contributor to release it. FINAL is what every consumer reads
	/// — movement, damage, damage reduction, a resource's maximum.
	/// </para>
	/// <para>
	/// These tests pin the arithmetic rather than the plumbing. Each one describes a state a player
	/// can reach, because every failure in this area presents as a plausible wrong number rather
	/// than as an error.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AttributeLedgerContractTests
	{
		#region The three terms.

		/// <summary>
		/// An external modifier moves the final value and leaves the base value alone.
		/// </summary>
		/// <remarks>
		/// The separation is what makes a save correct: the row stores the base, and the modifiers
		/// are rebuilt from live sources on load. If a bonus ever reached the base, every relog
		/// would bake the buff in permanently and the character would ratchet upward.
		/// </remarks>
		[Test]
		public void ExternalModifier_MovesTheFinalValue_NotTheBase()
		{
			CharacterAttribute attribute = MakeAttribute(baseValue: 100);

			LogAssert.AreEqual(100, attribute.Value, "The base starts where it was constructed.");
			LogAssert.AreEqual(100, attribute.FinalValue, "With no contributors, final IS the base.");

			attribute.SetSource(ModifierSource.Item(1), 25);

			LogAssert.AreEqual(100, attribute.Value,
				"An equipped item must not touch the base value. The base is what the database " +
				"stores, so a bonus reaching it would be baked in permanently at the next save.");
			LogAssert.AreEqual(25, attribute.ExternalModifier, "The bonus is the external modifier.");
			LogAssert.AreEqual(125, attribute.FinalValue, "And final is base plus external.");

			attribute.ClearSource(ModifierSource.Item(1));

			LogAssert.AreEqual(100, attribute.Value, "Unequipping leaves the base where it was.");
			LogAssert.AreEqual(0, attribute.ExternalModifier, "...and releases the whole contribution.");
			LogAssert.AreEqual(100, attribute.FinalValue, "...so final returns exactly to the base.");
		}

		/// <summary>
		/// Contributors of different kinds sum rather than replacing one another.
		/// </summary>
		/// <remarks>
		/// A character wearing gear, carrying a buff and standing in a region holds three live
		/// contributions to one attribute. Releasing any one of them must leave the other two
		/// exactly as they were — the case a shared key breaks, and the reason the ledger records
		/// who contributed what instead of a running total.
		/// </remarks>
		[Test]
		public void Contributors_SumAndAreReleasedIndependently()
		{
			CharacterAttribute attribute = MakeAttribute(baseValue: 50);

			attribute.SetSource(ModifierSource.Item(11), 10);
			attribute.SetSource(ModifierSource.Buff(22), 5);
			attribute.SetSource(ModifierSource.Region(33), 3);

			LogAssert.AreEqual(3, attribute.ModifierSourceCount, "Three contributors, three entries.");
			LogAssert.AreEqual(68, attribute.FinalValue, "50 + 10 + 5 + 3.");

			// The buff expires. The gear and the region are untouched.
			attribute.ClearSource(ModifierSource.Buff(22));

			LogAssert.AreEqual(63, attribute.FinalValue, "Only the buff's 5 is released.");
			LogAssert.AreEqual(10, attribute.GetSourceValue(ModifierSource.Item(11)), "The item is untouched.");
			LogAssert.AreEqual(3, attribute.GetSourceValue(ModifierSource.Region(33)), "So is the region.");
		}

		/// <summary>
		/// Restating a contribution is idempotent, so a re-apply cannot double it.
		/// </summary>
		/// <remarks>
		/// A character is loaded from the database, restored from a spawn payload and then corrected
		/// by a reconcile — the same item can be applied three times over. Each states its whole
		/// contribution rather than adding one, so arriving twice is arriving once.
		/// </remarks>
		[Test]
		public void RestatingAContribution_DoesNotDoubleIt()
		{
			CharacterAttribute attribute = MakeAttribute(baseValue: 100);

			attribute.SetSource(ModifierSource.Item(1), 25);
			attribute.SetSource(ModifierSource.Item(1), 25);
			attribute.SetSource(ModifierSource.Item(1), 25);

			LogAssert.AreEqual(1, attribute.ModifierSourceCount, "One item is one contributor.");
			LogAssert.AreEqual(125, attribute.FinalValue, "However many times it is applied.");
		}

		#endregion

		#region The authoritative residual.

		/// <summary>
		/// Installing the server's total preserves what this peer has attributed.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The server's number is the answer and must be reproduced exactly, but it is not a
		/// contributor — so it lands as the RESIDUAL between that number and the sum of everything
		/// this peer knows the source of. The observable total is identical to a wholesale
		/// overwrite; what changes is that the peer can still RELEASE its own sources afterwards.
		/// </para>
		/// <para>
		/// Collapsing the ledger to a single entry on every reconcile is the alternative, and it
		/// leaves the owner unable to unequip between reconciles: the release finds nothing to
		/// remove and the bonus stays until the next authoritative push.
		/// </para>
		/// </remarks>
		[Test]
		public void AuthoritativeTotal_IsInstalledAsAResidual()
		{
			CharacterAttribute attribute = MakeAttribute(baseValue: 100);
			attribute.SetSource(ModifierSource.Item(1), 25);

			// The server says the whole external modifier is 25 — the same item, seen from there.
			attribute.SetModifier(25);

			LogAssert.AreEqual(25, attribute.ExternalModifier,
				"The total must be exactly the server's number, not the server's number plus ours.");
			LogAssert.AreEqual(25, attribute.GetSourceValue(ModifierSource.Item(1)),
				"...and the item's own entry must survive it, or nothing can release the item.");
			LogAssert.AreEqual(0, attribute.GetSourceValue(ModifierSource.Authoritative),
				"With the peer's ledger already agreeing, the residual is nothing.");

			// Unequip. The item's entry goes; the residual does not resurrect it.
			attribute.ClearSource(ModifierSource.Item(1));
			LogAssert.AreEqual(0, attribute.ExternalModifier,
				"Releasing the item after an authoritative install must actually release it.");
		}

		/// <summary>
		/// A peer that applied nothing carries the server's total as the whole modifier.
		/// </summary>
		/// <remarks>
		/// This is an observer. It runs no buff simulation and applies no item bonuses, so its
		/// ledger is a single authoritative entry — and the total still has to match the server's
		/// exactly, because a health bar is drawn from it.
		/// </remarks>
		[Test]
		public void AuthoritativeTotal_IsTheWholeModifier_ForAPeerThatAppliedNothing()
		{
			CharacterAttribute attribute = MakeAttribute(baseValue: 100);

			attribute.SetModifier(40);

			LogAssert.AreEqual(40, attribute.ExternalModifier, "The server's total, in full.");
			LogAssert.AreEqual(40, attribute.GetSourceValue(ModifierSource.Authoritative),
				"...held by the authoritative entry, because nothing else contributed.");
			LogAssert.AreEqual(140, attribute.FinalValue, "And final follows.");
		}

		/// <summary>
		/// A local contribution applied AFTER an authoritative install adds to it.
		/// </summary>
		/// <remarks>
		/// The owner predicts an equip between two reconciles. The bonus has to show immediately —
		/// that is the point of predicting it — and the next authoritative total then subsumes it
		/// rather than adding to it again.
		/// </remarks>
		[Test]
		public void ALocalContribution_AfterAnAuthoritativeInstall_IsAddedThenSubsumed()
		{
			CharacterAttribute attribute = MakeAttribute(baseValue: 100);

			// The server's state before the equip.
			attribute.SetModifier(0);
			LogAssert.AreEqual(100, attribute.FinalValue, "Nothing equipped yet.");

			// The owner predicts the equip.
			attribute.SetSource(ModifierSource.Item(1), 25);
			LogAssert.AreEqual(125, attribute.FinalValue,
				"The predicted bonus must show at once, not on the next reconcile.");

			// The server catches up and reports a total that already includes it.
			attribute.SetModifier(25);
			LogAssert.AreEqual(125, attribute.FinalValue,
				"The authoritative total must not add the bonus a second time.");
			LogAssert.AreEqual(0, attribute.GetSourceValue(ModifierSource.Authoritative),
				"The residual closes to nothing once the server agrees with the peer's ledger.");
		}

		#endregion

		#region Persistence marking.

		/// <summary>
		/// Only a change to a PERSISTED field marks the attribute for saving.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The row stores the base value. The external modifier is not stored — it is rebuilt from
		/// live sources on load — so a contribution arriving or leaving changes nothing the database
		/// needs to hear about.
		/// </para>
		/// <para>
		/// The mark used to be set from the change funnel every mutation passes through, which meant
		/// equipping an item, a buff ticking or walking into a region each dirtied the attribute and
		/// the periodic save rewrote a row whose contents were identical. In combat that was most of
		/// the sheet, most of the time — precisely the case the flag exists to avoid.
		/// </para>
		/// </remarks>
		[Test]
		public void PersistenceMark_FollowsTheBaseValue_NotTheModifiers()
		{
			CharacterAttribute attribute = MakeAttribute(baseValue: 100);

			// Whatever construction left behind is not what is under test.
			attribute.MarkPersistPending(1);
			attribute.MarkPersisted(1);
			LogAssert.IsFalse(attribute.PersistenceDirty, "Start from a confirmed-clean attribute.");

			attribute.SetSource(ModifierSource.Item(1), 25);
			LogAssert.IsFalse(attribute.PersistenceDirty,
				"Equipping an item changes no persisted field. The row would be rewritten identical.");

			attribute.SetSource(ModifierSource.Buff(2), 5);
			attribute.ClearSource(ModifierSource.Buff(2));
			LogAssert.IsFalse(attribute.PersistenceDirty,
				"A buff arriving and expiring changes no persisted field either.");

			attribute.SetModifier(30);
			LogAssert.IsFalse(attribute.PersistenceDirty,
				"Nor does installing the server's authoritative total.");

			attribute.SetValue(120);
			LogAssert.IsTrue(attribute.PersistenceDirty,
				"The BASE value is what the row stores, so moving it must mark.");
		}

		/// <summary>
		/// Assigning the value it already holds marks nothing.
		/// </summary>
		/// <remarks>
		/// The load path writes every attribute's base value whether or not it differs, and a
		/// reconcile restates the sheet on every corrected tick. If those marked, the flag would be
		/// permanently set and the save would be back to writing everything every pass.
		/// </remarks>
		[Test]
		public void PersistenceMark_IgnoresAWriteThatChangesNothing()
		{
			CharacterAttribute attribute = MakeAttribute(baseValue: 100);
			attribute.MarkPersistPending(1);
			attribute.MarkPersisted(1);

			attribute.SetValue(100);
			LogAssert.IsFalse(attribute.PersistenceDirty,
				"Setting the base to the value it already holds is not a change.");

			attribute.AddValue(0);
			LogAssert.IsFalse(attribute.PersistenceDirty, "Nor is adding nothing to it.");

			attribute.AddValue(5);
			LogAssert.IsTrue(attribute.PersistenceDirty, "Adding something is.");
		}

		#endregion

		// ── Fixture ──────────────────────────────────────────────────────────────────

		private CharacterAttributeTemplate template;
		private UnityEngine.GameObject host;

		[SetUp]
		public void CreateFixture()
		{
			template = UnityEngine.ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			template.name = "LedgerContractAttribute";
			template.InitialValue = 100;
			template.AddToCache(template.name);

			host = new UnityEngine.GameObject("LedgerContractHost");
		}

		[TearDown]
		public void DestroyFixture()
		{
			if (template != null)
			{
				template.RemoveFromCache();
				UnityEngine.Object.DestroyImmediate(template);
				template = null;
			}
			if (host != null)
			{
				UnityEngine.Object.DestroyImmediate(host);
				host = null;
			}
		}

		/// <summary>An attribute wired to a real controller, so propagation behaves normally.</summary>
		private CharacterAttribute MakeAttribute(int baseValue)
		{
			CharacterAttributeController controller = host.GetComponent<CharacterAttributeController>()
				?? host.AddComponent<CharacterAttributeController>();
			return new CharacterAttribute(controller, template.ID, baseValue, 0);
		}
	}
}
