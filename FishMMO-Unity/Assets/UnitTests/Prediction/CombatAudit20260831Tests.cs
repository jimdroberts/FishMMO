using System;
using System.Collections.Generic;
using System.IO;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regressions for the 2026-08-31 combat/prediction audit.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Seven defects, in three groups: a re-entrancy guard that did not cover the one call able to
	/// re-enter (<see cref="Mitigation_ReentrantRemove_StillReleasesEverySpentShield"/>); a
	/// deterministic-RNG rule that was stated nowhere and broken at seven call sites
	/// (<see cref="HitCount_AdvancesTheGeneratorEvenWhereItDoesNotSpendTheCount"/> and the source
	/// sweep beside it); and a capped selection that ran on two peers without an agreed order.
	/// </para>
	/// <para>
	/// What can be exercised for real is. What needs a spawned NetworkObject, a physics scene or two
	/// connected peers is asserted on the SOURCE instead — the idiom
	/// <c>BlockAndDeflectTests</c> and <c>PositionConditionTests</c> already use — and every such
	/// test says so in its remarks rather than pretending to be a behavioural one.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CombatAudit20260831Tests
	{
		private DamageNegationBuffTemplate firstShield;
		private DamageNegationBuffTemplate secondShield;
		private readonly List<GameObject> gameObjects = new List<GameObject>();

		[SetUp]
		public void SetUp()
		{
			firstShield = ScriptableObject.CreateInstance<DamageNegationBuffTemplate>();
			firstShield.name = "Audit0831_ShieldA";
			firstShield.Mode = DamageNegationMode.Absorb;
			firstShield.Amount = 50;
			firstShield.RequiresFacing = false;
			firstShield.Duration = 10f;
			firstShield.AddToCache(firstShield.name);

			secondShield = ScriptableObject.CreateInstance<DamageNegationBuffTemplate>();
			secondShield.name = "Audit0831_ShieldB";
			secondShield.Mode = DamageNegationMode.Absorb;
			secondShield.Amount = 50;
			secondShield.RequiresFacing = false;
			secondShield.Duration = 10f;
			secondShield.AddToCache(secondShield.name);
		}

		[TearDown]
		public void TearDown()
		{
			foreach (DamageNegationBuffTemplate t in new[] { firstShield, secondShield })
			{
				if (t == null)
				{
					continue;
				}
				t.RemoveFromCache();
				UnityEngine.Object.DestroyImmediate(t);
			}

			for (int i = 0; i < gameObjects.Count; ++i)
			{
				if (gameObjects[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(gameObjects[i]);
				}
			}
			gameObjects.Clear();
		}

		// ── F1: the re-entrancy guard must cover RemoveSpent ─────────────────────────

		/// <summary>
		/// A mitigation pass that empties two shields releases BOTH, even when removing the first
		/// re-enters mitigation on another character.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>DamageMitigation</c> collects the ids it emptied into a shared scratch list and lends
		/// that list out under <c>spentBuffsInUse</c>, so a nested pass allocates its own. The guard
		/// was released in the <c>finally</c> BEFORE <c>RemoveSpent</c> ran — and
		/// <c>RemoveSpent</c> is the one call in the method that can re-enter, because
		/// <c>BuffController.Remove</c> fires the buff-removed triggers and authored content there
		/// is free to deal damage.
		/// </para>
		/// <para>
		/// So the nested pass took the SHARED list and cleared it while the outer loop was still
		/// walking it: the outer loop saw <c>Count == 0</c>, exited, and the second emptied shield
		/// stayed on the character at zero charges until it expired. Worse, a nested pass that
		/// refilled the list had ITS ids removed from the outer defender.
		/// </para>
		/// <para>
		/// Reverting the fix (moving <c>RemoveSpent</c> back below the <c>finally</c>) fails this
		/// with one shield left behind.
		/// </para>
		/// </remarks>
		[Test]
		public void Mitigation_ReentrantRemove_StillReleasesEverySpentShield()
		{
			MitigationCharacter defender = NewCharacter("Audit0831_Defender");
			MitigationCharacter bystander = NewCharacter("Audit0831_Bystander");

			GiveShield(defender, firstShield);
			GiveShield(defender, secondShield);
			GiveShield(bystander, firstShield);

			/* The on-remove trigger, modelled: removing the defender's first shield deals damage to
			 * a bystander, which lands back in Negate. Fired once so the test describes one authored
			 * proc rather than a runaway. */
			bool reentered = false;
			defender.Buffs.OnRemove = _ =>
			{
				if (reentered)
				{
					return;
				}
				reentered = true;
				DamageMitigation.Negate(bystander, null, 10, mutate: true);
			};

			int survived = DamageMitigation.Negate(defender, null, 100, mutate: true);

			LogAssert.IsTrue(reentered,
				"The nested mitigation pass must actually have run, or this test proves nothing.");
			LogAssert.AreEqual(0, survived,
				"Two 50-point pools absorb a 100-point hit whole.");
			LogAssert.AreEqual(0, defender.Buffs.Buffs.Count,
				"BOTH emptied shields must be released. A shield left behind at zero charges is the " +
				"defect: the guard has to stay held across RemoveSpent, because RemoveSpent is what " +
				"re-enters.");
		}

		/// <summary>
		/// The nested pass gets its own scratch list, so it cannot disturb the outer one — and the
		/// outer pass cannot disturb the nested one either.
		/// </summary>
		/// <remarks>
		/// The complement of the test above: it asserts the defect's other half, that a nested pass
		/// which empties a shield of its OWN still releases it rather than having its ids consumed
		/// by whichever pass owns the shared list.
		/// </remarks>
		[Test]
		public void Mitigation_ReentrantRemove_NestedPassReleasesItsOwnShield()
		{
			MitigationCharacter defender = NewCharacter("Audit0831_Defender2");
			MitigationCharacter bystander = NewCharacter("Audit0831_Bystander2");

			GiveShield(defender, firstShield);
			GiveShield(defender, secondShield);
			GiveShield(bystander, firstShield);

			bool reentered = false;
			defender.Buffs.OnRemove = _ =>
			{
				if (reentered)
				{
					return;
				}
				reentered = true;
				// Big enough to empty the bystander's 50-point pool outright.
				DamageMitigation.Negate(bystander, null, 500, mutate: true);
			};

			DamageMitigation.Negate(defender, null, 100, mutate: true);

			LogAssert.IsTrue(reentered, "The nested pass must have run.");
			LogAssert.AreEqual(0, bystander.Buffs.Buffs.Count,
				"The nested pass emptied the bystander's shield, so the nested pass must release it.");
			LogAssert.AreEqual(0, defender.Buffs.Buffs.Count,
				"And the outer pass must still release both of its own.");
		}

		// ── F5: the generator advances on every peer, or on none ─────────────────────

		/// <summary>
		/// A value provider is evaluated before the peer gate, so the ability object's generator
		/// advances identically on a peer that will not act on the result.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>AbilityObject.RNG</c> is shared by every action in the object's event chains and is
		/// advanced by side effect, so a draw that happens on one peer and not another
		/// desynchronises everything drawn after it. <c>AbilityHitCountAction</c> gated on
		/// <c>ResolvesHitsLocally</c> and evaluated its provider AFTER — so on an observer (which
		/// runs the chain from the server's hit report but spends no count) the draw never happened,
		/// and the next ungated action in the chain — <c>AbilityForkHitAction</c> — read a different
		/// number and sent that copy of the projectile down a heading the server never took.
		/// </para>
		/// <para>
		/// A bare <see cref="AbilityObject"/> with no caster answers <c>ResolvesHitsLocally</c>
		/// false, which is exactly the observer case. Reverting the fix leaves the generator
		/// untouched and fails this.
		/// </para>
		/// </remarks>
		[Test]
		public void HitCount_AdvancesTheGeneratorEvenWhereItDoesNotSpendTheCount()
		{
			MitigationCharacter caster = NewCharacter("Audit0831_Caster");
			AbilityObject abilityObject = NewAbilityObject("Audit0831_Projectile");

			LogAssert.IsFalse(abilityObject.ResolvesHitsLocally,
				"An object with no caster must answer false — that is the observer case this pins.");

			DeterministicRNG rng = new DeterministicRNG(20260831);
			rng.CaptureState(out uint before0, out uint before1, out uint before2, out uint before3);

			AbilityHitCountAction action = new AbilityHitCountAction
			{
				AmountValue = new RandomRangeValue { Min = 1, Max = 6 },
			};

			int hitCountBefore = abilityObject.HitCount;
			AbilityCollisionEventData collision = new AbilityCollisionEventData(
				caster, caster, abilityObject, Vector3.zero, Vector3.up, rng);

			action.Execute(caster, collision);

			rng.CaptureState(out uint after0, out uint after1, out uint after2, out uint after3);

			LogAssert.IsTrue(
				before0 != after0 || before1 != after1 || before2 != after2 || before3 != after3,
				"The provider must be evaluated BEFORE the peer gate, so the shared generator " +
				"advances on every peer that runs the event chain. Drawing behind the gate leaves " +
				"an observer's generator a draw behind the server's for the object's whole life.");

			LogAssert.AreEqual(hitCountBefore, abilityObject.HitCount,
				"The gate still does its job: a peer that does not resolve hits must not move the " +
				"hit count. Only the DRAW is unconditional, never the effect.");
		}

		/// <summary>
		/// Every action that gates on a peer evaluates its value providers before that gate.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The rule <see cref="HitCount_AdvancesTheGeneratorEvenWhereItDoesNotSpendTheCount"/>
		/// pins behaviourally, applied to the whole set. Only two providers actually consume the
		/// generator today (<c>RandomRangeValue</c>, <c>RandomRangeFloatValue</c>) and neither is
		/// authored on any asset yet — so every one of these sites is latent, and a behavioural test
		/// per site would need a spawned NetworkObject to make the gate answer false. Asserting the
		/// ORDER in the source is what stops the next edit re-introducing it.
		/// </para>
		/// <para>
		/// Matched on the exact gate statement rather than on the type name, because every one of
		/// these files also names its gate in prose above the code.
		/// </para>
		/// </remarks>
		[Test]
		public void GatedActions_EvaluateValueProvidersBeforeTheirPeerGate()
		{
			const string actions = "Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/";

			AssertDrawsBeforeGate(actions + "ApplyDamageAction.cs",
				"if (!EcaAuthority.MayPredict(initiator, eventData))", "DamageValue.GetValue(");
			AssertDrawsBeforeGate(actions + "ApplyHealAction.cs",
				"if (!EcaAuthority.MayPredict(initiator, eventData))", "HealValue.GetValue(");
			AssertDrawsBeforeGate(actions + "ConsumeResourceAction.cs",
				"if (!EcaAuthority.MayPredict(initiator, eventData))", "AmountValue.GetValue(");
			AssertDrawsBeforeGate(actions + "KnockbackHitAction.cs",
				"if (!EcaAuthority.IsServer(initiator, eventData))", "ForceValue.GetValue(");
			AssertDrawsBeforeGate(actions + "Ability/AbilityApplyAreaAction.cs",
				"if (!abilityObject.IsServer)", "MaxHitsValue.GetValue(");
			AssertDrawsBeforeGate(actions + "Ability/AbilityApplyAreaAction.cs",
				"if (!abilityObject.IsServer)", "RadiusValue.GetValue(");
			AssertDrawsBeforeGate(actions + "Ability/AbilityApplyHitscanAction.cs",
				"if (!abilityObject.IsServer)", "RangeValue.GetValue(");
			AssertDrawsBeforeGate(actions + "Ability/AbilityApplyHitscanAction.cs",
				"if (!abilityObject.IsServer)", "MaxHitsValue.GetValue(");
			AssertDrawsBeforeGate(actions + "Ability/AbilityHitCountAction.cs",
				"if (!abilityObject.ResolvesHitsLocally)", "AmountValue.GetValue(");
		}

		// ── F4: a deflection is quantised before it is predicted ─────────────────────

		/// <summary>
		/// The heading a deflected object leaves on is the one the wire can carry, on every peer.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The peer that DECIDES a deflection computes a raw <c>Vector3.Reflect</c>; what every
		/// other peer receives is that vector through <c>AimDirectionCompression</c>. Applying the
		/// raw one had the server and the caster's client re-anchor the closed-form trajectory on a
		/// heading the wire cannot carry while observers flew the decoded one — the
		/// quantise-at-the-producer rule <c>KCCPlayer.PopulateInput</c> follows for aim, broken at
		/// the one place the deflect path still touched it.
		/// </para>
		/// <para>
		/// Asserted through <c>ApplyObservedDeflection</c>, which shares <c>ApplyDeflection</c> with
		/// the deciding path. Reverting the fix leaves the applied heading sitting on the raw
		/// vector and fails this.
		/// </para>
		/// </remarks>
		[Test]
		public void Deflection_AppliesTheQuantisedHeadingRatherThanTheRawOne()
		{
			// Deliberately off the quantisation grid; the precondition below refuses to let this
			// test pass vacuously if that ever stops being true.
			Vector3 raw = new Vector3(0.371f, 0.113f, -0.921f).normalized;
			Vector3 quantised = AimDirectionCompression.Quantize(raw);

			float quantisationError = Vector3.Distance(raw, quantised);
			LogAssert.IsTrue(quantisationError > 1e-5f,
				$"The chosen heading must not already sit on the grid, or this test asserts nothing " +
				$"(error was {quantisationError}). Pick another vector.");

			AbilityObject abilityObject = NewAbilityObject("Audit0831_Deflected");
			abilityObject.ApplyObservedDeflection(raw);

			Vector3 applied = abilityObject.SpawnRotation * Vector3.forward;

			LogAssert.IsTrue(Vector3.Distance(applied, quantised) < 1e-5f,
				"The applied heading must be the QUANTISED one — the value every other peer decodes " +
				"from the wire. Applying the raw reflect makes the deciding peer fly a heading no " +
				"receiver can reproduce, and the closed form evaluates the whole new leg from it.");
		}

		// ── F2 / F3: the shield sweep borrows every buffer, and orders before it caps ──

		/// <summary>
		/// <c>ShieldInterceptAction</c> lends out its query array under the same borrow as its
		/// lists.
		/// </summary>
		/// <remarks>
		/// The loop that walks the array destroys ability objects, whose OnDestroy events are
		/// authored content free to re-enter this same serialized instance — one asset serves every
		/// character that casts the ability. The two scratch LISTS were given the borrow and the
		/// array was missed, so a nested pass re-queried into it while the outer loop was still
		/// walking it under its own stale count. Source-asserted: reproducing it needs a physics
		/// scene, two spawned ability objects and an authored OnDestroy trigger.
		/// </remarks>
		[Test]
		public void ShieldIntercept_BorrowsItsQueryBufferAlongsideItsLists()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/Ability/ShieldInterceptAction.cs");

			int sweepOne = source.IndexOf("private void SweepOne(", StringComparison.Ordinal);
			LogAssert.IsTrue(sweepOne >= 0, "SweepOne was not found; this test needs updating.");

			string execute = source.Substring(0, sweepOne);
			string sweep = source.Substring(sweepOne);

			LogAssert.IsTrue(execute.Contains("hits ??= new Collider["),
				"The query array must be borrowed in Execute alongside the lists — that is what puts " +
				"it under the same inUse guard.");
			LogAssert.IsFalse(sweep.Contains("new Collider["),
				"The sweep must not allocate a query array of its own. Allocating there put it " +
				"outside the borrow, so a nested pass — reached through an intercepted object's " +
				"OnDestroy events — re-queried into the array the outer loop was still walking.");
			LogAssert.IsFalse(sweep.Contains("ref hits)"),
				"The sweep must grow the BORROWED buffer, never the shared field directly.");
			LogAssert.IsTrue(source.Contains("ref Collider[] hitBuffer"),
				"SweepOne must take the borrowed buffer by ref so growth belongs to one pass.");
			LogAssert.IsTrue(source.Contains("hits = hitBuffer;"),
				"And the owning pass must write the grown array back, or every pass re-grows from " +
				"the starting size — the reallocate-on-mismatch shape that undoes the last query's " +
				"growth.");
		}

		/// <summary>
		/// The shield sweep orders its candidates before <c>MaxIntercepts</c> truncates them.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the one capped selection in the project that runs on the server AND on a
		/// predicting client (<c>EcaAuthority.MayPredict</c>) without a rewind scope to make the two
		/// worlds identical. A cap applied to raw broadphase order therefore let the two peers stop
		/// DIFFERENT projectiles, and nothing corrects that because each peer's intercept succeeded
		/// on its own terms: the blocker watches two arrows die on the shield and then takes damage
		/// from both of them.
		/// </para>
		/// <para>
		/// Distance from the shield centre, with <c>TargetOrdering</c>'s identity tiebreak, is
		/// peer-agreed here — an ability object's position is a closed form both peers evaluate
		/// identically, and the blocker's own position is what its client predicts and reconciles.
		/// Source-asserted for the same reason as the test above.
		/// </para>
		/// </remarks>
		[Test]
		public void ShieldIntercept_OrdersCandidatesBeforeApplyingTheCap()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/Ability/ShieldInterceptAction.cs");

			int sort = source.IndexOf("TargetOrdering.SortByDistance(rankBuffer)", StringComparison.Ordinal);
			int cappedWalk = source.IndexOf("keyBuffer.Count < cap; ++i)", StringComparison.Ordinal);

			LogAssert.IsTrue(sort >= 0,
				"The candidates must be ranked. Truncating broadphase order lets the server and the " +
				"predicting blocker stop different projectiles.");
			LogAssert.IsTrue(cappedWalk >= 0, "The capped walk was not found; this test needs updating.");
			LogAssert.IsTrue(sort < cappedWalk,
				"The sort must come BEFORE the capped walk — a cap is only meaningful over an " +
				"ordered set, which is the whole rule TargetOrdering exists to state.");
		}

		// ── F7: knockback is refused rather than stored ──────────────────────────────

		/// <summary>
		/// A victim whose motor will not run this tick is not knocked back at all.
		/// </summary>
		/// <remarks>
		/// <c>KCCPlayer.OnReplicate</c> returns before <c>Motor.UpdatePhase1/2</c> for an
		/// incapacitated or dead character, and <c>BaseVelocity</c> is motor state carried in the
		/// reconcile — so an impulse written for such a victim was not lost, it was STORED, and
		/// fired as one lurch on the tick the stun ended. Source-asserted: the behavioural version
		/// needs a spawned <c>IPlayerCharacter</c> with a live <c>KinematicCharacterMotor</c>.
		/// </remarks>
		[Test]
		public void Knockback_RefusesAVictimWhoseMotorWillNotRun()
		{
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/KnockbackHitAction.cs");

			int guard = source.IndexOf("CharacterIncapacitation.IsIncapacitated(character)", StringComparison.Ordinal);
			int impulse = source.IndexOf("motor.BaseVelocity = currentVelocity", StringComparison.Ordinal);

			LogAssert.IsTrue(guard >= 0,
				"An incapacitated victim must be refused, or the impulse is stored in motor state " +
				"and released when the crowd control breaks.");
			LogAssert.IsTrue(source.Contains("!defenderDamageController.IsAlive"),
				"A dead victim must be refused for the same reason, tested on the replicated health " +
				"value rather than CharacterFlags.IsDead — flags ride the spawn payload and go stale.");
			LogAssert.IsTrue(impulse >= 0, "The impulse write was not found; this test needs updating.");
			LogAssert.IsTrue(guard < impulse, "The guard must precede the impulse.");
		}

		// ── F6: the non-capping selectors do not claim a cap ─────────────────────────

		/// <summary>
		/// The three selectors that apply no <c>MaxHits</c> cap do not describe one.
		/// </summary>
		/// <remarks>
		/// Chain bounds its walk with <c>ChainLength</c>; Nearest and Furthest emit exactly one
		/// target. All three had a copy-pasted comment block describing "the MaxHits cap below" and
		/// a buffer "wide enough that the cap is applied by this selector" — contradicting their own
		/// <c>MaxHits</c> field docs, which correctly call it a sizing hint. A maintainer reading
		/// the block concludes the cap is missing and adds one, turning a single-winner selector
		/// into a truncating one.
		/// </remarks>
		[Test]
		public void NonCappingSelectors_DoNotDescribeACapTheyDoNotApply()
		{
			const string targets = "Assets/Scripts/Shared/Implementation/Entity/ECA/Target/";

			foreach (string name in new[]
			{
				"ChainTargetSelector.cs",
				"NearestTargetSelector.cs",
				"FurthestTargetSelector.cs",
			})
			{
				string source = ReadSource(targets + name);

				LogAssert.IsFalse(source.Contains("cap is applied by this selector"),
					$"{name} applies no cap, so it must not claim the buffer exists to let it apply one.");
				LogAssert.IsFalse(source.Contains("the ranking and the MaxHits cap"),
					$"{name} has no MaxHits cap below its query loop.");
				LogAssert.IsFalse(source.Contains("Deliberately wider than the cap"),
					$"{name} has no cap to be wider than.");
				LogAssert.IsTrue(source.Contains("TargetOrdering.TryGrowQueryBuffer"),
					$"{name} must still grow its buffer — a truncated query loses candidates whether " +
					"or not a cap reads the result.");
			}
		}

		// ── F8: the RNG rule reaches the SELECTORS, not just the actions ─────────────

		/// <summary>
		/// A server-only consumer never draws from the event's shared generator.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The first gate sweep fixed every ACTION that drew behind a peer gate and stopped there,
		/// so two server-only consumers reaching the shared stream by a different route survived it.
		/// <c>RandomTargetSelector</c> is gated wholesale by
		/// <c>TargetSelector.IsAuthoritativePeer</c> — it cannot hoist its draw above the gate the
		/// way an action can, because the gate IS the selector — and it took <c>eventData.RNG</c>
		/// whenever one had been threaded on, which is every ability event.
		/// <c>ApplyDispelAction</c> is worse: its <c>RemoveRandom</c> loop draws a VARIABLE number
		/// of times, so the two streams do not merely differ by one draw.
		/// </para>
		/// <para>
		/// Either one leaves <c>AbilityForkHitAction</c> — ungated, and run by every peer that
		/// simulates the object — reading a different number on the peers that never ran the
		/// server-only step, putting an observer's copy of a forking projectile on a heading the
		/// server never took, permanently, from its first hit.
		/// </para>
		/// </remarks>
		[Test]
		public void ServerOnlyConsumers_DoNotDrawFromTheSharedEventGenerator()
		{
			string selector = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Target/RandomTargetSelector.cs");

			LogAssert.IsFalse(selector.Contains("return eventData.RNG;"),
				"RandomTargetSelector is server-only, so consuming the event's shared generator " +
				"advances it on the server alone and desynchronises every later ungated draw in " +
				"the same chain. It must take its own stream instead.");
			LogAssert.IsFalse(selector.Contains("eventData.HasExplicitRNG"),
				"Branching on HasExplicitRNG is what routed the ability events — the only ones " +
				"where the desynchronisation bites — onto the shared stream.");
			LogAssert.IsTrue(selector.Contains("eventData.IndependentRNG(RandomSelectionSalt)"),
				"The selector must draw from its own memoised stream.");

			string dispel = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/ApplyDispelAction.cs");

			LogAssert.IsTrue(dispel.Contains("eventData.IndependentRNG(DispelSelectionSalt)"),
				"The dispel must draw from its own memoised stream.");
			LogAssert.IsFalse(dispel.Contains("? eventData.RNG"),
				"ApplyDispelAction is server-only and draws a variable number of times, so it must " +
				"not touch the shared generator at all.");
		}

		/// <summary>
		/// <c>EventData.IndependentRNG</c> is memoised per (event chain, salt) and leaves the
		/// shared generator alone.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The three properties the fixes above rest on, and the middle one is why the obvious
		/// version of this fix was wrong. <c>DeriveRNG</c> is a pure factory that returns a FRESH
		/// generator per call, so routing the two consumers through it made the dispel's loop draw
		/// the same index every iteration and made two selectors sharing a salt pick the same
		/// target. Memoising is the whole difference.
		/// </para>
		/// <para>
		/// The fork case matters because a fan-out is one event: a selector drawing on the parent
		/// and an action drawing on a per-candidate fork must continue one sequence, or the fork's
		/// first draw repeats the parent's.
		/// </para>
		/// </remarks>
		[Test]
		public void IndependentRNG_IsMemoisedPerChainAndLeavesTheSharedStreamUntouched()
		{
			const int salt = 0x4453_504C;

			// 1. It does not advance the shared generator.
			EventData shared = new EventData((ICharacter)null) { RNG = new DeterministicRNG(12345) };
			EventData control = new EventData((ICharacter)null) { RNG = new DeterministicRNG(12345) };
			for (int i = 0; i < 8; ++i)
			{
				shared.IndependentRNG(salt).Next(0, 100);
			}
			LogAssert.AreEqual(control.RNG.Next(0, 100), shared.RNG.Next(0, 100),
				"Drawing from an independent stream must not advance the event's shared generator " +
				"— that is the entire reason the stream exists.");

			// 2. It ADVANCES across calls, where DeriveRNG would restart. This is the property whose
			//    absence made the first version of the fix wrong.
			EventData advancing = new EventData((ICharacter)null);
			int firstDraw = advancing.IndependentRNG(salt).Next(0, 1000000);
			int secondDraw = advancing.IndependentRNG(salt).Next(0, 1000000);
			int freshDraw = advancing.DeriveRNG(salt).Next(0, 1000000);
			LogAssert.AreEqual(firstDraw, freshDraw,
				"A fresh DeriveRNG stream restarts at the first value — the factory behaviour the " +
				"memoised accessor deliberately does not have.");
			LogAssert.AreNotEqual(firstDraw, secondDraw,
				"Two draws on the memoised stream must advance it. If they do not, a dispel loop " +
				"strips the same buff repeatedly and two selectors pick the same target.");

			// 3. A fork continues the chain's sequence rather than restarting it.
			EventData root = new EventData((ICharacter)null);
			int rootDraw = root.IndependentRNG(salt).Next(0, 1000000);
			int forkDraw = root.Fork(null).IndependentRNG(salt).Next(0, 1000000);
			LogAssert.AreNotEqual(rootDraw, forkDraw,
				"A fork shares the chain's stream, so its first draw continues the sequence. A fork " +
				"with a stream of its own would repeat the root's draw on every candidate.");

			// 4. Two salts are two streams.
			LogAssert.AreNotEqual(
				new EventData((ICharacter)null).IndependentRNG(0x4453_504C).Next(0, 1000000),
				new EventData((ICharacter)null).IndependentRNG(0x5241_4E44).Next(0, 1000000),
				"Two salts must give two streams, or two server-only consumers of one event would " +
				"draw the same numbers.");
		}

		// ── F8: the gate sweep's stragglers ──────────────────────────────────────────

		/// <summary>
		/// The three actions the 2026-08-31 gate sweep missed evaluate their providers first.
		/// </summary>
		/// <remarks>
		/// Same rule, same shape as the sites that sweep did fix — these were passed over because
		/// they gate on <c>EcaAuthority.IsServer</c> rather than <c>MayPredict</c>, and because in
		/// each of them a peer-varying guard (a target resolution, a controller lookup) sat between
		/// the gate and the draw, so hoisting past the gate alone would not have been enough. An
		/// achievement controller in particular is a server-side component, so a client returned
		/// before drawing even with the gate cleared.
		/// </remarks>
		[Test]
		public void GateSweepStragglers_EvaluateValueProvidersBeforeTheirPeerGate()
		{
			const string actions = "Assets/Scripts/Shared/Implementation/Entity/ECA/Actions/Character/";

			AssertDrawsBeforeGate(actions + "ApplyDispelAction.cs",
				"EcaAuthority.IsServer", "AmountToRemoveValue.GetValue");
			AssertDrawsBeforeGate(actions + "ApplyReviveAction.cs",
				"EcaAuthority.IsServer", "ReviveValue.GetValue");
			AssertDrawsBeforeGate(actions + "Achievement/AchievementIncrementAction.cs",
				"EcaAuthority.IsServer", "AmountValue.GetValue");

			// And past the peer-varying guards that sat between the gate and the draw, which is the
			// half a plain hoist above the gate would have missed.
			string achievement = ReadSource(actions + "Achievement/AchievementIncrementAction.cs");
			LogAssert.IsTrue(
				achievement.IndexOf("AmountValue.GetValue", StringComparison.Ordinal) <
				achievement.IndexOf("TryGet(out IAchievementController", StringComparison.Ordinal),
				"The draw must precede the controller lookup: an achievement controller is a " +
				"server-side component, so a client returns there and never draws.");

			string revive = ReadSource(actions + "ApplyReviveAction.cs");
			LogAssert.IsTrue(
				revive.IndexOf("ReviveValue.GetValue", StringComparison.Ordinal) <
				revive.IndexOf("TryResolveTargetOrInitiator", StringComparison.Ordinal),
				"The draw must precede the target resolution, which can answer differently per peer.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static void AssertDrawsBeforeGate(string relativePath, string gate, string draw)
		{
			string source = ReadSource(relativePath);

			int gateIndex = source.IndexOf(gate, StringComparison.Ordinal);
			int drawIndex = source.IndexOf(draw, StringComparison.Ordinal);

			LogAssert.IsTrue(gateIndex >= 0, $"{relativePath}: gate '{gate}' not found; this test needs updating.");
			LogAssert.IsTrue(drawIndex >= 0, $"{relativePath}: draw '{draw}' not found; this test needs updating.");
			LogAssert.IsTrue(drawIndex < gateIndex,
				$"{relativePath}: '{draw}' must be evaluated before '{gate}'. A provider may consume " +
				"the ability object's DeterministicRNG, which every action in the event chain shares, " +
				"so drawing behind a peer gate advances it only on the peers that pass. See " +
				"AbilityObject.RNG.");
		}

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		private MitigationCharacter NewCharacter(string name)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);
			return new MitigationCharacter(go);
		}

		private AbilityObject NewAbilityObject(string name)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);
			return go.AddComponent<AbilityObject>();
		}

		private static Buff GiveShield(MitigationCharacter character, DamageNegationBuffTemplate template)
		{
			Buff buff = new Buff(template.ID, 100u, 1f / 30f);
			character.Buffs.Buffs[template.ID] = buff;
			return buff;
		}

		/// <summary>
		/// A buff container with a real dictionary and a hook on <see cref="Remove"/>, so a test can
		/// model the authored buff-removed trigger that makes <c>RemoveSpent</c> re-entrant.
		/// </summary>
		private sealed class StubBuffController : IBuffController
		{
			/// <summary>Invoked from <see cref="Remove"/>, standing in for the removed-buff triggers.</summary>
			public Action<int> OnRemove;

			public ICharacter Character => null;
			public bool Initialized => true;
			public List<Trigger> OnBuffApplyTriggers { get; } = new List<Trigger>();
			public List<Trigger> OnBuffRemoveTriggers { get; } = new List<Trigger>();
			public SortedDictionary<int, Buff> Buffs { get; } = new SortedDictionary<int, Buff>();
			public bool SimulatesBuffEffects => true;
			public void MarkBuffStateDirty() { }
			public IReadOnlyList<ObservedBuffEntry> ObservedBuffs { get; } = new List<ObservedBuffEntry>();

			public void InitializeOnce(ICharacter character) { }
			public void OnStartCharacter() { }
			public void OnStopCharacter() { }
			public uint GetCurrentDomainTick() => 100u;
			public void Tick(uint currentTick) { }
			public void Apply(BaseBuffTemplate template, PredictionTick currentTick, ICharacter caster = null) { }
			public void ApplyAuthoritative(BaseBuffTemplate template, uint serverTick, ICharacter caster = null) { }
			public uint ResolveAuthoritativeTick(uint serverTick) => serverTick;
			public void Apply(Buff buff, bool suppressFX = false) { }

			public void Remove(int buffID)
			{
				Buffs.Remove(buffID);
				OnRemove?.Invoke(buffID);
			}

			public void RemoveRandom(DeterministicRNG rng, bool includeBuffs = false, bool includeDebuffs = false) { }
			public void RemoveAll(bool ignoreInvokeRemove = false, bool includePermanent = false, bool preserveFX = false) { }
			public BuffReconcileEntry[] CreateReconcileSnapshot() => null;
			public void RestoreFromReconcile(BuffReconcileEntry[] entries, uint reconcileTick) { }
		}

		/// <summary>A character with a real transform and a hookable buff container.</summary>
		private sealed class MitigationCharacter : ICharacter
		{
			public readonly StubBuffController Buffs = new StubBuffController();

			public MitigationCharacter(GameObject gameObject)
			{
				GameObject = gameObject;
				Transform = gameObject.transform;
			}

			public long ID { get; set; }
			public string Name => GameObject != null ? GameObject.name : "MitigationCharacter";
			public Transform Transform { get; }
			public GameObject GameObject { get; }
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
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
			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }

			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				control = Buffs as T;
				return control != null;
			}

			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
