using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Guards for the parts of target selection whose answer must not depend on the peer, the run,
	/// or the order the physics broadphase happened to fill a buffer in.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Everything here exercises the extracted pure functions rather than the selectors themselves.
	/// That is deliberate: a selector needs a live <c>NetworkManager</c>, a populated
	/// <c>LagCompensationRegistry</c> and a physics scene before it will answer at all, and none of
	/// those can be stood up in EditMode. The rules the audit found broken — ranking against the
	/// world the query ran in, a total order before any cap, a reproducible seed, a peer gate — are
	/// all decidable from numbers, so they are expressed as functions and pinned here.
	/// </para>
	/// <para>
	/// The one exception is the cone test, which is exercised through real transforms because the
	/// bug it guards (a caster selecting itself out of its own cone) was a property of vector
	/// arithmetic on a degenerate input rather than of any ordering.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class EcaDeterminismTests
	{
		// ── Ranking reads the world the query ran in ─────────────────────────────────

		/// <summary>
		/// Nearest is decided by the distances handed to the ranker, not by list position.
		/// </summary>
		/// <remarks>
		/// This is the shape of the rewind bug in miniature. The selectors used to build their
		/// candidate list from a rewound query and then measure distances after the scope had closed,
		/// so the two halves described different worlds. Ranking is now a function of the distances
		/// captured inside the scope; feeding it rewound distances and live distances for the same
		/// candidates must pick different winners, or the distinction would not be load-bearing.
		/// </remarks>
		[Test]
		public void NearestIndex_RanksByTheDistancesItIsGiven_NotByListOrder()
		{
			// Two candidates. In the rewound world A is closer; in the live world the peers have
			// moved and B is closer. Same list, same order, different answer.
			List<TargetRank> rewound = new List<TargetRank>
			{
				new TargetRank(0, 10, 0, 0, 2.0f),
				new TargetRank(1, 11, 0, 0, 5.0f),
			};
			List<TargetRank> live = new List<TargetRank>
			{
				new TargetRank(0, 10, 0, 0, 6.5f),
				new TargetRank(1, 11, 0, 0, 1.5f),
			};

			int rewoundPick = TargetOrdering.NearestIndex(rewound);
			int livePick = TargetOrdering.NearestIndex(live);

			TestContext.WriteLine($"MEASURE nearest: rewound→{rewoundPick} live→{livePick}");

			LogAssert.AreEqual(0, rewoundPick, "In the caster's view of the world, the first candidate is nearest.");
			LogAssert.AreEqual(1, livePick,
				"Ranking against live positions picks a different character — which is exactly what the selectors did before the ranking moved inside the rewind scope.");
		}

		/// <summary>Furthest is the mirror of nearest, and reads the same distances.</summary>
		[Test]
		public void FurthestIndex_PicksTheGreatestDistance()
		{
			List<TargetRank> ranks = new List<TargetRank>
			{
				new TargetRank(0, 10, 0, 0, 2.0f),
				new TargetRank(1, 11, 0, 0, 9.0f),
				new TargetRank(2, 12, 0, 0, 5.0f),
			};

			LogAssert.AreEqual(1, TargetOrdering.FurthestIndex(ranks), "The greatest distance wins.");
			LogAssert.AreEqual(0, TargetOrdering.NearestIndex(ranks), "The least distance wins.");
		}

		/// <summary>Both rankers refuse an empty set rather than returning a usable index.</summary>
		[Test]
		public void RankersReturnNoIndex_ForAnEmptyCandidateSet()
		{
			List<TargetRank> empty = new List<TargetRank>();
			LogAssert.AreEqual(-1, TargetOrdering.NearestIndex(empty), "No candidates means no nearest.");
			LogAssert.AreEqual(-1, TargetOrdering.FurthestIndex(empty), "No candidates means no furthest.");
			LogAssert.AreEqual(-1, TargetOrdering.NearestIndex(null), "A null list must not be dereferenced.");
			LogAssert.AreEqual(-1, TargetOrdering.FurthestIndex(null), "A null list must not be dereferenced.");
		}

		// ── Ties resolve the same way every time ─────────────────────────────────────

		/// <summary>
		/// Equidistant candidates resolve by network identity, not by which one the query listed first.
		/// </summary>
		[Test]
		public void EquidistantCandidates_ResolveByNetworkIdentity()
		{
			// Same distance, listed high-id-first. The lower ObjectId must win regardless.
			List<TargetRank> ranks = new List<TargetRank>
			{
				new TargetRank(0, 77, 0, 0, 4.0f),
				new TargetRank(1, 12, 0, 0, 4.0f),
			};

			int nearest = TargetOrdering.NearestIndex(ranks);
			int furthest = TargetOrdering.FurthestIndex(ranks);

			TestContext.WriteLine($"MEASURE tie at 4.0m between ObjectId 77 and 12: nearest→{ranks[nearest].ObjectId} furthest→{ranks[furthest].ObjectId}");

			LogAssert.AreEqual(12, ranks[nearest].ObjectId, "A tie must be broken by the lowest network id, not by buffer order.");
			LogAssert.AreEqual(12, ranks[furthest].ObjectId,
				"Furthest breaks ties the same direction as nearest, so two selectors pointed at the same pair name the same character.");
		}

		/// <summary>The identity order is a total order — no two entries ever compare equal.</summary>
		/// <remarks>
		/// Load-bearing because <see cref="List{T}.Sort"/> is an introsort and is not stable. If the
		/// comparator ever returned 0 for two distinct candidates, their relative order would be an
		/// artefact of the sort's internal partitioning and would differ with list length.
		/// </remarks>
		[Test]
		public void CompareStable_IsATotalOrder()
		{
			TargetRank a = new TargetRank(0, 5, 100, 900, 1f);
			TargetRank b = new TargetRank(1, 5, 100, 900, 1f);

			LogAssert.IsTrue(TargetOrdering.CompareStable(a, b) < 0, "Identical keys still separate by original index.");
			LogAssert.IsTrue(TargetOrdering.CompareStable(b, a) > 0, "The comparison must be antisymmetric.");
			LogAssert.AreEqual(0, TargetOrdering.CompareStable(a, a), "An entry compares equal only to itself.");

			LogAssert.IsTrue(TargetOrdering.CompareStable(new TargetRank(9, 4, 0, 0, 0f), new TargetRank(0, 5, 0, 0, 0f)) < 0,
				"ObjectId outranks the original index.");
			LogAssert.IsTrue(TargetOrdering.CompareStable(new TargetRank(9, 5, 1, 0, 0f), new TargetRank(0, 5, 2, 0, 0f)) < 0,
				"With equal ObjectIds the name key decides — the case that separates un-networked scene objects.");
		}

		/// <summary>Sorting an identical set from two different starting orders yields one order.</summary>
		[Test]
		public void SortStable_ProducesTheSameOrder_FromAnyStartingPermutation()
		{
			List<TargetRank> forward = new List<TargetRank>
			{
				new TargetRank(0, 31, 0, 0, 0f),
				new TargetRank(1, 7, 0, 0, 0f),
				new TargetRank(2, 19, 0, 0, 0f),
				new TargetRank(3, 4, 0, 0, 0f),
			};
			// The same four candidates as a physics buffer might have listed them on another peer.
			List<TargetRank> shuffled = new List<TargetRank>
			{
				new TargetRank(0, 19, 0, 0, 0f),
				new TargetRank(1, 4, 0, 0, 0f),
				new TargetRank(2, 31, 0, 0, 0f),
				new TargetRank(3, 7, 0, 0, 0f),
			};

			TargetOrdering.SortStable(forward);
			TargetOrdering.SortStable(shuffled);

			int[] forwardIds = forward.Select(r => r.ObjectId).ToArray();
			int[] shuffledIds = shuffled.Select(r => r.ObjectId).ToArray();

			TestContext.WriteLine("MEASURE sorted ids: " + string.Join(",", forwardIds));

			CollectionAssert.AreEqual(new[] { 4, 7, 19, 31 }, forwardIds, "Sorted ascending by network id.");
			CollectionAssert.AreEqual(forwardIds, shuffledIds, "Two peers holding the same candidates in different buffer orders must agree.");
		}

		// ── MaxHits caps an ordered set, not an arbitrary one ────────────────────────

		/// <summary>
		/// The cap keeps the lowest identities, and keeps the same ones from any starting order.
		/// </summary>
		/// <remarks>
		/// The default <c>MaxHits</c> of 5 on <c>AreaTargetSelector</c> is the case the audit called
		/// out: applied to an unordered set it selected five arbitrary characters out of a crowd, and
		/// a different five the next time. The cap is only meaningful after the sort, so the two are
		/// pinned together here.
		/// </remarks>
		[Test]
		public void MaxHits_TruncatesAfterTheSort_AndKeepsTheSameCandidates()
		{
			List<TargetRank> first = BuildRanks(new[] { 40, 10, 30, 20, 50, 60, 70 });
			List<TargetRank> second = BuildRanks(new[] { 70, 60, 50, 20, 30, 10, 40 });

			TargetOrdering.SortStable(first);
			TargetOrdering.ApplyMaxHits(first, 5);
			TargetOrdering.SortStable(second);
			TargetOrdering.ApplyMaxHits(second, 5);

			int[] firstIds = first.Select(r => r.ObjectId).ToArray();

			TestContext.WriteLine($"MEASURE cap 5 of 7 candidates: {string.Join(",", firstIds)}");

			LogAssert.AreEqual(5, first.Count, "A cap of 5 keeps five candidates.");
			CollectionAssert.AreEqual(new[] { 10, 20, 30, 40, 50 }, firstIds, "The cap keeps the first five of the identity order.");
			CollectionAssert.AreEqual(firstIds, second.Select(r => r.ObjectId).ToArray(),
				"The surviving set must not depend on the order the query produced.");
		}

		/// <summary>A cap wider than the candidate set leaves it alone; a zero cap is not a cap.</summary>
		[Test]
		public void MaxHits_EdgeCases()
		{
			LogAssert.AreEqual(3, TargetOrdering.CappedCount(3, 10), "A cap above the count keeps everything.");
			LogAssert.AreEqual(0, TargetOrdering.CappedCount(0, 5), "An empty set stays empty.");
			LogAssert.AreEqual(4, TargetOrdering.CappedCount(4, 0),
				"A cap of zero means 'unset', not 'select nothing' — a selector authored with no cap must still hit.");

			List<TargetRank> ranks = BuildRanks(new[] { 1, 2, 3 });
			TargetOrdering.ApplyMaxHits(ranks, 10);
			LogAssert.AreEqual(3, ranks.Count, "Applying a wide cap must not truncate.");
		}

		// ── Cone geometry ────────────────────────────────────────────────────────────

		/// <summary>
		/// A caster is never inside its own cone, at any angle.
		/// </summary>
		/// <remarks>
		/// The failure this replaces: the caster-to-caster vector is zero,
		/// <c>Vector3.normalized</c> returns zero rather than throwing, and
		/// <c>Acos(Dot(forward, zero))</c> is <c>Acos(0)</c> — 90&#176; — so every cone of 180&#176;
		/// or wider selected its own caster and any cone effect became a self-hit.
		/// </remarks>
		[Test]
		public void Cone_NeverSelectsATargetStandingOnItsOrigin()
		{
			Vector3 origin = new Vector3(3f, 0f, 7f);

			foreach (float angle in new[] { 45f, 90f, 180f, 270f, 359f, 360f })
			{
				LogAssert.IsFalse(TargetOrdering.IsWithinCone(origin, Vector3.forward, origin, angle),
					$"A {angle}-degree cone must not contain the point it opens from.");
			}
		}

		/// <summary>The cone accepts what is in front and rejects what is outside the spread.</summary>
		[Test]
		public void Cone_AcceptsWithinHalfAngle_AndRejectsBeyond()
		{
			Vector3 origin = Vector3.zero;
			Vector3 forward = Vector3.forward;

			LogAssert.IsTrue(TargetOrdering.IsWithinCone(origin, forward, new Vector3(0f, 0f, 5f), 90f),
				"Dead ahead is inside every positive cone.");
			LogAssert.IsTrue(TargetOrdering.IsWithinCone(origin, forward, new Vector3(3f, 0f, 5f), 90f),
				"31 degrees off axis is inside a 90-degree cone (45-degree half-angle).");
			LogAssert.IsFalse(TargetOrdering.IsWithinCone(origin, forward, new Vector3(5f, 0f, 1f), 90f),
				"79 degrees off axis is outside a 90-degree cone.");
			LogAssert.IsFalse(TargetOrdering.IsWithinCone(origin, forward, new Vector3(0f, 0f, -5f), 90f),
				"Directly behind is never inside a forward cone.");
			LogAssert.IsTrue(TargetOrdering.IsWithinCone(origin, forward, new Vector3(0f, 0f, -5f), 360f),
				"A 360-degree cone is a sphere and contains every direction.");
			LogAssert.IsFalse(TargetOrdering.IsWithinCone(origin, forward, new Vector3(0f, 0f, 5f), 0f),
				"A zero-degree cone selects nothing.");
			LogAssert.IsFalse(TargetOrdering.IsWithinCone(origin, Vector3.zero, new Vector3(0f, 0f, 5f), 90f),
				"A degenerate facing has no cone to be inside of.");
		}

		/// <summary>The cone test is a function of position, and gives the same answer twice.</summary>
		[Test]
		public void Cone_IsAPureFunction()
		{
			Vector3 origin = new Vector3(-2f, 1f, 4f);
			Vector3 forward = new Vector3(0.3f, 0f, 0.9f);
			Vector3 target = new Vector3(1.2f, 1f, 9.4f);

			bool first = TargetOrdering.IsWithinCone(origin, forward, target, 60f);
			bool second = TargetOrdering.IsWithinCone(origin, forward, target, 60f);

			LogAssert.AreEqual(first, second, "Both peers evaluate this; it must not depend on anything but its arguments.");
		}

		// ── Deterministic RNG derivation ─────────────────────────────────────────────

		/// <summary>The same event identity derives the same seed, every time.</summary>
		[Test]
		public void DerivedSeed_IsStable_ForEqualInputs()
		{
			int a = EventData.DeriveSeed(42, 1000u, 7);
			int b = EventData.DeriveSeed(42, 1000u, 7);

			LogAssert.AreEqual(a, b, "The seed must be a pure function of identity, tick and salt.");
		}

		/// <summary>Each input actually participates: change one, get a different stream.</summary>
		/// <remarks>
		/// The tick is the one that matters most. Without it, two casts by the same character would
		/// share a seed and a "random" target would be the same target every time.
		/// </remarks>
		[Test]
		public void DerivedSeed_Differs_ByCaster_ByTick_AndBySalt()
		{
			int baseline = EventData.DeriveSeed(42, 1000u, 7);

			LogAssert.AreNotEqual(baseline, EventData.DeriveSeed(43, 1000u, 7), "A different caster must roll differently.");
			LogAssert.AreNotEqual(baseline, EventData.DeriveSeed(42, 1001u, 7), "A different tick must roll differently.");
			LogAssert.AreNotEqual(baseline, EventData.DeriveSeed(42, 1000u, 8), "A different consumer must roll differently.");
		}

		/// <summary>
		/// Two independent runs of the same derived stream produce identical sequences.
		/// </summary>
		/// <remarks>
		/// This is the property the process-wide <c>DeterministicRNG.Shared</c> fallback could not
		/// provide: it is seeded from <c>Environment.TickCount</c>, so the client and the server were
		/// on different streams and the same cast picked a different victim on each.
		/// </remarks>
		[Test]
		public void DerivedRNG_ProducesIdenticalSequences_AcrossIndependentRuns()
		{
			int seed = EventData.DeriveSeed(1234, 55u, 0x5241_4E44);
			DeterministicRNG peerA = new DeterministicRNG(seed);
			DeterministicRNG peerB = new DeterministicRNG(seed);

			int[] rollsA = new int[16];
			int[] rollsB = new int[16];
			for (int i = 0; i < rollsA.Length; ++i)
			{
				rollsA[i] = peerA.Range(0, 5);
				rollsB[i] = peerB.Range(0, 5);
			}

			TestContext.WriteLine("MEASURE derived rolls: " + string.Join(",", rollsA));

			CollectionAssert.AreEqual(rollsA, rollsB, "Two peers deriving from the same event must draw the same sequence.");
		}

		/// <summary>
		/// An event with no generator threaded onto it derives one rather than answering null.
		/// </summary>
		/// <remarks>
		/// <c>AbilityEventData</c>, <c>RegionEventData</c> and <c>BuffEventData</c> all arrive without
		/// one; that is what sent every consumer to the shared instance.
		/// </remarks>
		[Test]
		public void EventData_DerivesAnRNG_WhenNoneWasThreaded()
		{
			EventData eventData = new EventData(null);

			LogAssert.IsFalse(eventData.HasExplicitRNG, "Nothing was threaded onto this event.");
			LogAssert.IsTrue(eventData.RNG != null, "The generator must be derived rather than left null.");
			LogAssert.IsTrue(ReferenceEquals(eventData.RNG, eventData.RNG), "The derived generator is cached, so one event is one stream.");
			LogAssert.IsFalse(ReferenceEquals(eventData.RNG, DeterministicRNG.Shared),
				"The derived generator must never be the process-wide instance — that is the non-determinism this replaces.");
		}

		/// <summary>An explicitly threaded generator is kept, not replaced by a derived one.</summary>
		[Test]
		public void EventData_KeepsAnExplicitlyThreadedRNG()
		{
			DeterministicRNG supplied = new DeterministicRNG(99);
			EventData eventData = new EventData(null) { RNG = supplied };

			LogAssert.IsTrue(eventData.HasExplicitRNG, "A threaded generator is reported as explicit.");
			LogAssert.IsTrue(ReferenceEquals(supplied, eventData.RNG),
				"The ability object owns the stream for a hit event; deriving over it would break its lockstep with the server.");
		}

		/// <summary>Two events at the same tick from the same initiator derive the same stream.</summary>
		[Test]
		public void DerivedRNG_IsAFunctionOfTheEvent_NotOfTheInstance()
		{
			// No initiator and no tick payload is the weakest case — identity 0, tick 0 — and it must
			// still be reproducible rather than random.
			DeterministicRNG first = new EventData(null).DeriveRNG(5);
			DeterministicRNG second = new EventData(null).DeriveRNG(5);

			for (int i = 0; i < 8; ++i)
			{
				LogAssert.AreEqual(first.Next(1000), second.Next(1000), "Two events with the same identity must derive the same stream.");
			}
		}

		// ── The server gate ──────────────────────────────────────────────────────────

		/// <summary>
		/// The gate's decision, for each thing an event can prove about the peer it is running on.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The <c>None</c> case is the one worth stating out loud. It allows, because a trigger fired
		/// by a scene object, an unspawned character or an edit-mode test has no peer to be wrong
		/// about, and refusing there would disable every scene-authored trigger in the project. On a
		/// real client the characters involved always carry a spawned NetworkObject, so the case the
		/// gate exists for is always decided by <c>Client</c> rather than by the fallback.
		/// </para>
		/// </remarks>
		[Test]
		public void AuthorityGate_AllowsServerAndUndecidable_RefusesClient()
		{
			LogAssert.IsTrue(EcaAuthority.Allows(EcaAuthority.PeerEvidence.Server),
				"The server is the peer authoritative outcomes are computed on.");
			LogAssert.IsFalse(EcaAuthority.Allows(EcaAuthority.PeerEvidence.Client),
				"A client must never write authoritative state; it receives the outcome.");
			LogAssert.IsTrue(EcaAuthority.Allows(EcaAuthority.PeerEvidence.None),
				"An un-networked context has no peer to refuse for; scene-authored triggers must still run.");
		}

		/// <summary>
		/// With no networked identity reachable, the gate reports no evidence rather than guessing.
		/// </summary>
		[Test]
		public void AuthorityGate_ReportsNoEvidence_ForAnUnnetworkedEvent()
		{
			LogAssert.AreEqual(EcaAuthority.PeerEvidence.None, EcaAuthority.Evidence(null, null),
				"A null initiator and a null event prove nothing.");
			LogAssert.AreEqual(EcaAuthority.PeerEvidence.None, EcaAuthority.Evidence(null, new EventData(null)),
				"An event with no initiator and no target character prove nothing.");
			LogAssert.IsTrue(EcaAuthority.IsServer(null, null), "Undecidable resolves to allowed.");
		}

		// ── Selector-level shape ─────────────────────────────────────────────────────

		/// <summary>
		/// A physics selector yields nothing when the event proves the peer is a client.
		/// </summary>
		/// <remarks>
		/// Exercised through the pure gate rather than the selector, for the reason given on the
		/// fixture: a selector needs a NetworkManager before it will answer. What is pinned here is
		/// that the selectors ask this question and not the old one — the old guard tested
		/// <c>TickEventData.IsReplicateTick</c>, which is true on the server for every ability spawn
		/// and self-target dispatch, and so refused the server as well.
		/// </remarks>
		[Test]
		public void ReplicateTick_IsNotEvidenceOfBeingAClient()
		{
			// The payload the server's own self-target dispatch attaches.
			EventData eventData = new EventData(null);
			eventData.Add(new TickEventData(null, new PredictionTick(1234u)));

			LogAssert.IsTrue(eventData.TryGet(out TickEventData tickData), "The tick payload is still carried.");
			LogAssert.IsTrue(tickData.IsReplicateTick, "A spawn dispatch carries a replicate-domain tick — on the server too.");
			LogAssert.IsTrue(EcaAuthority.IsServer(eventData),
				"Carrying a replicate tick says nothing about which peer is executing; gating on it is what made point-blank area abilities deal no damage anywhere.");
		}

		/// <summary>The tick reaches the seed, so two casts one tick apart roll differently.</summary>
		[Test]
		public void DerivedRNG_ReadsTheEventTick()
		{
			EventData early = new EventData(null);
			early.Add(new TickEventData(null, new PredictionTick(1000u)));
			EventData late = new EventData(null);
			late.Add(new TickEventData(null, new PredictionTick(1001u)));

			// Eight draws rather than one, so the assertion is about the streams and not about a
			// one-in-a-million collision between two single rolls.
			DeterministicRNG earlyRng = early.DeriveRNG(1);
			DeterministicRNG lateRng = late.DeriveRNG(1);
			int[] earlyRolls = new int[8];
			int[] lateRolls = new int[8];
			for (int i = 0; i < earlyRolls.Length; ++i)
			{
				earlyRolls[i] = earlyRng.Next(1_000_000);
				lateRolls[i] = lateRng.Next(1_000_000);
			}

			TestContext.WriteLine($"MEASURE tick 1000 → {string.Join(",", earlyRolls)}");
			TestContext.WriteLine($"MEASURE tick 1001 → {string.Join(",", lateRolls)}");

			CollectionAssert.AreNotEqual(earlyRolls, lateRolls,
				"Without the tick in the seed, every cast by the same character would pick the same 'random' target.");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static List<TargetRank> BuildRanks(int[] objectIds)
		{
			List<TargetRank> ranks = new List<TargetRank>(objectIds.Length);
			for (int i = 0; i < objectIds.Length; ++i)
			{
				ranks.Add(new TargetRank(i, objectIds[i], 0, 0, 0f));
			}
			return ranks;
		}
	}
}
