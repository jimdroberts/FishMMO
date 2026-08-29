using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the predict / confirm / reject cycle for combat numbers the caster draws itself.
	/// </summary>
	/// <remarks>
	/// Damage numbers used to arrive only with the server's combat report, so a player's own hit
	/// showed nothing for half a round trip. The caster now draws immediately and the server's
	/// report confirms; a prediction the report never matches is greyed out rather than removed.
	/// The tricky properties are the pairing rule and the fact that <b>absence</b> is the only
	/// rejection signal available — the server never knew a prediction happened.
	/// </remarks>
	[TestFixture]
	public class PredictedCombatEventTests
	{
		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<long> predicted = new List<long>();
		private readonly List<long> rejected = new List<long>();
		private readonly List<long> confirmed = new List<long>();

		/// <summary>
		/// The character doing the hitting. Every prediction has one, because confirmation pairs on
		/// the attacker as well as the victim — a combat report reaches every client observing the
		/// victim, so without the attacker any other player's hit would consume this one's prediction.
		/// </summary>
		private ICharacter attacker;

		[SetUp]
		public void SetUp()
		{
			PredictedCombatEvents.Clear();
			PredictedCombatEvents.ConfirmationWindowSeconds = 1.0f;
			predicted.Clear();
			rejected.Clear();
			confirmed.Clear();
			PredictedCombatEvents.OnPredicted += RecordPredicted;
			PredictedCombatEvents.OnPredictionRejected += RecordRejected;
			PredictedCombatEvents.OnPredictionConfirmed += RecordConfirmed;
			attacker = MakeCharacter("Attacker", objectId: 1);
		}

		[TearDown]
		public void TearDown()
		{
			PredictedCombatEvents.OnPredicted -= RecordPredicted;
			PredictedCombatEvents.OnPredictionRejected -= RecordRejected;
			PredictedCombatEvents.OnPredictionConfirmed -= RecordConfirmed;
			PredictedCombatEvents.Clear();

			for (int i = 0; i < gameObjects.Count; ++i)
			{
				if (gameObjects[i] != null)
				{
					Object.DestroyImmediate(gameObjects[i]);
				}
			}
			gameObjects.Clear();
		}

		private void RecordPredicted(long id, ICharacter target, int amount, PredictedCombatEvents.Kind kind, DamageAttributeTemplate dmg)
			=> predicted.Add(id);

		private void RecordRejected(long id) => rejected.Add(id);

		private void RecordConfirmed(long id) => confirmed.Add(id);

		/// <summary>A prediction must announce itself so the display can draw it at once.</summary>
		[Test]
		public void Predict_AnnouncesImmediately()
		{
			ICharacter target = MakeCharacter("PredictTarget", objectId: 5);

			PredictedCombatEvents.Predict(attacker, target, 42, PredictedCombatEvents.Kind.Damage, null, 0f);

			LogAssert.AreEqual(1, predicted.Count,
				"The number must be announced on the predicting tick; waiting for the server is the " +
				"delay this whole path exists to remove.");
			LogAssert.AreEqual(1, PredictedCombatEvents.PendingCount,
				"It must stay pending until the server's report confirms it.");
		}

		/// <summary>
		/// A matching report consumes the prediction so the number is not drawn twice.
		/// </summary>
		[Test]
		public void TryConfirm_ConsumesTheMatchingPrediction()
		{
			ICharacter target = MakeCharacter("ConfirmTarget", objectId: 7);
			PredictedCombatEvents.Predict(attacker, target, 20, PredictedCombatEvents.Kind.Damage, null, 0f);

			LogAssert.IsTrue(PredictedCombatEvents.TryConfirm(attacker, target, PredictedCombatEvents.Kind.Damage),
				"The report describes a hit already on screen; the display must skip it rather than " +
				"drawing a second number for one hit.");
			LogAssert.AreEqual(0, PredictedCombatEvents.PendingCount, "The prediction must be consumed.");

			PredictedCombatEvents.Sweep(100f);
			LogAssert.AreEqual(0, rejected.Count,
				"A confirmed prediction must never be rejected later.");
		}

		/// <summary>
		/// The amount is deliberately not part of the match.
		/// </summary>
		/// <remarks>
		/// Predicted and reported amounts agree whenever the deterministic RNG states agree, but a
		/// transient divergence would otherwise leave the prediction unmatched — producing the worst
		/// available outcome for a hit that landed: the predicted number greyed out AND the server's
		/// number drawn beside it.
		/// </remarks>
		[Test]
		public void TryConfirm_MatchesDespiteAnAmountMismatch()
		{
			ICharacter target = MakeCharacter("AmountTarget", objectId: 9);
			PredictedCombatEvents.Predict(attacker, target, 20, PredictedCombatEvents.Kind.Damage, null, 0f);

			LogAssert.IsTrue(PredictedCombatEvents.TryConfirm(attacker, target, PredictedCombatEvents.Kind.Damage),
				"A differing amount must still pair. Matching on the amount would grey out a hit that " +
				"actually landed and draw the server's number next to it.");
		}

		/// <summary>A report for a different character must not consume this one's prediction.</summary>
		[Test]
		public void TryConfirm_DoesNotMatchAnotherTarget()
		{
			ICharacter target = MakeCharacter("MineTarget", objectId: 11);
			ICharacter other = MakeCharacter("OtherTarget", objectId: 12);
			PredictedCombatEvents.Predict(attacker, target, 20, PredictedCombatEvents.Kind.Damage, null, 0f);

			LogAssert.IsFalse(PredictedCombatEvents.TryConfirm(attacker, other, PredictedCombatEvents.Kind.Damage),
				"A hit on somebody else must be drawn, not silently swallowed by an unrelated prediction.");
			LogAssert.AreEqual(1, PredictedCombatEvents.PendingCount, "The original prediction must survive.");
		}

		/// <summary>Damage and heal predictions must not confirm each other.</summary>
		[Test]
		public void TryConfirm_DoesNotMatchAcrossKinds()
		{
			ICharacter target = MakeCharacter("KindTarget", objectId: 13);
			PredictedCombatEvents.Predict(attacker, target, 20, PredictedCombatEvents.Kind.Damage, null, 0f);

			LogAssert.IsFalse(PredictedCombatEvents.TryConfirm(attacker, target, PredictedCombatEvents.Kind.Heal),
				"A heal report must not consume a damage prediction; the two are different numbers on " +
				"the same character and swallowing one hides a real event.");
		}

		/// <summary>An unconfirmed prediction is rejected once the window elapses.</summary>
		/// <remarks>
		/// The server never learns that a client predicted, so it cannot send a rejection. Absence
		/// is the only signal available.
		/// </remarks>
		[Test]
		public void Sweep_RejectsAnUnconfirmedPrediction()
		{
			ICharacter target = MakeCharacter("StaleTarget", objectId: 15);
			PredictedCombatEvents.Predict(attacker, target, 20, PredictedCombatEvents.Kind.Damage, null, 0f);

			PredictedCombatEvents.Sweep(0.5f);
			LogAssert.AreEqual(0, rejected.Count,
				"Inside the window the report may still be in flight; rejecting here would grey out " +
				"good hits on a laggy connection.");

			PredictedCombatEvents.Sweep(1.5f);
			LogAssert.AreEqual(1, rejected.Count,
				"Past the window the hit did not land and the number must be marked invalid.");
			LogAssert.AreEqual(0, PredictedCombatEvents.PendingCount, "A rejected entry must not linger.");
		}

		/// <summary>A rejection fires once, not on every sweep.</summary>
		[Test]
		public void Sweep_RejectsEachPredictionOnlyOnce()
		{
			ICharacter target = MakeCharacter("OnceTarget", objectId: 17);
			PredictedCombatEvents.Predict(attacker, target, 20, PredictedCombatEvents.Kind.Damage, null, 0f);

			PredictedCombatEvents.Sweep(2f);
			PredictedCombatEvents.Sweep(3f);

			LogAssert.AreEqual(1, rejected.Count,
				"Repeated sweeps must not re-reject; the display would recolour a recycled label.");
		}

		/// <summary>Oldest-first pairing when several predictions share a target.</summary>
		[Test]
		public void TryConfirm_ConsumesOldestFirst()
		{
			ICharacter target = MakeCharacter("MultiTarget", objectId: 19);
			PredictedCombatEvents.Predict(attacker, target, 10, PredictedCombatEvents.Kind.Damage, null, 0f);
			PredictedCombatEvents.Predict(attacker, target, 20, PredictedCombatEvents.Kind.Damage, null, 0.1f);

			LogAssert.IsTrue(PredictedCombatEvents.TryConfirm(attacker, target, PredictedCombatEvents.Kind.Damage));
			LogAssert.AreEqual(1, PredictedCombatEvents.PendingCount,
				"One report confirms one prediction; a burst must not be collapsed into a single match.");

			// Only the second is still outstanding, so only it can age out.
			PredictedCombatEvents.Sweep(2f);
			LogAssert.AreEqual(1, rejected.Count, "The unmatched prediction must still be rejectable.");
		}

		/// <summary>Clear drops predictions without greying out labels that no longer exist.</summary>
		[Test]
		public void Clear_DropsPendingWithoutRejecting()
		{
			ICharacter target = MakeCharacter("ClearTarget", objectId: 21);
			PredictedCombatEvents.Predict(attacker, target, 20, PredictedCombatEvents.Kind.Damage, null, 0f);

			PredictedCombatEvents.Clear();
			PredictedCombatEvents.Sweep(10f);

			LogAssert.AreEqual(0, PredictedCombatEvents.PendingCount, "Clear must empty the set.");
			LogAssert.AreEqual(0, rejected.Count,
				"A scene change removes the characters and their labels together; raising rejections " +
				"would ask the display to recolour labels that are already gone.");
		}

		/// <summary>A zero or negative amount is not a number worth drawing.</summary>
		[Test]
		public void Predict_IgnoresNonPositiveAmounts()
		{
			ICharacter target = MakeCharacter("ZeroTarget", objectId: 23);

			PredictedCombatEvents.Predict(attacker, target, 0, PredictedCombatEvents.Kind.Damage, null, 0f);
			PredictedCombatEvents.Predict(attacker, target, -5, PredictedCombatEvents.Kind.Damage, null, 0f);

			LogAssert.AreEqual(0, predicted.Count, "Fully resisted or zero-effective hits draw nothing.");
			LogAssert.AreEqual(0, PredictedCombatEvents.PendingCount, "And leave nothing pending.");
		}

		/// <summary>
		/// Another player's hit on the same target must not consume this client's prediction.
		/// </summary>
		/// <remarks>
		/// The regression this pins is the routine case, not an exotic one: a combat report is
		/// broadcast to everyone observing the VICTIM, so two players on one mob was enough. With the
		/// attacker left out of the match, the other player's report paired with this client's pending
		/// entry — the caller draws nothing on a match, so their real number vanished — and this
		/// client's own report then arrived to find nothing pending and was drawn a second time.
		/// </remarks>
		[Test]
		public void TryConfirm_DoesNotMatchAnotherAttackersReport()
		{
			ICharacter target = MakeCharacter("SharedTarget", objectId: 25);
			ICharacter otherAttacker = MakeCharacter("OtherAttacker", objectId: 26);

			PredictedCombatEvents.Predict(attacker, target, 120, PredictedCombatEvents.Kind.Damage, null, 0f);

			LogAssert.IsFalse(PredictedCombatEvents.TryConfirm(otherAttacker, target, PredictedCombatEvents.Kind.Damage),
				"Somebody else's damage on the same target must be drawn. Swallowing it hides their " +
				"number entirely and leaves this client's own report to be drawn twice.");
			LogAssert.AreEqual(1, PredictedCombatEvents.PendingCount,
				"This client's prediction must still be waiting for its OWN report.");

			LogAssert.IsTrue(PredictedCombatEvents.TryConfirm(attacker, target, PredictedCombatEvents.Kind.Damage),
				"And that report must still pair when it arrives.");
		}

		/// <summary>
		/// A prediction with no resolvable attacker is not recorded.
		/// </summary>
		/// <remarks>
		/// Same reasoning as the missing-target case: confirmation now matches on the source, so an
		/// entry without one could never be paired and would always end up greyed out. Drawing nothing
		/// beats drawing a number guaranteed to be marked wrong.
		/// </remarks>
		[Test]
		public void Predict_IgnoresAPredictionWithNoAttacker()
		{
			ICharacter target = MakeCharacter("NoSourceTarget", objectId: 27);

			PredictedCombatEvents.Predict(null, target, 30, PredictedCombatEvents.Kind.Damage, null, 0f);

			LogAssert.AreEqual(0, predicted.Count, "An unpairable prediction must not be announced.");
			LogAssert.AreEqual(0, PredictedCombatEvents.PendingCount, "And must leave nothing pending.");
		}

		/// <summary>
		/// Confirmation must be announced, so the display can release the handle it kept.
		/// </summary>
		/// <remarks>
		/// The display keeps one entry per prediction so it can grey that number out later. Only
		/// rejection used to be announced, so the entries for every prediction that turned out RIGHT —
		/// nearly all of them — accumulated for the whole session, each pinning a pooled label that had
		/// long since been recycled onto some other number.
		/// </remarks>
		[Test]
		public void TryConfirm_AnnouncesTheConfirmation_SoTheDisplayCanReleaseItsHandle()
		{
			ICharacter target = MakeCharacter("HandleTarget", objectId: 29);
			PredictedCombatEvents.Predict(attacker, target, 15, PredictedCombatEvents.Kind.Damage, null, 0f);

			LogAssert.AreEqual(1, predicted.Count, "One handle was issued.");
			LogAssert.IsTrue(PredictedCombatEvents.TryConfirm(attacker, target, PredictedCombatEvents.Kind.Damage));

			LogAssert.AreEqual(1, confirmed.Count,
				"The confirmed handle must be announced; without it the display can only ever free the " +
				"handles for predictions that were wrong.");
			LogAssert.AreEqual(predicted[0], confirmed[0],
				"And it must name the handle that was issued, or the display frees the wrong entry.");
			LogAssert.AreEqual(0, rejected.Count, "A confirmed prediction is never also rejected.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// A character stand-in whose network object id can be set.
		/// </summary>
		/// <remarks>
		/// The tracker pairs on <c>NetworkObject.ObjectId</c>, which an unspawned object reports as
		/// 0 — and 0 is the "no identity" case the tracker refuses. A real <c>NetworkObject</c> with
		/// its id assigned by reflection is what lets the pairing rules be tested at all.
		/// </remarks>
		private ICharacter MakeCharacter(string name, int objectId)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);

			FishNet.Object.NetworkObject nob = go.AddComponent<FishNet.Object.NetworkObject>();
			typeof(FishNet.Object.NetworkObject)
				.GetProperty("ObjectId")
				.SetValue(nob, objectId);

			return go.AddComponent<ProbeCombatCharacter>();
		}

		/// <summary>Minimal character exposing a real NetworkObject, which is all the tracker reads.</summary>
		private sealed class ProbeCombatCharacter : MonoBehaviour, ICharacter
		{
			public long ID { get; set; }
			public string Name => name;
			public Transform Transform => transform;
			public GameObject GameObject => gameObject;
			public Collider Collider { get; set; }
			public FishNet.Connection.NetworkConnection Owner => null;
			public FishNet.Object.NetworkObject NetworkObject => GetComponent<FishNet.Object.NetworkObject>();
			public FishNet.Managing.Predicting.PredictionManager PredictionManager => null;
			public HashSet<FishNet.Connection.NetworkConnection> Observers { get; } = new HashSet<FishNet.Connection.NetworkConnection>();
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
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
