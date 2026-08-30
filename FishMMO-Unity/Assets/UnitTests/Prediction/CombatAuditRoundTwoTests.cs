using System.Reflection;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regressions for the second round of the 2026-08-30 combat/prediction audit.
	/// </summary>
	/// <remarks>
	/// Every defect pinned here shares a shape: a rule that was correct for the peer or the
	/// contributor its author had in mind, and silently wrong for one that arrives by another
	/// route. None of them produced an error or a log line, which is why they are pinned rather
	/// than left to be noticed.
	/// </remarks>
	[TestFixture]
	public class CombatAuditRoundTwoTests
	{
		private const BindingFlags Any =
			BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		#region F1 — a one-shot input flag is only raised where something drains it.

		/// <summary>
		/// The server interrupting a player cancels directly and queues NOTHING.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>Interrupt</c> used to raise <c>AbilityActivationFlags.Interrupt</c> in
		/// <c>localInputFlags</c> unconditionally, before deciding what to do. That field is drained
		/// by exactly one thing — <c>HandleCharacterInput</c>, which copies it into the replicate and
		/// clears the bit — and that method returns early for a peer with no input authority, BEFORE
		/// the clear. So on the server's copy of a client-owned player, which is precisely the case
		/// <c>InterruptAction</c> hits, the bit went up and could never come down.
		/// </para>
		/// <para>
		/// It was inert only because nothing server-side happens to call <c>Activate</c> on a player
		/// today. The guards at both <c>Activate</c> overloads read that flag, so a scripted event,
		/// a mount or a possession would have found the character permanently unable to cast.
		/// </para>
		/// </remarks>
		[Test]
		public void Interrupt_QueuesTheFlag_OnlyWhereTheInputStreamDrainsIt()
		{
			LogAssert.AreEqual(
				AbilityController.InterruptDisposition.Applied,
				AbilityController.ResolveInterruptDisposition(isServerStarted: true, hasInputAuthority: false),
				"The server interrupting a PLAYER writes no input for it, so there is no stream to " +
				"carry a flag and nothing to clear one. It must cancel here and raise nothing.");

			LogAssert.AreEqual(
				AbilityController.InterruptDisposition.Queued,
				AbilityController.ResolveInterruptDisposition(isServerStarted: true, hasInputAuthority: true),
				"An NPC or pet: the server writes its input, so HandleCharacterInput reads the flag " +
				"next tick and clears it. Queuing is strictly better than cancelling out of band.");

			LogAssert.AreEqual(
				AbilityController.InterruptDisposition.Queued,
				AbilityController.ResolveInterruptDisposition(isServerStarted: false, hasInputAuthority: true),
				"A player interrupting its own cast drains its own flag.");

			LogAssert.AreEqual(
				AbilityController.InterruptDisposition.Ignored,
				AbilityController.ResolveInterruptDisposition(isServerStarted: false, hasInputAuthority: false),
				"An observer has neither an input stream nor the authority to cancel. Raising the " +
				"flag here latched it exactly as it did on the server's copy of a player.");
		}

		/// <summary>
		/// Every disposition that raises the flag is one whose peer drains it.
		/// </summary>
		/// <remarks>
		/// Stated as the invariant rather than as four cases, so a future peer role added to the
		/// truth table cannot quietly reintroduce a latch. "Drains it" is exactly
		/// <c>hasInputAuthority</c>, because that is the condition <c>HandleCharacterInput</c>
		/// returns early on.
		/// </remarks>
		[Test]
		public void Interrupt_NeverQueues_OnAPeerThatWritesNoInput()
		{
			foreach (bool isServer in new[] { false, true })
			{
				foreach (bool hasInputAuthority in new[] { false, true })
				{
					AbilityController.InterruptDisposition disposition =
						AbilityController.ResolveInterruptDisposition(isServer, hasInputAuthority);

					if (disposition == AbilityController.InterruptDisposition.Queued)
					{
						LogAssert.IsTrue(hasInputAuthority,
							$"Queued the interrupt flag with isServer={isServer}, " +
							$"hasInputAuthority={hasInputAuthority}. HandleCharacterInput returns before " +
							"the clear on a peer with no input authority, so this latches forever.");
					}
				}
			}
		}

		#endregion

		#region F2 — one buffer-growth rule, one truncation ceiling.

		/// <summary>
		/// <c>ApplyThreatAction</c> grows its overlap buffer through the shared helper.
		/// </summary>
		/// <remarks>
		/// <para>
		/// It hand-rolled the loop with a private ceiling of 512, against
		/// <see cref="TargetOrdering.MaximumQueryBufferSize"/> of 256 — so it was the one query in
		/// the project that truncated somewhere else, while <c>TargetOrdering</c>'s own
		/// documentation claimed 256 was universal.
		/// </para>
		/// <para>
		/// The ceiling is the lesser half. The shared helper is also the only thing that reports the
		/// one case no ordering downstream can repair: past the ceiling the broadphase discards
		/// candidates in its own order, and for a threat sweep that means an arbitrary subset of a
		/// pull never notices the cast. A hand-rolled loop truncates in silence.
		/// </para>
		/// </remarks>
		[Test]
		public void ApplyThreatAction_UsesTheSharedBufferCeiling()
		{
			LogAssert.IsNull(
				typeof(ApplyThreatAction).GetField("MaximumBufferSize", Any),
				"ApplyThreatAction must not carry its own buffer ceiling. A second ceiling means a " +
				"second truncation point, and the sweep that used it also skipped the shared " +
				"helper's once-per-session saturation warning.");

			FieldInfo hits = typeof(ApplyThreatAction).GetField("hits", Any);
			LogAssert.IsNotNull(hits, "The overlap buffer field should still exist.");

			Collider[] buffer = (Collider[])hits.GetValue(null);
			LogAssert.AreEqual(TargetOrdering.QueryBufferSize(0), buffer.Length,
				"The buffer must start at the shared starting size, so its growth curve and its " +
				"truncation point are the ones every other query in the project uses.");
		}

		/// <summary>
		/// The shared helper stops growing at the shared ceiling and says so once.
		/// </summary>
		/// <remarks>
		/// Pinned alongside the call site because the two only mean anything together: adopting the
		/// helper buys the warning, and the warning is the whole reason the ceiling is a reportable
		/// event rather than a silent one.
		/// </remarks>
		[Test]
		public void TryGrowQueryBuffer_StopsAtTheSharedCeiling()
		{
			Collider[] buffer = new Collider[TargetOrdering.MaximumQueryBufferSize];

			TargetOrdering.ResetQueryBufferWarning();
			bool grew = TargetOrdering.TryGrowQueryBuffer(ref buffer, buffer.Length);
			TargetOrdering.ResetQueryBufferWarning();

			LogAssert.IsFalse(grew,
				"A full buffer already at the ceiling cannot grow; the caller must stop rather than " +
				"loop forever.");
			LogAssert.AreEqual(TargetOrdering.MaximumQueryBufferSize, buffer.Length,
				"...and the buffer stays at the ceiling rather than being reallocated smaller, which " +
				"would undo the previous query's growth on every cast.");
		}

		#endregion

		#region F3 — one spelling of "broadcast to observers except the owner".

		/// <summary>
		/// <c>EquipmentController</c> no longer carries its own copy of the recipient collection.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The copy was line-for-line identical to <see cref="ObserverBroadcastScope"/>, which is the
		/// one place that documents why the copy is mandatory at all:
		/// <c>ServerManager.BroadcastExcept</c> calls <c>Remove</c> on the set it is handed, so
		/// passing <c>NetworkObject.Observers</c> to it does not exclude the owner for one message —
		/// it permanently drops the owner from that object's observer set.
		/// </para>
		/// <para>
		/// A second implementation of a rule that subtle is how the next one gets it wrong. The copy
		/// also never cleared its static set after a send, so it retained a connection reference per
		/// observer between equipment pushes.
		/// </para>
		/// </remarks>
		[Test]
		public void EquipmentController_DoesNotReimplementTheObserverBroadcastScope()
		{
			LogAssert.IsNull(
				typeof(EquipmentController).GetMethod("CollectObserverRecipients", Any),
				"Equipment must broadcast through ObserverBroadcastScope. A private copy of the " +
				"BroadcastExcept rule is how the owner gets dropped from an observer set.");

			LogAssert.IsNull(
				typeof(EquipmentController).GetField("observerRecipients", Any),
				"...and its scratch recipient set goes with it; the shared one clears itself after " +
				"each send, this one did not.");
		}

		#endregion

		#region F4 — the ability reverse index names one ability, and removal checks which.

		/// <summary>
		/// Removing one of two abilities built from the same template leaves the other resolvable.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>templateToAbilityID</c> maps ONE template to ONE ability id and the last writer wins,
		/// so two abilities sharing a template leave the index naming only the later of them.
		/// <c>RemoveAbility</c> deleted the mapping unconditionally, so removing the EARLIER ability
		/// dropped the entry belonging to one the character still has, and
		/// <c>KnowsLearnedAbility</c> then reported false for it.
		/// </para>
		/// <para>
		/// Exercised against the dictionaries directly rather than through the controller, which is a
		/// <c>NetworkBehaviour</c> and cannot answer <c>IsOwner</c> unspawned. What is pinned is the
		/// rule the removal now applies: delete the mapping only when it actually names the ability
		/// being removed.
		/// </para>
		/// </remarks>
		[Test]
		public void RemoveAbility_DropsTheReverseIndexEntry_OnlyWhenItNamesThatAbility()
		{
			GameObject controllerHost = new GameObject("AuditRoundTwoAbilityController");
			AbilityTemplate abilityTemplate = ScriptableObject.CreateInstance<AbilityTemplate>();
			try
			{
				abilityTemplate.name = "AuditRoundTwoAbility";
				abilityTemplate.AddToCache(abilityTemplate.name);

				AbilityController controller = controllerHost.AddComponent<AbilityController>();
				if (controller.KnownAbilities == null)
				{
					controller.OnAwake();
				}

				FieldInfo indexField = typeof(AbilityController).GetField("templateToAbilityID", Any);
				LogAssert.IsNotNull(indexField, "The reverse index should still exist.");

				// Two abilities from ONE template. The later learn wins the index, which is exactly
				// what LearnAbility's last-writer-wins assignment produces.
				controller.KnownAbilities[100L] = new Ability(100L, abilityTemplate, null);
				controller.KnownAbilities[200L] = new Ability(200L, abilityTemplate, null);
				var index = (System.Collections.Generic.Dictionary<int, long>)indexField.GetValue(controller);
				index[abilityTemplate.ID] = 200L;

				controller.RemoveAbility(100L);

				index = (System.Collections.Generic.Dictionary<int, long>)indexField.GetValue(controller);
				LogAssert.IsTrue(index.ContainsKey(abilityTemplate.ID),
					"Removing ability 100 must not delete the index entry that names ability 200 — " +
					"that ability is still known, and KnowsLearnedAbility resolves through this map.");
				LogAssert.AreEqual(200L, index[abilityTemplate.ID],
					"...and the entry must still name the ability it named before.");

				controller.RemoveAbility(200L);

				index = (System.Collections.Generic.Dictionary<int, long>)indexField.GetValue(controller);
				LogAssert.IsFalse(index.ContainsKey(abilityTemplate.ID),
					"Removing the ability the index DOES name must clear it, or the map outlives " +
					"its target and resolves a template to an ability that is gone.");
			}
			finally
			{
				if (abilityTemplate != null)
				{
					abilityTemplate.RemoveFromCache();
					Object.DestroyImmediate(abilityTemplate);
				}
				Object.DestroyImmediate(controllerHost);
			}
		}

		#endregion

		#region F5 — only a peer that spends the hit count may move it.

		/// <summary>
		/// The peers that resolve hits are exactly the peers that spend the hit count.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>AbilityObject.ApplyHit</c> returns before its <c>HitCount--</c> on any peer that does
		/// not resolve its own hits, because such a peer is only ever TOLD about the hits the server
		/// resolved and never sees the ones it declined. <c>AbilityHitCountAction</c> had no
		/// authority gate at all, so it ADDED to the count on those same peers — walking an
		/// observer's copy away from the authoritative number in a direction nothing corrects.
		/// </para>
		/// <para>
		/// Inert today (an observer's copy ends on <c>AbilityObjectDestroyedBroadcast</c>, not on its
		/// own count) and latent only because no ability asset authors this action yet.
		/// </para>
		/// </remarks>
		[Test]
		public void HitCount_IsSpentAndMoved_ByTheSamePeers()
		{
			LogAssert.IsTrue(AbilityObject.ResolvesHitsOnThisPeer(isServer: true, casterIsOwner: false),
				"The server resolves authoritatively, inside a rewind to the caster's view.");
			LogAssert.IsTrue(AbilityObject.ResolvesHitsOnThisPeer(isServer: false, casterIsOwner: true),
				"The caster's owner predicts against the world it aimed in.");
			LogAssert.IsFalse(AbilityObject.ResolvesHitsOnThisPeer(isServer: false, casterIsOwner: false),
				"A third-party observer holds neither world and is told instead.");

			PropertyInfo resolvesLocally = typeof(AbilityObject).GetProperty("ResolvesHitsLocally", Any);
			LogAssert.IsNotNull(resolvesLocally,
				"AbilityHitCountAction asks this before moving the count; it must remain reachable " +
				"from the action's assembly.");
		}

		/// <summary>
		/// <c>AbilityHitCountAction</c> leaves the count alone on a peer that never spends it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The action ran on every peer with no authority gate at all, so an observer's
		/// <c>HitCount</c> climbed away from the server's on every hit the ability was told about,
		/// with nothing anywhere to bring the two back together.
		/// </para>
		/// <para>
		/// Driven through the real action against a real <c>AbilityObject</c>, with <c>isServer</c>
		/// flipped by reflection because that is the field the property reads and there is no
		/// NetworkManager here to set it. A bare object with no caster answers "observer", which is
		/// the case that was wrong.
		/// </para>
		/// </remarks>
		[Test]
		public void AbilityHitCountAction_MovesTheCount_OnlyOnAPeerThatSpendsIt()
		{
			GameObject objectHost = new GameObject("AuditRoundTwoAbilityObject");
			try
			{
				AbilityObject abilityObject = objectHost.AddComponent<AbilityObject>();
				FieldInfo isServer = typeof(AbilityObject).GetField("isServer", Any);
				LogAssert.IsNotNull(isServer, "AbilityObject should still track which peer it is on.");

				AbilityHitCountAction action = new AbilityHitCountAction
				{
					AmountValue = new ConstantValue { Amount = 3 },
				};

				// Observer: no caster and not the server, so this peer never spends the count.
				isServer.SetValue(abilityObject, false);
				abilityObject.HitCount = 1;
				action.Execute(null, new AbilityCollisionEventData(null, null, abilityObject));

				LogAssert.AreEqual(1, abilityObject.HitCount,
					"An observer is only told about the hits the server resolved and never spends " +
					"the count, so adding to it here walks its copy away from the authoritative " +
					"number in a direction nothing corrects.");

				// Server: this peer resolves and spends, so it may also extend.
				isServer.SetValue(abilityObject, true);
				action.Execute(null, new AbilityCollisionEventData(null, null, abilityObject));

				LogAssert.AreEqual(4, abilityObject.HitCount,
					"The action must still do its job on a peer that resolves hits — pierce is the " +
					"whole point of it.");
			}
			finally
			{
				Object.DestroyImmediate(objectHost);
			}
		}

		#endregion

		#region F8 — a region's two contributions to one attribute sum.

		/// <summary>
		/// Two region contributions to one attribute sum instead of overwriting each other.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>CharacterAttribute.SetSource</c> STATES a whole contribution rather than adding to one,
		/// so two contributions sharing a key are not two contributions — the second silently
		/// replaces the first. <c>ApplyRegionAttributeAction</c> always passed the default entry
		/// index, so a region carrying two of them pointed at one attribute delivered half its
		/// authored bonus, with no error anywhere.
		/// </para>
		/// <para>
		/// This is the hazard <see cref="ModifierSource.Index"/> exists for, and which
		/// <c>ItemGenerator</c> (keyed by item attribute template) and <c>AttributeBuffTemplate</c>
		/// (keyed by list position) already answered. The region action was the last contributor
		/// still passing the default.
		/// </para>
		/// </remarks>
		[Test]
		public void RegionContributions_WithDistinctIndices_Sum()
		{
			const long regionObjectID = 42L;

			LogAssert.AreNotEqual(
				ModifierSource.Region(regionObjectID, 0),
				ModifierSource.Region(regionObjectID, 1),
				"Two contributions from one region must be distinguishable, or SetSource collapses " +
				"them and the second silently replaces the first.");

			LogAssert.IsNotNull(
				typeof(ApplyRegionAttributeAction).GetField("EntryIndex", Any),
				"ApplyRegionAttributeAction must be able to name WHICH of a region's contributions " +
				"it is. It always passed the default index, so a region carrying two of these " +
				"actions pointed at one attribute delivered half its authored bonus.");

			CharacterAttribute attribute = MakeAttribute(baseValue: 100);

			attribute.SetSource(ModifierSource.Region(regionObjectID, 0), 10);
			attribute.SetSource(ModifierSource.Region(regionObjectID, 1), 5);

			LogAssert.AreEqual(15, attribute.ExternalModifier,
				"Both of the region's contributions must be live. Sharing one key gave 5 — the " +
				"second overwriting the first — and a designer's flat-plus-scalar split lost half.");
			LogAssert.AreEqual(115, attribute.FinalValue, "...and both reach the final value.");
		}

		/// <summary>
		/// Leaving the region releases every contribution it made, whatever they were keyed as.
		/// </summary>
		/// <remarks>
		/// The release half of the index. <c>Region.ReleaseAttributeContributions</c> goes through
		/// <c>ClearSourceGroup</c>, which drops every entry sharing the region's (Kind, Id) — so the
		/// apply side is free to pick any index scheme without the release side reconstructing it.
		/// Reproducing the scheme on both sides would mean the two must agree forever.
		/// </remarks>
		[Test]
		public void LeavingARegion_ReleasesEveryIndexedContribution()
		{
			const long regionObjectID = 42L;
			CharacterAttribute attribute = MakeAttribute(baseValue: 100);

			attribute.SetSource(ModifierSource.Region(regionObjectID, 0), 10);
			attribute.SetSource(ModifierSource.Region(regionObjectID, 1), 5);
			attribute.SetSource(ModifierSource.Region(regionObjectID + 1, 0), 3);

			attribute.ClearSourceGroup(ModifierSourceKind.Region, regionObjectID);

			LogAssert.AreEqual(3, attribute.ExternalModifier,
				"Both of the departed region's entries go, whatever index wrote them, and the OTHER " +
				"region's contribution stays — overlapping regions must not release each other's.");
		}

		/// <summary>
		/// Re-entering a region does not accumulate.
		/// </summary>
		/// <remarks>
		/// The property that made an <c>OnRegionStay</c> trigger usable at all: <c>SetSource</c>
		/// states the same number every tick rather than adding it. Under the old
		/// <c>AddModifier</c> shape a stay trigger accumulated once per tick, forever, with nothing
		/// able to reverse it.
		/// </remarks>
		[Test]
		public void ReenteringARegion_RestatesRatherThanAccumulates()
		{
			const long regionObjectID = 42L;
			CharacterAttribute attribute = MakeAttribute(baseValue: 100);

			for (int stayTick = 0; stayTick < 50; ++stayTick)
			{
				attribute.SetSource(ModifierSource.Region(regionObjectID, 0), 10);
			}

			LogAssert.AreEqual(10, attribute.ExternalModifier,
				"Fifty stay ticks are one contribution restated fifty times, not fifty bonuses.");
		}

		#endregion

		#region F10 — a taunt guarantees in the space the target choice is made in.

		/// <summary>
		/// The vulnerability bound covers every score the table could produce.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>AggressionController.PickTarget</c> chooses on <c>GetThreatScore</c>, which multiplies
		/// raw points by a vulnerability factor for a wounded or out-of-mana character.
		/// <c>ApplyTauntAction</c> compared RAW points, so a taunt that put the taunter above the
		/// highest raw entry still lost to a wounded ally on the next re-evaluation —
		/// <c>ForceImmediateTargetSwitch</c> hid it for exactly one switch.
		/// </para>
		/// <para>
		/// The table holds no character references, so it cannot evaluate another entry's score
		/// directly. What makes the guarantee sound is that every entry's score is bounded above by
		/// its raw points times this factor, so clearing that bound clears every actual score
		/// whichever entry carries it.
		/// </para>
		/// </remarks>
		[Test]
		public void VulnerabilityBound_CoversTheCompoundedMultipliers()
		{
			AggressionController aggression = new AggressionController
			{
				LowHealthThreatMultiplier = 1.5f,
				LowResourceThreatMultiplier = 1.3f,
			};

			LogAssert.IsTrue(
				Mathf.Abs(aggression.MaximumVulnerabilityMultiplier - (1.5f * 1.3f)) < 1e-4f,
				"Both multipliers apply at once to a caster that is wounded AND out of mana, so the " +
				"bound must compound them exactly as GetThreatScore does. Taking only the larger " +
				$"would under-estimate that entry's score. Got {aggression.MaximumVulnerabilityMultiplier}.");
		}

		/// <summary>
		/// A multiplier tuned below one cannot turn the bound into an under-estimate.
		/// </summary>
		/// <remarks>
		/// The multipliers are designer-facing floats with no validation. A value below one is a
		/// plausible tuning choice ("de-prioritise the wounded"), and it would make a naive product
		/// smaller than the neutral 1 — so an entry at full health, whose real multiplier IS 1,
		/// would score above the bound and the guarantee would silently stop guaranteeing.
		/// </remarks>
		[Test]
		public void VulnerabilityBound_IsNeverBelowNeutral()
		{
			AggressionController aggression = new AggressionController
			{
				LowHealthThreatMultiplier = 0.5f,
				LowResourceThreatMultiplier = 0.25f,
			};

			LogAssert.IsTrue(aggression.MaximumVulnerabilityMultiplier >= 1f,
				"A character at full health and full mana is scored at exactly its raw points, so " +
				"the bound can never sit below 1 however the multipliers are tuned.");
		}

		/// <summary>
		/// The guaranteed raw points beat the worst case another entry could score.
		/// </summary>
		/// <remarks>
		/// The arithmetic <c>ApplyTauntAction</c> performs, exercised on a real table. The taunter is
		/// given the points the action would compute, and the assertion is the thing the player
		/// actually cares about: the taunter's SCORE leads, not merely its raw number.
		/// </remarks>
		[Test]
		public void TauntedRawPoints_LeadEveryReachableScore()
		{
			AggressionController aggression = new AggressionController
			{
				LowHealthThreatMultiplier = 1.5f,
				LowResourceThreatMultiplier = 1.3f,
			};

			const long taunter = 1L;
			const long woundedAlly = 2L;
			const float leadOverHighest = 100f;

			aggression.AddPoints(woundedAlly, 1000f);
			aggression.AddPoints(taunter, 50f);

			// What ApplyTauntAction computes. The taunter is at full health, so its own multiplier
			// is the neutral 1 and the division leaves the requirement unchanged.
			float highestRaw = aggression.GetHighestPoints(taunter);
			float ceilingScore = highestRaw * aggression.MaximumVulnerabilityMultiplier;
			float requiredPoints = (ceilingScore + leadOverHighest) / 1f;
			float required = requiredPoints - aggression.GetPoints(taunter);

			aggression.AddPoints(taunter, required);

			/* The ally's worst score, spelled out rather than taken from the property under test —
			 * otherwise a bound that under-estimates would be used on BOTH sides of the comparison
			 * and the test would agree with its own mistake. A wounded caster out of mana takes
			 * both multipliers. */
			float allyWorstScore = aggression.GetPoints(woundedAlly)
				* aggression.LowHealthThreatMultiplier
				* aggression.LowResourceThreatMultiplier;

			LogAssert.IsTrue(aggression.GetPoints(taunter) > allyWorstScore,
				$"The taunter holds {aggression.GetPoints(taunter)} raw against the ally's best " +
				$"possible score of {allyWorstScore}. Comparing raw against raw gave the taunter " +
				"1100 against a score of 1950, so the very next PickTarget went back to the ally.");

			LogAssert.IsTrue(aggression.GetPoints(taunter) > aggression.GetPoints(woundedAlly),
				"...and it still leads on raw points, so the taunt is not weaker than it was.");
		}

		#endregion

		#region Upstream — a clamp only runs against a settled maximum.

		/// <summary>
		/// A restored resource keeps its current value when its maximum comes from a modifier.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>SetResourceAttribute</c> applied the current value BEFORE the modifier, so the clamp in
		/// <c>SetCurrentValue</c> ran against a maximum that did not yet include it. A character at
		/// 950/1000 with 300 of that maximum from gear was clamped to its unbuffed 700, and the
		/// difference was gone for good: <c>UpdateValues</c> derives the MAXIMUM and never revisits
		/// the current value, so raising the maximum afterwards restores nothing.
		/// </para>
		/// <para>
		/// It survived only because <c>ClampCurrentValue</c> happens to decline to clamp while the
		/// character is not yet flagged <c>IsLoaded</c> — which on a client depends on whether the
		/// behaviour carrying <c>Flags</c> was read before this one. This test flags the character
		/// loaded precisely so the clamp is live and the ordering has to be right on its own.
		/// </para>
		/// </remarks>
		[Test]
		public void RestoredResource_KeepsItsCurrentValue_WhenTheMaximumComesFromAModifier()
		{
			CharacterAttributeController controller = host.GetComponent<CharacterAttributeController>()
				?? host.AddComponent<CharacterAttributeController>();
			LoadedMockCharacter character = new LoadedMockCharacter();
			character.EnableFlags(CharacterFlags.IsLoaded);
			controller.InitializeOnce(character);

			CharacterResourceAttribute health = new CharacterResourceAttribute(controller, template.ID, 700, 0f, 0);
			controller.AddResourceAttribute(health);

			// The observer shape: base 700, current 950, and 300 of maximum from gear.
			controller.SetResourceAttribute(template.ID, 700, 950f, 300);

			LogAssert.AreEqual(1000, health.FinalValue,
				"The modifier must be in before anything is clamped against the maximum.");
			LogAssert.AreEqual(950f, health.CurrentValue,
				"A character at 950/1000 must restore at 950. Clamping before the modifier landed " +
				"took it to its unbuffed 700 and nothing downstream ever put it back.");
		}

		/// <summary>
		/// The clamp is not skipped — it is merely deferred until the maximum is settled.
		/// </summary>
		/// <remarks>
		/// The counterpart to the test above, and the reason the fix is an ordering rule rather than
		/// an unclamped restore. A current value genuinely above the completed maximum must still
		/// come down; storing it raw and hoping something clamps it later would leave the resource
		/// permanently above its own maximum, because nothing in the graph pass revisits it.
		/// </remarks>
		[Test]
		public void RestoredResource_IsStillClamped_AgainstTheSettledMaximum()
		{
			CharacterAttributeController controller = host.GetComponent<CharacterAttributeController>()
				?? host.AddComponent<CharacterAttributeController>();
			LoadedMockCharacter character = new LoadedMockCharacter();
			character.EnableFlags(CharacterFlags.IsLoaded);
			controller.InitializeOnce(character);

			CharacterResourceAttribute health = new CharacterResourceAttribute(controller, template.ID, 700, 0f, 0);
			controller.AddResourceAttribute(health);

			controller.SetResourceAttribute(template.ID, 700, 5000f, 300);

			LogAssert.AreEqual(1000f, health.CurrentValue,
				"5000 of a 1000 maximum is not a value to preserve. Deferring the clamp must not " +
				"become skipping it, or a resource sits above its own maximum indefinitely.");
		}

		#endregion

		// ── Fixture ──────────────────────────────────────────────────────────────────

		private CharacterAttributeTemplate template;
		private GameObject host;

		[SetUp]
		public void CreateFixture()
		{
			template = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			template.name = "AuditRoundTwoAttribute";
			template.InitialValue = 100;
			template.AddToCache(template.name);

			host = new GameObject("AuditRoundTwoHost");
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

		/// <summary>An attribute wired to a real controller, so propagation behaves normally.</summary>
		private CharacterAttribute MakeAttribute(int baseValue)
		{
			CharacterAttributeController controller = host.GetComponent<CharacterAttributeController>()
				?? host.AddComponent<CharacterAttributeController>();
			return new CharacterAttribute(controller, template.ID, baseValue, 0);
		}

		/// <summary>
		/// The minimum <see cref="ICharacter"/> the resource clamp reads: it consults
		/// <c>Character.Flags</c> for <see cref="CharacterFlags.IsLoaded"/> and nothing else.
		/// </summary>
		private sealed class LoadedMockCharacter : ICharacter
		{
			public long ID { get; set; } = 1;
			public string Name => "LoadedMockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public FishNet.Connection.NetworkConnection Owner => null;
			public FishNet.Object.NetworkObject NetworkObject => null;
			public FishNet.Managing.Predicting.PredictionManager PredictionManager => null;
			public System.Collections.Generic.HashSet<FishNet.Connection.NetworkConnection> Observers { get; } =
				new System.Collections.Generic.HashSet<FishNet.Connection.NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			public WorldLabel CharacterNameLabel { get; set; }
			public WorldLabel CharacterGuildLabel { get; set; }
			public Transform MeshRoot => null;
#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif
			/* CharacterFlags is a SEQUENTIAL enum of bit POSITIONS (Idle = 0, IsMoving = 1, ...),
			 * not a bitmask of powers of two — IntBitExtensions.IsFlagged tests (flag & 1 << pos).
			 * Modelling it as a mask here made EnableFlags(IsLoaded) set an unrelated bit, the
			 * production clamp read false, and the ordering test below passed without the clamp
			 * ever running. Mirror the real semantics or the mock proves nothing. */
			public void EnableFlags(CharacterFlags flags) => Flags |= 1 << (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(1 << (int)flags);
			public bool IsFlagged(CharacterFlags flags) => (Flags & (1 << (int)flags)) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				control = null;
				return false;
			}
			public void Invoke(System.Collections.Generic.List<Trigger> triggers, EventData eventData) { }
		}
	}
}
