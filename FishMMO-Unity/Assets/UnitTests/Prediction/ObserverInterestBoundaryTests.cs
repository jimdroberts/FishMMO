using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The boundary between NETWORK INTEREST and everything else.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The observer set answers one question — who is this object currently being streamed to — and
	/// the visibility budget bounds the answer, so it is never a proximity set and never a gameplay
	/// scope. Four separate systems had borrowed it as one, and this fixture pins each of them to a
	/// primitive that means what it says.
	/// </para>
	/// <list type="bullet">
	/// <item><description>The engaged full-rate budget's overflow interval survives the engagement
	/// exemption, so the budget actually bounds the exemption instead of being cancelled by it.</description></item>
	/// <item><description>A connection's first packet after it becomes an observer is never shaped,
	/// so the receiver's one-tick premise holds on every spawn into observation.</description></item>
	/// <item><description>AI liveness comes from a measured distance, not from observer
	/// membership.</description></item>
	/// <item><description><c>/say</c> is scoped by a radius over the scene's connections, not by the
	/// sender's observer set.</description></item>
	/// </list>
	/// </remarks>
	[TestFixture]
	public class ObserverInterestBoundaryTests
	{
		private readonly List<GameObject> created = new List<GameObject>();

		[SetUp]
		public void SetUp()
		{
			ObserverStreamingRegistry.Clear();
		}

		[TearDown]
		public void TearDown()
		{
			ObserverStreamingRegistry.Clear();
			for (int i = 0; i < created.Count; ++i)
			{
				if (created[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(created[i]);
				}
			}
			created.Clear();
		}

		// ── Defect 1: the engaged budget's overflow must survive the exemption ──

		/// <summary>
		/// The composition rule as a truth table. The row that used to be wrong is the last one: an
		/// engaged observer was exempt from EVERY cap interval, including the one the engaged budget
		/// had just assigned it, so the budget bounded nothing.
		/// </summary>
		[Test]
		public void ResolveEffectiveInterval_ExemptsTheRelevanceCapButNotTheEngagedOverflow()
		{
			LogAssert.AreEqual(1, (int)ObserverStreamingEntry.ResolveEffectiveInterval(
					1, ObserverStreamingEntry.IntervalOrigin.None, 1, engaged: false),
				"Nothing throttling: full rate.");

			LogAssert.AreEqual(3, (int)ObserverStreamingEntry.ResolveEffectiveInterval(
					3, ObserverStreamingEntry.IntervalOrigin.RelevanceCap, 2, engaged: false),
				"Not engaged: the two throttles compose by max, cap winning here.");

			LogAssert.AreEqual(2, (int)ObserverStreamingEntry.ResolveEffectiveInterval(
					1, ObserverStreamingEntry.IntervalOrigin.None, 2, engaged: false),
				"Not engaged: the two throttles compose by max, distance winning here.");

			LogAssert.AreEqual(1, (int)ObserverStreamingEntry.ResolveEffectiveInterval(
					3, ObserverStreamingEntry.IntervalOrigin.RelevanceCap, 2, engaged: true),
				"Engaged: the relevance cap is exempt — it scores by relevance and knows nothing " +
				"about whether this observer can reach the object.");

			LogAssert.AreEqual(2, (int)ObserverStreamingEntry.ResolveEffectiveInterval(
					2, ObserverStreamingEntry.IntervalOrigin.EngagedOverflow, 1, engaged: true),
				"Engaged: the engaged-overflow interval is NOT exempt. It is the bound on the " +
				"exemption, so exempting it cancels the budget that assigned it.");

			LogAssert.AreEqual(2, (int)ObserverStreamingEntry.ResolveEffectiveInterval(
					2, ObserverStreamingEntry.IntervalOrigin.EngagedOverflow, 1, engaged: false),
				"An overflow interval applies whether or not the LOD agrees the observer is engaged.");
		}

		/// <summary>
		/// The two halves composed. <c>EngagedFullRateBudget_BoundsTheExemption</c> proves the
		/// numbers are sane and <c>ResolveEffectiveInterval_...</c> proves the rule is right; neither
		/// proves the scheduler and the send agree, which is exactly where the budget used to
		/// evaporate. This drives a real ranking pass and then asks the SEND PATH what each observer
		/// gets.
		/// </summary>
		[Test]
		public void RankForViewers_ThrottlesEngagedCharactersBeyondTheBudget_AtTheSendPath()
		{
			int budget = ObserverStreamingPolicy.EngagedFullRateBudget;
			byte overflow = ObserverStreamingPolicy.EngagedOverflowInterval;
			LogAssert.IsTrue(budget > 0, "The test needs a budget to overflow.");
			LogAssert.IsTrue(overflow > 1, "The test needs an overflow interval that actually throttles.");

			float engagementRange = ObserverStreamingPolicy.ResolveEngagementRange(0f);
			float engagementSqr = engagementRange * engagementRange;

			const int overflowCount = 3;
			int engagedCount = budget + overflowCount;

			NetworkConnection viewerConnection = new NetworkConnection { ClientId = 41 };
			NetworkObject viewerObject = MakeObject("Viewer", Vector3.zero, 1000);
			SetField(viewerObject, "_owner", viewerConnection);
			ObserverStreamingEntry viewerEntry = ObserverStreamingRegistry.Register(viewerObject, new MockCharacter(1));
			MakeViewer(viewerEntry);

			List<ObserverStreamingEntry> observed = new List<ObserverStreamingEntry>();
			List<NetworkTransformDistanceLod> lods = new List<NetworkTransformDistanceLod>();
			for (int i = 0; i < engagedCount; ++i)
			{
				// Well inside the engagement radius, each a little further out than the last so the
				// relevance order is deterministic.
				float distance = 2f + i * 0.5f;
				LogAssert.IsTrue(distance < engagementRange, "Every character in this test must be engaged.");

				NetworkObject nob = MakeObject($"Engaged{i}", new Vector3(distance, 0f, 0f), 2000 + i);
				// Before Register: the entry caches its LOD in its constructor.
				NetworkTransformDistanceLod lod = nob.gameObject.AddComponent<NetworkTransformDistanceLod>();
				ObserverStreamingEntry entry = ObserverStreamingRegistry.Register(nob, new MockCharacter(10 + i));
				LogAssert.IsTrue(entry.HasDistanceLod, "The entry must see the LOD on its own object.");

				lod.BandObserver(viewerConnection.ClientId, distance * distance, engagementSqr);
				LogAssert.IsTrue(lod.IsEngaged(viewerConnection),
					"Every character in this test is inside the viewer's engagement radius — that is the case under test.");

				observed.Add(entry);
				lods.Add(lod);
			}

			ObserverStreamingRegistry.RunPass();

			int fullRate = 0;
			int throttled = 0;
			for (int i = 0; i < observed.Count; ++i)
			{
				byte effective = observed[i].GetEffectiveInterval(viewerConnection);
				if (effective <= 1)
				{
					fullRate++;
					continue;
				}
				throttled++;
				LogAssert.AreEqual((int)overflow, (int)effective,
					"An engaged character past the budget falls to the overflow interval, not to its distance band.");
				LogAssert.IsTrue(lods[i].IsEngaged(viewerConnection),
					"...and it is still ENGAGED while throttled. That pair is the whole defect: the exemption " +
					"used to return 1 here and discard the interval the budget had just assigned.");
			}

			LogAssert.AreEqual(budget, fullRate,
				$"Exactly EngagedFullRateBudget ({budget}) engaged characters keep every tick.");
			LogAssert.AreEqual(overflowCount, throttled,
				"Everything past the budget is throttled at the send, not merely recorded as throttled.");
			LogAssert.AreEqual(overflowCount, ObserverStreamingRegistry.LastPassLimitedPairs,
				"LastPassLimitedPairs must count throttles that actually happen.");
		}

		// ── Defect 2: a new observer's first send ──

		/// <summary>
		/// A fresh observer's previous goal is the reliable spawn baseline, whose tick is 0, and
		/// NetworkTransform reads a zero predecessor as exactly one tick of motion. Filtering that
		/// observer's first packet therefore makes its NEXT packet play N ticks of motion in one.
		/// FishNet's own latch covers the reliable→unreliable transition but is per behaviour and is
		/// never re-armed when an observer is ADDED, so the filters have to answer for it themselves.
		/// </summary>
		[Test]
		public void Entry_NeverShapesAnObserversFirstSend_AndArmsAgainWhenThatObserverLeaves()
		{
			NetworkObject nob = MakeObject("FirstSendProbe", Vector3.zero, 3000);
			ObserverStreamingEntry entry = new ObserverStreamingEntry(nob, new MockCharacter(2), null);

			NetworkConnection observer = new NetworkConnection { ClientId = 7 };
			nob.Observers.Add(observer);

			// A throttle heavy enough that the phase can never let every tick through.
			entry.SetInterval(observer, 4, ObserverStreamingEntry.IntervalOrigin.RelevanceCap);

			LogAssert.IsTrue(entry.ShouldSend(nob, observer, Channel.Unreliable),
				"The first unreliable send to a new observer is never shaped.");

			int sentOfNext = 0;
			for (int i = 0; i < 8; ++i)
			{
				if (entry.ShouldSend(nob, observer, Channel.Unreliable))
				{
					sentOfNext++;
				}
			}
			LogAssert.IsTrue(sentOfNext < 8,
				"The exemption is for the FIRST packet only; the throttle must apply to everything after it.");

			// The observer leaves and comes back — a range exit, a budget eviction, a scene boundary.
			nob.Observers.Remove(observer);
			entry.RefreshForPass();
			nob.Observers.Add(observer);
			entry.SetInterval(observer, 4, ObserverStreamingEntry.IntervalOrigin.RelevanceCap);

			LogAssert.IsTrue(entry.ShouldSend(nob, observer, Channel.Unreliable),
				"A re-admitted observer is a NEW observer: its first packet is exempt again, or the lurch " +
				"returns on every range entry and every budget re-admit.");
		}

		/// <summary>
		/// A reliable send must not consume the exemption: the reliable baseline is what the
		/// exemption is measured from, so spending it there leaves the first unreliable packet
		/// shaped — the exact case the exemption exists for.
		/// </summary>
		[Test]
		public void Entry_ReliableSendDoesNotSpendTheFirstSendExemption()
		{
			NetworkObject nob = MakeObject("ReliableFirstProbe", Vector3.zero, 3001);
			ObserverStreamingEntry entry = new ObserverStreamingEntry(nob, new MockCharacter(3), null);

			NetworkConnection observer = new NetworkConnection { ClientId = 9 };
			nob.Observers.Add(observer);
			entry.SetInterval(observer, 4, ObserverStreamingEntry.IntervalOrigin.RelevanceCap);

			LogAssert.IsTrue(entry.ShouldSend(nob, observer, Channel.Reliable),
				"Reliable sends are never shaped.");
			LogAssert.IsTrue(entry.ShouldSend(nob, observer, Channel.Unreliable),
				"...and the unreliable packet that follows one is still the first unreliable packet.");
		}

		/// <summary>
		/// The same invariant on the other filter. An object with no streaming entry — a world item,
		/// a static interactable — installs the distance LOD as its own send filter, so the rule has
		/// to hold there too.
		/// </summary>
		[Test]
		public void DistanceLod_NeverShapesAnObserversFirstSend()
		{
			NetworkObject nob = MakeObject("LodFirstSendProbe", Vector3.zero, 3002);
			NetworkTransformDistanceLod lod = nob.gameObject.AddComponent<NetworkTransformDistanceLod>();

			NetworkConnection observer = new NetworkConnection { ClientId = 11 };
			lod.BandObserver(observer.ClientId, 200f * 200f);
			LogAssert.IsTrue(lod.GetInterval(observer) > 1, "The 200 m observer must be throttled by distance.");

			LogAssert.IsTrue(lod.ShouldSend(nob, observer, Channel.Unreliable),
				"The first unreliable send to a new observer is never shaped.");

			int sent = 0;
			for (int i = 0; i < 8; ++i)
			{
				if (lod.ShouldSend(nob, observer, Channel.Unreliable))
				{
					sent++;
				}
			}
			LogAssert.IsTrue(sent < 8, "The throttle must apply to everything after the first packet.");
		}

		/// <summary>
		/// One rule, two filters. The decision lives in <c>ObserverStreamingPolicy</c> so the entry
		/// and the LOD cannot drift apart, which is what let the LOD keep a rule the entry had lost.
		/// </summary>
		[Test]
		public void ShouldSendToObserver_TruthTable()
		{
			// Reliable, owner and first-send all short circuit whatever the interval says.
			LogAssert.IsTrue(ObserverStreamingPolicy.ShouldSendToObserver(
				Channel.Reliable, isOwner: false, firstSendToObserver: false, interval: 8, tick: 1u, clientId: 0),
				"Reliable is never shaped.");
			LogAssert.IsTrue(ObserverStreamingPolicy.ShouldSendToObserver(
				Channel.Unreliable, isOwner: true, firstSendToObserver: false, interval: 8, tick: 1u, clientId: 0),
				"The owner is never shaped.");
			LogAssert.IsTrue(ObserverStreamingPolicy.ShouldSendToObserver(
				Channel.Unreliable, isOwner: false, firstSendToObserver: true, interval: 8, tick: 1u, clientId: 0),
				"A new observer's first packet is never shaped.");
			LogAssert.IsTrue(ObserverStreamingPolicy.ShouldSendToObserver(
				Channel.Unreliable, isOwner: false, firstSendToObserver: false, interval: 1, tick: 1u, clientId: 0),
				"An interval of 1 is no throttle at all.");

			// Otherwise it is the phase-shifted modulo, and it must actually decline sometimes.
			int declined = 0;
			for (uint tick = 0; tick < 8; ++tick)
			{
				if (!ObserverStreamingPolicy.ShouldSendToObserver(
					Channel.Unreliable, isOwner: false, firstSendToObserver: false, interval: 4, tick: tick, clientId: 0))
				{
					declined++;
				}
			}
			LogAssert.AreEqual(6, declined, "At interval 4 exactly two of eight ticks are sent.");
		}

		// ── Defect 4: AI liveness is a proximity question ──

		/// <summary>
		/// The registry measures viewer distance BEFORE it applies any range or budget filter, so an
		/// object nothing is streaming still knows how far the nearest player is. That is the number
		/// AI liveness needs; observer membership is not it.
		/// </summary>
		[Test]
		public void Registry_MeasuresNearestPlayerDistance_EvenForAnObjectNobodyObserves()
		{
			NetworkConnection viewerConnection = new NetworkConnection { ClientId = 43 };
			NetworkObject viewerObject = MakeObject("ProximityViewer", Vector3.zero, 4000);
			SetField(viewerObject, "_owner", viewerConnection);
			MakeViewer(ObserverStreamingRegistry.Register(viewerObject, new MockCharacter(1)));

			// Beyond the range that admits it as a ranking candidate, and with an empty observer set:
			// under the old rule this monster reported "nobody is anywhere near me".
			const float distance = 60f;
			NetworkObject monster = MakeObject("Monster", new Vector3(distance, 0f, 0f), 4001);
			ObserverStreamingRegistry.Register(monster, new MockCharacter(20));

			LogAssert.IsFalse(ObserverStreamingRegistry.TryGetNearestViewerDistance(monster, out _),
				"Before any pass there is no measurement, and 'no measurement' must be distinguishable from 'nobody is near'.");

			ObserverStreamingRegistry.RunPass();

			LogAssert.AreEqual(0, monster.Observers.Count,
				"Nothing has added this monster to an observer set — the case the defect lived in.");
			LogAssert.IsTrue(ObserverStreamingRegistry.TryGetNearestViewerDistance(monster, out float measured),
				"A completed pass measures every registered object.");
			LogAssert.IsTrue(Mathf.Abs(measured - distance) < 0.01f,
				$"The measurement is the real distance to the nearest player; got {measured}.");

			// And an object in a scene with no players at all is measured as 'nobody', not as 'unknown'.
			ObserverStreamingRegistry.Clear();
			NetworkObject lonely = MakeObject("Lonely", Vector3.zero, 4002);
			ObserverStreamingRegistry.Register(lonely, new MockCharacter(21));
			ObserverStreamingRegistry.RunPass();
			LogAssert.IsTrue(ObserverStreamingRegistry.TryGetNearestViewerDistance(lonely, out float none),
				"An empty scene is an answer, not a missing measurement.");
			LogAssert.IsTrue(float.IsPositiveInfinity(none), "...and the answer is 'no player at any distance'.");
		}

		/// <summary>The tier rule. An unanswered proximity question is never permission to suspend a brain.</summary>
		[Test]
		public void ResolveLodTier_TruthTable()
		{
			AILodSettings settings = ScriptableObject.CreateInstance<AILodSettings>();
			try
			{
				LogAssert.AreEqual(AILodTier.Active,
					AIController.ResolveLodTier(null, hasProximityMeasurement: true, nearestPlayerSqrDistance: float.PositiveInfinity),
					"No authored LOD settings means no throttle.");

				LogAssert.AreEqual(AILodTier.Active,
					AIController.ResolveLodTier(settings, hasProximityMeasurement: false, nearestPlayerSqrDistance: float.PositiveInfinity),
					"No measurement yet: run the full pipeline until the question is answered, never suspend on it.");

				LogAssert.AreEqual(AILodTier.Active,
					AIController.ResolveLodTier(settings, true, settings.ActiveDistanceSqr - 1f),
					"Inside the Active band.");
				LogAssert.AreEqual(AILodTier.Nearby,
					AIController.ResolveLodTier(settings, true, settings.NearbyDistanceSqr - 1f),
					"Inside the Nearby band.");
				LogAssert.AreEqual(AILodTier.Far,
					AIController.ResolveLodTier(settings, true, settings.FarDistanceSqr - 1f),
					"Inside the Far band.");
				LogAssert.AreEqual(AILodTier.Dormant,
					AIController.ResolveLodTier(settings, true, settings.FarDistanceSqr + 1f),
					"Beyond every band, and only DISTANCE may put an NPC here.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(settings);
			}
		}

		/// <summary>
		/// The leash reset restores health, drops the threat table and rewinds boss phases. It is
		/// authoritative simulation on the premise that nobody is close enough to notice, so it must
		/// verify that premise against a distance rather than inherit it from a network-interest tier.
		/// </summary>
		[Test]
		public void AllowsLeashReset_TruthTable()
		{
			const float leashSqr = 10000f; // 100 m

			LogAssert.IsFalse(AIController.AllowsLeashReset(
					AILodTier.Dormant, inAttackingState: false, hasProximityMeasurement: true,
					nearestPlayerSqrDistance: float.PositiveInfinity, leashResetSqrDistance: leashSqr),
				"Not fighting: there is no fight to end.");

			LogAssert.IsFalse(AIController.AllowsLeashReset(
					AILodTier.Nearby, true, true, float.PositiveInfinity, leashSqr),
				"Still simulated in full: the reset belongs to disengagement, not to a rate change.");

			LogAssert.IsFalse(AIController.AllowsLeashReset(
					AILodTier.Dormant, true, hasProximityMeasurement: false,
					nearestPlayerSqrDistance: float.PositiveInfinity, leashResetSqrDistance: leashSqr),
				"No measurement: never hand a monster its health back on an unanswered question.");

			LogAssert.IsFalse(AIController.AllowsLeashReset(
					AILodTier.Dormant, true, true, nearestPlayerSqrDistance: 400f, leashResetSqrDistance: leashSqr),
				"A player is standing twenty metres away. Whatever the network is streaming them, the fight is on.");

			LogAssert.IsTrue(AIController.AllowsLeashReset(
					AILodTier.Far, true, true, nearestPlayerSqrDistance: leashSqr + 1f, leashResetSqrDistance: leashSqr),
				"Genuinely alone and disengaged: reset.");
		}

		/// <summary>
		/// Source-level, because the alternative needs a NavMeshAgent, a spawned NetworkObject and a
		/// live connection. The point is narrow: neither the tier evaluation nor the enemy sweep may
		/// consult <c>Observers</c> again.
		/// </summary>
		[Test]
		public void AiLiveness_IsNotDecidedFromTheObserverSet()
		{
			string ai = ReadSource("Scripts/Shared/Implementation/Entity/NPC/AI/AIController.cs");
			LogAssert.IsFalse(ai.Contains("Observers.Count", StringComparison.Ordinal),
				"An observer count is a bandwidth figure. Deciding liveness from it let a budget eviction " +
				"full-heal a monster with a player standing next to it.");
			LogAssert.IsTrue(ai.Contains("ObserverStreamingRegistry.TryGetNearestViewerDistance", StringComparison.Ordinal),
				"The tier must come from the registry's measured proximity.");

			string sweep = ReadSource("Scripts/Shared/Implementation/Entity/NPC/AI/BaseAIState.cs");
			LogAssert.IsFalse(sweep.Contains("controller.Observers", StringComparison.Ordinal),
				"A budget-evicted monster must still be able to aggro the player who pulled it.");
			LogAssert.IsTrue(sweep.Contains("controller.HasNearbyPlayer", StringComparison.Ordinal),
				"The sweep gate is a proximity gate.");
		}

		// ── Defect 3: /say is scoped by the observer set, deliberately ──

		/// <summary>
		/// Say chat is scoped by the sender's observer set, and that is the intended design.
		/// </summary>
		/// <remarks>
		/// This audit first replaced the observer scope with an explicit radius over the sender's
		/// scene, on the grounds that the observer set is a bandwidth budget rather than a proximity
		/// set. That was reverted deliberately: the player distance condition already limits who
		/// observes whom to 100 m, so an observer of the speaker is by construction near enough to
		/// hear them, and measuring earshot again here is a second copy of a radius the interest
		/// system already owns. Two copies drift the moment either is retuned.
		/// <para>
		/// The residual the radius version was written for is the visibility BUDGET, which admits only
		/// the top forty players per viewer and is asymmetric — so beyond that many players inside the
		/// radius, some adjacent players are not observers. That is a property of the interest system,
		/// and if it needs addressing it belongs there rather than in a chat handler that quietly
		/// disagrees with it.
		/// </para>
		/// </remarks>
		[Test]
		public void SayChat_IsScopedByTheObserverSet()
		{
			string source = ReadSource("Scripts/Server/Implementation/World/SceneServer/Chat/ChatSystem.LocalChat.cs");
			string body = Between(source, "public bool OnSayChat(", "\n\t\t}");
			LogAssert.IsTrue(body.Length > 0, "OnSayChat must be locatable.");

			LogAssert.IsTrue(body.Contains("sender.Observers", StringComparison.Ordinal),
				"Say chat is scoped by the observer set, which the player distance condition already bounds.");
			LogAssert.IsFalse(body.Contains("sqrMagnitude", StringComparison.Ordinal),
				"It must not re-measure a radius the interest system already applies.");
			LogAssert.IsFalse(body.Contains("SceneConnections", StringComparison.Ordinal),
				"Nor widen to the whole scene, which is what region chat is for.");
		}

		// ── Defect 6: the prune must tear down like Unregister ──

		/// <summary>
		/// The prune in <c>RunPass</c> used to drop the entry from both collections and stop there,
		/// leaving the object's send filter pointing at a dead entry whose interval map was frozen at
		/// the last pass's values. Both removal paths now go through one teardown.
		/// </summary>
		[Test]
		public void Prune_TearsDownLikeUnregister()
		{
			NetworkObject nob = MakeObject("PruneProbe", Vector3.zero, 5000);
			ObserverStreamingEntry entry = ObserverStreamingRegistry.Register(nob, new MockCharacter(30));
			LogAssert.IsNotNull(entry, "Registration must produce an entry.");
			LogAssert.IsTrue(ReferenceEquals(nob.ObserverSendFilter, entry), "Registration installs the filter.");

			entry.SetInterval(new NetworkConnection { ClientId = 3 }, 4);
			LogAssert.AreEqual(1, entry.LimitedObserverCount, "The entry is holding a per-observer interval.");

			// Despawned, which is what the prune reacts to.
			SetAutoProperty(nob, "IsDeinitializing", true);
			LogAssert.IsFalse(nob.IsSpawned, "The object must read as despawned for the prune to fire.");

			ObserverStreamingRegistry.RunPass();

			LogAssert.AreEqual(0, ObserverStreamingRegistry.Count, "The entry is gone from the registry.");
			LogAssert.IsNull(ObserverStreamingRegistry.Get(nob), "...from both collections.");
			LogAssert.IsNull(nob.ObserverSendFilter,
				"...and the object's send filter is cleared, or a pooled object comes back filtered by a dead " +
				"entry whose intervals are frozen at the last pass.");
			LogAssert.AreEqual(0, entry.LimitedObserverCount, "...with its intervals cleared.");
		}

		/// <summary>
		/// Pinning the de-duplication itself, not just its effect: the two paths must share one
		/// teardown, or the next divergence is a copy-paste away.
		/// </summary>
		[Test]
		public void RegistryTeardown_HasExactlyOnePath()
		{
			string source = ReadSource("Scripts/Shared/Implementation/Entity/Prediction/ObserverStreaming/ObserverStreamingRegistry.cs");

			int filterClears = CountOccurrences(source, "ObserverSendFilter = null;");
			LogAssert.AreEqual(1, filterClears,
				"Exactly one place may clear the send filter: Detach. Two means the paths can diverge again.");

			string unregister = Between(source, "public static void Unregister(", "\n\t\t}");
			LogAssert.IsTrue(unregister.Contains("Detach(", StringComparison.Ordinal),
				"Unregister delegates to the shared teardown.");

			string runPass = Between(source, "public static void RunPass()", "entriesByScene.Values");
			LogAssert.IsTrue(runPass.Contains("Detach(entry, i);", StringComparison.Ordinal),
				"...and so does the prune, which is where the asymmetry lived.");
		}

		// ── Defect 5 and 7: hot-path logging and per-connection fan-outs ──

		/// <summary>
		/// <c>OnReplicate</c> runs on the live tick and on every tick of every reconcile replay, for
		/// every rider. <c>Log.Debug</c> is an async method with no call-site level gate, so an
		/// interpolated diagnostic there costs a string build, a Unity object-name marshal and a Task
		/// allocation per replayed tick.
		/// </summary>
		[Test]
		public void KccReplicate_DoesNotLogOnThePredictionHotPath()
		{
			string source = ReadSource("Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlayer.cs");
			string replicate = Between(source, "public void OnReplicate(ref CharacterReplicateData", "\n\t\t}");
			LogAssert.IsTrue(replicate.Length > 0, "OnReplicate must be locatable.");
			LogAssert.IsFalse(replicate.Contains("Log.", StringComparison.Ordinal),
				"Nothing may log from inside the replicate body — it is re-entered for every tick of every replay.");

			string setPlatform = Between(source, "public void SetPlatform(KCCPlatform platform)", "\n\t\t}");
			LogAssert.IsFalse(setPlatform.Contains("if (currentPlatform != null)", StringComparison.Ordinal),
				"The empty conditional left behind by the removed diagnostic is gone.");
		}

		/// <summary>
		/// The set and object broadcast overloads serialise a message once and reuse the
		/// <c>ArraySegment</c>; the per-connection overload writes it again for every recipient.
		/// </summary>
		[Test]
		public void ObserverFanOuts_SerialiseOnce()
		{
			string boss = ReadSource("Scripts/Shared/Implementation/Entity/NPC/AI/Boss/BossScriptState.cs");
			LogAssert.IsFalse(boss.Contains("foreach (NetworkConnection conn in controller.NetworkObject.Observers)", StringComparison.Ordinal),
				"A boss phase announcement must not be re-serialised once per raid member.");
			LogAssert.IsTrue(boss.Contains("ServerManager.Broadcast(controller.NetworkObject,", StringComparison.Ordinal),
				"The NetworkObject overload is exactly this call, written once.");

			string sceneServer = ReadSource("Scripts/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.cs");
			string warn = Between(sceneServer, "private void BroadcastToInstance(", "\n\t\t}");
			LogAssert.IsTrue(warn.Length > 0, "BroadcastToInstance must be locatable.");
			LogAssert.IsFalse(warn.Contains("ConnectionCharacters", StringComparison.Ordinal),
				"Addressing one instance must not walk every character on the whole scene server, per instance, per pulse.");
			LogAssert.IsTrue(warn.Contains("SceneConnections.TryGetValue", StringComparison.Ordinal),
				"SceneConnections already holds the exact connection set for a loaded scene.");
			LogAssert.IsTrue(warn.Contains("ServerManager.Broadcast(connections,", StringComparison.Ordinal),
				"...and the set overload writes the message once.");
		}

		// ── Helpers ──

		private static string ReadAsset(string relativePath)
		{
			string path = Path.Combine(Application.dataPath, relativePath);
			LogAssert.IsTrue(File.Exists(path), $"Expected a file at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>
		/// A source file with its comments removed, so a scan for a construct cannot be satisfied — or
		/// defeated — by prose that merely names it. Every comment in this tree explains the thing it
		/// replaced, so the removed construct is almost always still written down somewhere above the
		/// code that no longer does it.
		/// </summary>
		private static string ReadSource(string relativePath)
		{
			return StripComments(ReadAsset(relativePath));
		}

		/// <summary>Removes block comments and whole-line <c>//</c> comments.</summary>
		private static string StripComments(string source)
		{
			System.Text.StringBuilder builder = new System.Text.StringBuilder(source.Length);
			int at = 0;
			while (at < source.Length)
			{
				int block = source.IndexOf("/*", at, StringComparison.Ordinal);
				if (block < 0)
				{
					builder.Append(source, at, source.Length - at);
					break;
				}
				builder.Append(source, at, block - at);
				int close = source.IndexOf("*/", block + 2, StringComparison.Ordinal);
				at = close < 0 ? source.Length : close + 2;
			}

			string[] lines = builder.ToString().Split('\n');
			System.Text.StringBuilder kept = new System.Text.StringBuilder(source.Length);
			for (int i = 0; i < lines.Length; ++i)
			{
				if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
				{
					continue;
				}
				kept.Append(lines[i]).Append('\n');
			}
			return kept.ToString();
		}

		/// <summary>Text between the first occurrence of <paramref name="from"/> and the next <paramref name="to"/>.</summary>
		private static string Between(string source, string from, string to)
		{
			int start = source.IndexOf(from, StringComparison.Ordinal);
			if (start < 0)
			{
				return string.Empty;
			}
			int end = source.IndexOf(to, start, StringComparison.Ordinal);
			return end < 0 ? source.Substring(start) : source.Substring(start, end - start);
		}

		private static int CountOccurrences(string source, string needle)
		{
			int count = 0;
			for (int at = source.IndexOf(needle, StringComparison.Ordinal); at >= 0;
				at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
			{
				count++;
			}
			return count;
		}

		/// <summary>A NetworkObject that reads as spawned, so the registry will keep and rank it.</summary>
		private NetworkObject MakeObject(string name, Vector3 position, int objectId)
		{
			GameObject go = new GameObject(name);
			go.transform.position = position;
			created.Add(go);

			NetworkObject nob = go.AddComponent<NetworkObject>();
			SetAutoProperty(nob, "ObjectId", objectId);
			SetAutoProperty(nob, "IsDeinitializing", false);
			LogAssert.IsTrue(nob.IsSpawned, "The probe object must read as spawned.");
			return nob;
		}

		/// <summary>
		/// Marks an entry as a player, which is what makes the registry rank FROM it. Set by
		/// reflection rather than by implementing IPlayerCharacter, which is forty members of
		/// gameplay surface none of this needs.
		/// </summary>
		private static void MakeViewer(ObserverStreamingEntry entry)
		{
			LogAssert.IsNotNull(entry, "Registration must produce an entry.");
			SetAutoProperty(entry, "IsPlayer", true);
		}

		private static void SetAutoProperty(object target, string propertyName, object value)
		{
			SetField(target, $"<{propertyName}>k__BackingField", value);
		}

		private static void SetField(object target, string fieldName, object value)
		{
			for (Type type = target.GetType(); type != null; type = type.BaseType)
			{
				FieldInfo field = type.GetField(fieldName,
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (field != null)
				{
					field.SetValue(target, value);
					return;
				}
			}
			LogAssert.Fail($"{target.GetType().Name}.{fieldName} must exist.");
		}

		private sealed class MockCharacter : ICharacter
		{
			public MockCharacter(long id) => ID = id;
			public long ID { get; set; }
			public string Name => "MockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			/// <inheritdoc/>
			public Nameplate CharacterNameplate { get; set; }
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
