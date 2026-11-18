using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regressions for the two defects the 2026-08-30 combat/prediction audit found.
	/// </summary>
	/// <remarks>
	/// Both are cases where a mechanism looked correct because the shape it was written in was
	/// correct for one peer or for one contributor, and silently did nothing — or silently kept
	/// half the answer — everywhere else. Neither produced an error, which is why they are pinned
	/// here rather than left to be noticed.
	/// </remarks>
	[TestFixture]
	public class CombatAudit20260830Tests
	{
		#region Interrupt reaches a player, not just an NPC.

		/// <summary>
		/// The server must cancel directly for a character whose input it does not write.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>Interrupt</c> queued its work by setting a bit in <c>localInputFlags</c>, and that
		/// field is read by exactly one thing: <c>HandleCharacterInput</c>, reached from
		/// <c>PopulateInput</c>, which <c>CharacterPredictionController</c> invokes only where
		/// <c>HasInputAuthority</c>. For a PLAYER that peer is the owning client — so the server,
		/// which is where <c>InterruptAction</c> runs, set a bit nothing on that peer would ever
		/// read and nothing would ever clear.
		/// </para>
		/// <para>
		/// The queued path is still right for an NPC or a pet (the server does write their input,
		/// so the cancel lands inside the deterministic replicate a tick later) and for a player
		/// interrupting itself. It is only the server-acting-on-a-player case that has nobody to
		/// read it. That asymmetry is the whole rule, and it is why this is a truth table rather
		/// than a single assertion.
		/// </para>
		/// </remarks>
		[Test]
		public void Interrupt_AppliesDirectly_OnlyWhenTheServerDoesNotWriteTheInput()
		{
			LogAssert.IsTrue(
				AbilityController.ServerCancelsDirectly(isServerStarted: true, hasInputAuthority: false),
				"The server interrupting a PLAYER writes no input for that character, so the queued " +
				"flag would never be read. This is the case InterruptAction actually hits, and it " +
				"silently did nothing.");

			LogAssert.IsFalse(
				AbilityController.ServerCancelsDirectly(isServerStarted: true, hasInputAuthority: true),
				"The server interrupting an NPC or a pet DOES write their input, so the queued flag " +
				"is read next tick and the cancel happens inside the replicate. Cancelling directly " +
				"as well would take the cast down outside the deterministic simulation.");

			LogAssert.IsFalse(
				AbilityController.ServerCancelsDirectly(isServerStarted: false, hasInputAuthority: true),
				"A player interrupting its own cast queues it as input, like every other action it " +
				"takes, so the cancel is predicted and reconciled.");

			LogAssert.IsFalse(
				AbilityController.ServerCancelsDirectly(isServerStarted: false, hasInputAuthority: false),
				"An observer has authority over nothing and is told what happened.");
		}

		/// <summary>
		/// And the interrupt path actually consults that rule.
		/// </summary>
		/// <remarks>
		/// <c>OnInterrupt</c> is the observable half. On the direct path it fires here; on the
		/// queued path it fires later, from <c>ProcessInterrupt</c> inside the replicate body, so
		/// firing it here as well would double the event. Asserting the event rather than the
		/// private activation fields keeps this a test of behaviour and not of layout.
		/// </remarks>
		[Test]
		public void Interrupt_RaisesOnInterrupt_OnlyOnTheDirectPath()
		{
			GameObject host = new GameObject("InterruptHost");
			try
			{
				AbilityController controller = host.AddComponent<AbilityController>();

				int fired = 0;
				controller.OnInterrupt += () => ++fired;

				controller.Interrupt(isServerStarted: false, hasInputAuthority: true);
				LogAssert.AreEqual(0, fired,
					"The owning client queues the interrupt; ProcessInterrupt raises the event when " +
					"the replicate body consumes the flag.");

				controller.Interrupt(isServerStarted: true, hasInputAuthority: true);
				LogAssert.AreEqual(0, fired,
					"So does the server for a character whose input it writes.");

				controller.Interrupt(isServerStarted: true, hasInputAuthority: false);
				LogAssert.AreEqual(1, fired,
					"The server interrupting a player has no queue to put this in, so it cancels " +
					"here and raises the event here. Before the fix this did nothing at all and a " +
					"player's cast simply completed.");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		#endregion

		#region One contributor, several contributions to one attribute.

		/// <summary>
		/// A source that contributes twice to one attribute keeps both contributions.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>SetSource</c> STATES a contribution rather than adding to one, which is what makes a
		/// payload restore or a reconcile replay idempotent. The cost is that a contributor writing
		/// twice under one key keeps only the second write — and an item's generated attributes, a
		/// buff's <c>BonusAttributes</c> and an NPC's <c>AttributeBonuses</c> are all authored
		/// LISTS that may name one character attribute twice: a weapon with base Attack Power and a
		/// rolled Attack Power affix, a buff split into a flat part and a scalar part.
		/// </para>
		/// <para>
		/// The failure is silent in both directions: the character gets one of the two bonuses, and
		/// the release removes exactly what is there, so nothing ever drifts into being noticeable.
		/// <see cref="ModifierSource.Index"/> is what separates them.
		/// </para>
		/// </remarks>
		[Test]
		public void OneSource_ContributingTwiceToOneAttribute_KeepsBoth()
		{
			CharacterAttribute attribute = MakeAttribute(baseValue: 100);

			// One item (row 7), two of its generated attributes, both raising this one.
			attribute.SetSource(ModifierSource.Item(7, entryIndex: 101), 5);
			attribute.SetSource(ModifierSource.Item(7, entryIndex: 202), 3);

			LogAssert.AreEqual(2, attribute.ModifierSourceCount,
				"Two contributions from one item are two ledger entries. Under a single key per " +
				"item the second overwrote the first and this was 1.");
			LogAssert.AreEqual(8, attribute.ExternalModifier,
				"They SUM. Keyed only by the item, the character silently got 3 instead of 8.");
			LogAssert.AreEqual(108, attribute.FinalValue, "...and final follows.");
		}

		/// <summary>
		/// Unequipping releases every entry the item held, not the one a key happens to name.
		/// </summary>
		/// <remarks>
		/// The apply side is free to choose any index scheme — <c>ItemGenerator</c> uses the
		/// <c>ItemAttributeTemplate</c>'s own id, the buff templates use a list position — so the
		/// release side must not have to reconstruct it. Releasing one index and stranding the rest
		/// is the orphaned-modifier failure the whole ledger exists to prevent, and it would only
		/// show up on a character that had worn a two-affix item.
		/// </remarks>
		[Test]
		public void ClearSourceGroup_ReleasesEveryEntryOneContributorHolds()
		{
			CharacterAttribute attribute = MakeAttribute(baseValue: 100);

			attribute.SetSource(ModifierSource.Item(7, entryIndex: 101), 5);
			attribute.SetSource(ModifierSource.Item(7, entryIndex: 202), 3);
			// A different contributor, which must survive untouched.
			attribute.SetSource(ModifierSource.Buff(42), 10);

			LogAssert.AreEqual(18, attribute.ExternalModifier, "Two item entries plus a buff.");

			attribute.ClearSource(ModifierSource.Item(7, entryIndex: 101));
			LogAssert.AreEqual(13, attribute.ExternalModifier,
				"ClearSource still releases exactly one entry — it names one contribution.");

			attribute.SetSource(ModifierSource.Item(7, entryIndex: 101), 5);
			attribute.ClearSourceGroup(ModifierSourceKind.Item, 7);

			LogAssert.AreEqual(10, attribute.ExternalModifier,
				"ClearSourceGroup releases BOTH of the item's entries, whatever they were keyed as.");
			LogAssert.AreEqual(1, attribute.ModifierSourceCount, "Only the buff is left.");
			LogAssert.AreEqual(110, attribute.FinalValue, "...and the buff is untouched.");

			attribute.ClearSourceGroup(ModifierSourceKind.Item, 7);
			LogAssert.AreEqual(10, attribute.ExternalModifier,
				"Releasing a contributor that holds nothing is a no-op, matching ClearSource — the " +
				"correct answer for a peer that never applied it.");
		}

		/// <summary>
		/// The index is part of the identity, and only within its own kind and id.
		/// </summary>
		/// <remarks>
		/// Two contributors that differ only by index must not collapse, and a shared index across
		/// two different contributors must not collapse either. <c>NpcBonus</c> used to pack the
		/// index into the high word of the id to get the first property; the field gets both, and
		/// generalises to <c>Item</c>, whose id is a full 64-bit database row with no spare word.
		/// </remarks>
		[Test]
		public void ModifierSource_IdentityIncludesIndex_WithinItsOwnKindAndId()
		{
			LogAssert.AreNotEqual(ModifierSource.Item(7, 1), ModifierSource.Item(7, 2),
				"Same item, different contributions.");
			LogAssert.AreNotEqual(ModifierSource.Item(7, 1), ModifierSource.Item(8, 1),
				"Different items sharing an index are still different contributors.");
			LogAssert.AreEqual(ModifierSource.Item(7, 1), ModifierSource.Item(7, 1),
				"And the same key is the same key.");

			LogAssert.AreNotEqual(ModifierSource.Item(7, 1), ModifierSource.Buff(7, 1),
				"Kind separates the id spaces, which is why the kind exists.");

			LogAssert.IsTrue(ModifierSource.Item(7, 1).IsSameContributor(ModifierSource.Item(7, 2)),
				"Both are the same item, which is the question ClearSourceGroup asks.");
			LogAssert.IsFalse(ModifierSource.Item(7, 1).IsSameContributor(ModifierSource.Item(8, 1)),
				"...and a different item is a different contributor.");

			LogAssert.AreNotEqual(ModifierSource.NpcBonus(5, 0), ModifierSource.NpcBonus(5, 1),
				"NpcBonus keeps the property it had while it packed the index by hand.");
		}

		#endregion

		#region The observer stops guessing what a projectile hit.

		/// <summary>
		/// Only the server and the caster's own client decide an ability object's hit set.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The server resolves inside a rewind to the caster's view; the caster's owner resolves
		/// against the world it aimed in, which IS that rewound one, so its prediction and the
		/// server's agree by construction. A third-party observer is neither — it holds every
		/// character interpolated against its own latency — so the same query answered a question
		/// nobody asked.
		/// </para>
		/// <para>
		/// Before <c>AbilityObjectHitBroadcast</c> this answered TRUE for every peer. The last row
		/// is the regression: an observer that invented a hit ended its copy early, played the
		/// impact where nothing had happened, and with a fork carried on down a heading the server
		/// never took — and unlike a missed hit, nothing corrected it.
		/// </para>
		/// </remarks>
		[Test]
		public void HitResolution_BelongsToTheServerAndTheCastersOwner_Only()
		{
			LogAssert.IsTrue(AbilityObject.ResolvesHitsOnThisPeer(isServer: true, casterIsOwner: false),
				"The server resolves authoritatively, inside the caster's rewind.");

			LogAssert.IsTrue(AbilityObject.ResolvesHitsOnThisPeer(isServer: false, casterIsOwner: true),
				"The caster's own client predicts, against the world it actually aimed in.");

			LogAssert.IsTrue(AbilityObject.ResolvesHitsOnThisPeer(isServer: true, casterIsOwner: true),
				"Both at once is still the server; there is no host mode, but the rule must not " +
				"depend on that.");

			LogAssert.IsFalse(AbilityObject.ResolvesHitsOnThisPeer(isServer: false, casterIsOwner: false),
				"A third-party observer holds every character interpolated against its OWN latency, " +
				"not the caster's. It is told what was hit rather than deciding.");
		}

		/// <summary>
		/// A hit an observer is told about runs once and spends none of its hit count.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Two properties in one, because they are the same guard. An observer's end-of-life belongs
		/// to <c>AbilityObjectDestroyedBroadcast</c>, sent from the tick the SERVER's count ran out —
		/// so spending the count here as well would end the copy early on the peer whose answer does
		/// not matter, which is the failure the whole path was rebuilt to remove. It could not be
		/// right in any case: an observer only ever hears about the hits the server accepted, never
		/// the ones it declined.
		/// </para>
		/// <para>
		/// And the per-object hit set absorbs a repeat, which is what lets the server send the
		/// message to the caster's owner as well: the owner normally predicted the same hit already,
		/// so the report costs it nothing — but an owner that mispredicted a MISS had no correction
		/// at all before this, and its impact effect simply never played.
		/// </para>
		/// <para>
		/// A bare component is an observer by construction: <c>isServer</c> is false and there is no
		/// caster to own, so the peer gate above answers false.
		/// </para>
		/// </remarks>
		[Test]
		public void ObservedHit_SpendsNoHitCount_AndAbsorbsARepeat()
		{
			GameObject host = new GameObject("ObservedHitHost");
			try
			{
				AbilityObject abilityObject = host.AddComponent<AbilityObject>();
				abilityObject.HitCount = 1;

				abilityObject.ApplyObservedHit(null, Vector3.zero, Vector3.up);

				LogAssert.AreEqual(1, abilityObject.HitCount,
					"An observer spends no hit count: the server's destroy message is what ends its " +
					"copy, and it is sent from the tick the SERVER's count ran out.");
				LogAssert.IsFalse(abilityObject.IsDestroyed,
					"So a single hit must not end an observed copy, whatever its HitCount was.");
				LogAssert.AreEqual(1, abilityObject.HitTargetCount, "The hit was recorded once.");

				abilityObject.ApplyObservedHit(null, Vector3.zero, Vector3.up);

				LogAssert.AreEqual(1, abilityObject.HitTargetCount,
					"The same body reported twice is one hit. This is what makes the message safe to " +
					"send to the caster's owner, which usually predicted it already.");
				LogAssert.AreEqual(1, abilityObject.HitCount, "...and still spends nothing.");
				LogAssert.IsFalse(abilityObject.IsDestroyed, "...and still does not end the copy.");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		/// <summary>
		/// A hit is published to observers once per body, not once per tick.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The publish has to sit BEHIND the per-object hit set, not in front of it. The sweep
		/// re-runs every tick, and every shipped ability is stationary with a five second lifetime —
		/// so an object sitting on a character reports that character on all 150 of its ticks. A
		/// publish on the wrong side of the dedupe turns one hit into 150 reliable messages per
		/// observer, against a budget of roughly 400&#160;B/s per observed peer.
		/// </para>
		/// <para>
		/// This is a regression test in the literal sense: the first cut of the observer hit path
		/// published from <c>DispatchSweptHit</c> before calling into the dedupe, and did exactly
		/// that.
		/// </para>
		/// </remarks>
		[Test]
		public void ServerPublishesEachHit_OncePerBody_NotOncePerTick()
		{
			GameObject host = new GameObject("PublishOnceHost");
			GameObject victim = new GameObject("PublishOnceVictim");
			try
			{
				AbilityObject abilityObject = host.AddComponent<AbilityObject>();
				abilityObject.HitCount = 5;
				// The server's copy: the peer that resolves hits, and the only one that publishes.
				IsServerField.SetValue(abilityObject, true);

				Rigidbody body = victim.AddComponent<Rigidbody>();
				body.isKinematic = true;
				Collider collider = victim.AddComponent<BoxCollider>();

				Dispatch(abilityObject, collider);
				LogAssert.AreEqual(1, abilityObject.PublishedHitCount, "The first hit is published.");

				// The same body, on the next three ticks of the sweep.
				Dispatch(abilityObject, collider);
				Dispatch(abilityObject, collider);
				Dispatch(abilityObject, collider);

				LogAssert.AreEqual(1, abilityObject.PublishedHitCount,
					"A body already hit is published once, however many ticks it stays inside the " +
					"sweep. Publishing ahead of the dedupe sent one message per tick.");
				LogAssert.AreEqual(1, abilityObject.HitTargetCount, "One body, one entry.");
				LogAssert.AreEqual(4, abilityObject.HitCount, "And one hit charged, not four.");
			}
			finally
			{
				Object.DestroyImmediate(victim);
				Object.DestroyImmediate(host);
			}
		}

		#endregion

		// ── Fixture ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Marks an <see cref="AbilityObject"/> as the server's copy — the peer that resolves and
		/// publishes hits. A bare component is an observer by construction, and reflection is how
		/// the sweep tests already reach this: <c>Initialize</c> wants a live caster and a
		/// TimeManager, neither of which an edit-mode test has.
		/// </summary>
		private static readonly FieldInfo IsServerField = typeof(AbilityObject)
			.GetField("isServer", BindingFlags.Instance | BindingFlags.NonPublic);

		/// <summary>Invokes the private swept-hit dispatch, as <c>AbilityObjectSweepTests</c> does.</summary>
		private static readonly MethodInfo DispatchSweptHitMethod = typeof(AbilityObject)
			.GetMethod("DispatchSweptHit", BindingFlags.Instance | BindingFlags.NonPublic);

		private static void Dispatch(AbilityObject abilityObject, Collider collider)
		{
			AbilitySweepHit hit = new AbilitySweepHit(collider, Vector3.zero, Vector3.up, 1f,
				// Local point: far outside any authored shield volume, so these fixtures exercise the
				// ordinary hit path rather than the block gate.
				new Vector3(0f, 0f, -1000f));
			DispatchSweptHitMethod.Invoke(abilityObject, new object[] { hit });
		}

		private CharacterAttributeTemplate template;
		private GameObject host;

		[SetUp]
		public void CreateFixture()
		{
			template = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			template.name = "CombatAudit20260830Attribute";
			template.InitialValue = 100;
			// CharacterAttribute resolves its template out of the cache by id, so an
			// unregistered template leaves it with a null Template and the first
			// UpdateValues throws. Same fixture shape as AttributeLedgerContractTests.
			template.AddToCache(template.name);

			host = new GameObject("CombatAudit20260830Host");
		}

		[TearDown]
		public void DestroyFixture()
		{
			if (template != null)
			{
				template.RemoveFromCache();
				Object.DestroyImmediate(template);
				template = null;
			}
			if (host != null)
			{
				Object.DestroyImmediate(host);
				host = null;
			}
		}

		private CharacterAttribute MakeAttribute(int baseValue)
		{
			CharacterAttributeController controller = host.GetComponent<CharacterAttributeController>()
				?? host.AddComponent<CharacterAttributeController>();
			return new CharacterAttribute(controller, template.ID, baseValue, 0);
		}
	}
}
