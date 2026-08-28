using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Covers everything an observer needs in order to draw a cast it did not predict: the
	/// activation wire format, the learn message that keeps a late-learned ability visible, the
	/// deterministic container id both peers must agree on, the owner/observer split in the spawn
	/// payload, and the in-flight catch-up a late joiner receives.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The ability simulation is deterministic and entirely client-local, so none of this is
	/// covered by state: if a message is malformed, addressed to the wrong peer, or dropped, the
	/// result is not a visible desync that corrects itself a tick later — it is a projectile that
	/// one client never sees, or sees forever.
	/// </para>
	/// <para>
	/// EditMode never runs <c>RuntimeInitializeOnLoadMethod</c> and FishNet's IL post-processor
	/// does not run either, so the broadcasts here are written through the hand-written
	/// <c>Write*</c>/<c>Read*</c> pair directly rather than through codegen — which is exactly the
	/// code that ships, since both structs carry <c>[UseGlobalCustomSerializer]</c>.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilityObserverReproductionTests
	{
		private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

		/// <summary>
		/// Every field of the struct, written unconditionally the way FishNet's generated
		/// serializer would.
		/// </summary>
		/// <remarks>
		/// This is the baseline the mode-shaped format has to beat, and it is written here rather
		/// than reused from <c>BandwidthCompositionTests</c> because that file's version writes
		/// only seven of the eleven fields and therefore under-reports the size it is compared
		/// against.
		/// </remarks>
		private static void WriteEveryField(Writer w, AbilityActivatedBroadcast m)
		{
			w.WriteInt32(m.CasterObjectID);
			w.WriteInt64(m.AbilityID);
			w.WriteInt32(m.Seed);
			w.WriteUInt32(m.SpawnTick);
			w.WriteUInt32(m.ServerTick);
			w.WriteUInt8Unpacked(m.SpawnMode);
			w.WriteInt32(m.TargetObjectID);
			w.WriteVector3(m.AimOrigin);
			w.WriteUInt32(m.PackedAimDirection);
			w.WriteVector3(m.SpawnPosition);
			w.WriteQuaternion32(m.SpawnRotation);
		}

		private static AbilityActivatedBroadcast SampleActivation(AbilitySpawnTarget mode, int targetObjectID = 77)
		{
			return new AbilityActivatedBroadcast()
			{
				CasterObjectID = 40,
				AbilityID = 8_842_001_337L,
				Seed = -1_713_468_379,
				SpawnTick = 123_450u,
				ServerTick = 123_456u,
				SpawnMode = (byte)mode,
				TargetObjectID = targetObjectID,
				AimOrigin = new Vector3(112.5f, 32.6f, -47.2f),
				PackedAimDirection = AimDirectionCompression.Encode(new Vector3(0.3f, -0.1f, 0.95f)),
				SpawnPosition = new Vector3(113.0f, 33.1f, -46.0f),
				SpawnRotation = Quaternion.Euler(12f, 200f, 0f),
			};
		}

		/// <summary>
		/// The size an activation may not exceed on the wire, in bytes.
		/// </summary>
		/// <remarks>
		/// A cast broadcast goes to every observer of the caster, and a channelled ability sends
		/// one per held tick, so this bound is what keeps a busy fight's ability traffic bounded.
		/// The worst mode carries an id, an ability id, a seed, a server tick, a tick offset, a
		/// target and a full pose; the bound has headroom over that but is far below the
		/// all-fields form.
		/// </remarks>
		private const int MaxActivationBytes = 44;

		[Test]
		public void ActivationBroadcast_RoundTripsEverySpawnMode_AndStaysUnderTheSizeBound()
		{
			foreach (AbilitySpawnTarget mode in Enum.GetValues(typeof(AbilitySpawnTarget)))
			{
				AbilityActivatedBroadcast src = SampleActivation(mode);

				Writer writer = new Writer();
				writer.WriteAbilityActivatedBroadcast(src);
				int shaped = writer.Position;

				Writer everything = new Writer();
				WriteEveryField(everything, src);

				Reader reader = new Reader(writer.GetArraySegment(), null);
				AbilityActivatedBroadcast dst = reader.ReadAbilityActivatedBroadcast();

				TestContext.WriteLine($"MEASURE activation[{mode}]: {shaped} B shaped, {everything.Position} B all-fields");

				LogAssert.AreEqual(0, reader.Remaining,
					$"[{mode}] The reader must consume exactly what the writer produced.");
				LogAssert.IsTrue(shaped <= MaxActivationBytes,
					$"[{mode}] An activation is {shaped} B, over the {MaxActivationBytes} B bound.");
				LogAssert.IsTrue(shaped < everything.Position,
					$"[{mode}] The mode-shaped format ({shaped} B) must beat the all-fields form ({everything.Position} B).");

				// Fields every mode carries.
				LogAssert.AreEqual(src.CasterObjectID, dst.CasterObjectID, $"[{mode}] CasterObjectID must round-trip.");
				LogAssert.AreEqual(src.AbilityID, dst.AbilityID, $"[{mode}] AbilityID must round-trip.");
				LogAssert.AreEqual(src.Seed, dst.Seed, $"[{mode}] Seed must round-trip — the whole reproduction hangs off it.");
				LogAssert.AreEqual(src.SpawnTick, dst.SpawnTick, $"[{mode}] SpawnTick must round-trip through the 16-bit offset.");
				LogAssert.AreEqual(src.ServerTick, dst.ServerTick, $"[{mode}] ServerTick must round-trip.");
				LogAssert.AreEqual(src.SpawnMode, dst.SpawnMode, $"[{mode}] SpawnMode selects the shape and must round-trip.");
				LogAssert.AreEqual(src.TargetObjectID, dst.TargetObjectID, $"[{mode}] TargetObjectID must round-trip.");

				if (mode == AbilitySpawnTarget.Camera)
				{
					// Camera re-derives its pose, so the aim is what has to survive.
					LogAssert.AreEqual(src.AimOrigin, dst.AimOrigin, "[Camera] AimOrigin must round-trip.");
					LogAssert.AreEqual(src.PackedAimDirection, dst.PackedAimDirection, "[Camera] PackedAimDirection must round-trip.");
					LogAssert.AreEqual(Vector3.zero, dst.SpawnPosition,
						"[Camera] The pose is not sent; it must come back at its default so the receiver cannot use it by accident.");
				}
				else
				{
					LogAssert.AreEqual(src.SpawnPosition, dst.SpawnPosition, $"[{mode}] SpawnPosition must round-trip.");
					LogAssert.IsTrue(Quaternion.Angle(src.SpawnRotation, dst.SpawnRotation) < 0.5f,
						$"[{mode}] SpawnRotation must survive 32-bit quaternion packing to within a fraction of a degree.");
					LogAssert.AreEqual(Vector3.zero, dst.AimOrigin,
						$"[{mode}] The aim is not sent for this mode and must come back at its default.");
				}
			}
		}

		[Test]
		public void ActivationBroadcast_AbsentTarget_AndFarApartTicks_StillRoundTrip()
		{
			// No target: the flag is clear and the id must come back as -1, not 0 (a valid id).
			AbilityActivatedBroadcast noTarget = SampleActivation(AbilitySpawnTarget.Forward, targetObjectID: -1);
			Writer w1 = new Writer();
			w1.WriteAbilityActivatedBroadcast(noTarget);
			Reader r1 = new Reader(w1.GetArraySegment(), null);
			AbilityActivatedBroadcast d1 = r1.ReadAbilityActivatedBroadcast();
			LogAssert.AreEqual(0, r1.Remaining, "The absent-target shape must be consumed exactly.");
			LogAssert.AreEqual(-1, d1.TargetObjectID, "An absent target must come back as -1.");

			/* The two tick domains far enough apart that the 16-bit offset cannot hold them — a
			 * client that has just resynchronised, or an unset tick. The full-width fallback is
			 * the only thing between that and a silently wrong spawn tick. */
			AbilityActivatedBroadcast farApart = SampleActivation(AbilitySpawnTarget.PointBlank);
			farApart.SpawnTick = 5u;
			farApart.ServerTick = 4_000_000u;
			Writer w2 = new Writer();
			w2.WriteAbilityActivatedBroadcast(farApart);
			Reader r2 = new Reader(w2.GetArraySegment(), null);
			AbilityActivatedBroadcast d2 = r2.ReadAbilityActivatedBroadcast();
			LogAssert.AreEqual(0, r2.Remaining, "The full-width tick shape must be consumed exactly.");
			LogAssert.AreEqual(farApart.SpawnTick, d2.SpawnTick, "A spawn tick outside offset range must fall back to full width.");
			LogAssert.AreEqual(farApart.ServerTick, d2.ServerTick, "ServerTick must round-trip on the fallback path too.");
		}

		[Test]
		public void LearnBroadcast_RoundTrips_IncludingItsEvents()
		{
			AbilityLearnedObserverBroadcast src = new AbilityLearnedObserverBroadcast()
			{
				CasterObjectID = 40,
				AbilityID = 8_842_001_337L,
				TemplateID = -55_112,
				Events = new[] { 11, 22, 33 },
			};

			Writer writer = new Writer();
			writer.WriteAbilityLearnedObserverBroadcast(src);
			Reader reader = new Reader(writer.GetArraySegment(), null);
			AbilityLearnedObserverBroadcast dst = reader.ReadAbilityLearnedObserverBroadcast();

			TestContext.WriteLine($"MEASURE learn(3 events): {writer.Position} B");

			LogAssert.AreEqual(0, reader.Remaining, "The learn message must be consumed exactly.");
			LogAssert.AreEqual(src.CasterObjectID, dst.CasterObjectID, "CasterObjectID must round-trip.");
			LogAssert.AreEqual(src.AbilityID, dst.AbilityID, "AbilityID is what activation broadcasts key on and must round-trip.");
			LogAssert.AreEqual(src.TemplateID, dst.TemplateID, "TemplateID must round-trip.");
			LogAssert.AreEqual(3, dst.Events.Length, "Every event id must survive — they carry the ability's behaviour.");
			for (int i = 0; i < src.Events.Length; ++i)
			{
				LogAssert.AreEqual(src.Events[i], dst.Events[i], $"Event id {i} must round-trip in order.");
			}

			// A null event list is a legitimate ability with no crafted events.
			AbilityLearnedObserverBroadcast noEvents = src;
			noEvents.Events = null;
			Writer w2 = new Writer();
			w2.WriteAbilityLearnedObserverBroadcast(noEvents);
			Reader r2 = new Reader(w2.GetArraySegment(), null);
			AbilityLearnedObserverBroadcast d2 = r2.ReadAbilityLearnedObserverBroadcast();
			LogAssert.AreEqual(0, r2.Remaining, "The no-events shape must be consumed exactly.");
			LogAssert.AreEqual(0, d2.Events.Length, "A null event list must read back as an empty one, never null.");
		}

		[Test]
		public void ObserverBroadcasts_NeverIncludeTheOwner_AndLeaveTheObserverSetIntact()
		{
			NetworkConnection owner = new NetworkConnection { ClientId = 1 };
			NetworkConnection a = new NetworkConnection { ClientId = 2 };
			NetworkConnection b = new NetworkConnection { ClientId = 3 };

			HashSet<NetworkConnection> observers = new HashSet<NetworkConnection> { owner, a, b, null };
			HashSet<NetworkConnection> recipients = new HashSet<NetworkConnection>();

			int count = ObserverBroadcastScope.CollectRecipients(observers, owner, recipients);

			LogAssert.AreEqual(2, count, "Both non-owner observers must be recipients.");
			LogAssert.IsFalse(recipients.Contains(owner),
				"The owner predicted the cast and holds the authoritative copy; it must not be sent the observer message.");
			LogAssert.IsTrue(recipients.Contains(a) && recipients.Contains(b), "Every other observer must be a recipient.");

			/* The reason the copy exists at all. FishNet's ServerManager.BroadcastExcept calls
			 * Remove on the set it is handed, so passing NetworkObject.Observers straight to it
			 * would evict the owner from its own object's observer set permanently. */
			LogAssert.IsTrue(observers.Contains(owner),
				"Collecting recipients must not mutate the source observer set.");
			LogAssert.AreEqual(4, observers.Count, "The source observer set must be untouched, nulls included.");
		}

		/// <summary>
		/// The reconcile truth table for objects spawned exactly ON the reconcile tick.
		/// </summary>
		/// <remarks>
		/// Every row of <c>AbilityController.ShouldDestroySpawnsAtReconcileTick</c>. The two that
		/// matter most pull in opposite directions: a denied instant cast leaves a ghost unless
		/// tick T is cleaned, and a confirmed spawn at T is lost forever if it is cleaned, because
		/// FishNet replays from T+1 and never re-creates it.
		/// </remarks>
		[Test]
		public void DeniedAtTick_TruthTable_DestroysOnlyWhatTheServerDidNotSpawn()
		{
			const int before = 1000;   // the client's seed going into tick T
			const int clientAfter = 2000;  // the client's seed after T (it spawned)
			const int serverOther = 3000;  // a server seed that matches neither

			// Row 1: both spawned at T and agree. The object at T is confirmed.
			LogAssert.IsFalse(
				AbilityController.ShouldDestroySpawnsAtReconcileTick(
					denied: false, havePredicted: true, predictedSeed: clientAfter,
					havePrevious: true, previousSeed: before, serverSeed: clientAfter),
				"Agreeing seeds mean the server spawned what the client spawned; the object at T must survive.");

			// Row 2: the server denied the activation at T. Nothing started, so nothing spawned.
			LogAssert.IsTrue(
				AbilityController.ShouldDestroySpawnsAtReconcileTick(
					denied: true, havePredicted: true, predictedSeed: clientAfter,
					havePrevious: true, previousSeed: before, serverSeed: before),
				"A denied activation at T means the predicted object spawned at T is a ghost and must be destroyed.");

			// Row 2b: denied, but the client predicted the denial correctly. Nothing to remove.
			LogAssert.IsFalse(
				AbilityController.ShouldDestroySpawnsAtReconcileTick(
					denied: true, havePredicted: true, predictedSeed: before,
					havePrevious: true, previousSeed: before, serverSeed: before),
				"A denial the client already predicted needs no correction.");

			// Row 3: the server never advanced at T (the input was lost) while the client did.
			LogAssert.IsTrue(
				AbilityController.ShouldDestroySpawnsAtReconcileTick(
					denied: false, havePredicted: true, predictedSeed: clientAfter,
					havePrevious: true, previousSeed: before, serverSeed: before),
				"A server seed still equal to the client's pre-tick seed means the server spawned nothing at T.");

			// Row 4: the server spawned at T and the client did not. Nothing local to destroy, and
			// removing tick T would be wrong if anything else lived there.
			LogAssert.IsFalse(
				AbilityController.ShouldDestroySpawnsAtReconcileTick(
					denied: false, havePredicted: true, predictedSeed: before,
					havePrevious: true, previousSeed: before, serverSeed: serverOther),
				"The client advanced nothing at T; tick T must be left alone.");

			// Row 5: divergence began earlier — the server's seed matches neither side of T.
			LogAssert.IsFalse(
				AbilityController.ShouldDestroySpawnsAtReconcileTick(
					denied: false, havePredicted: true, predictedSeed: clientAfter,
					havePrevious: true, previousSeed: before, serverSeed: serverOther),
				"With the divergence older than T, who spawned at T is unknown; a confirmed object must not be risked.");

			// Row 5b: same, with no previous entry to judge against.
			LogAssert.IsFalse(
				AbilityController.ShouldDestroySpawnsAtReconcileTick(
					denied: false, havePredicted: true, predictedSeed: clientAfter,
					havePrevious: false, previousSeed: 0, serverSeed: serverOther),
				"Without the pre-tick seed there is no evidence the server skipped T.");

			// Row 6: no history for T at all (first reconciles after spawn, or older than the ring).
			LogAssert.IsFalse(
				AbilityController.ShouldDestroySpawnsAtReconcileTick(
					denied: true, havePredicted: false, predictedSeed: 0,
					havePrevious: false, previousSeed: 0, serverSeed: serverOther),
				"With nothing recorded for T there is nothing to judge, denial or not.");
		}

		// ── Spawn payload ────────────────────────────────────────────────────

		private sealed class ProbeAbilityTemplate : AbilityTemplate { }

		private static void SetPrivate(object target, string field, object value)
		{
			Type t = target.GetType();
			FieldInfo f = null;
			while (t != null && f == null)
			{
				f = t.GetField(field, Instance);
				t = t.BaseType;
			}
			LogAssert.IsNotNull(f, $"{target.GetType().Name}.{field} must exist.");
			f.SetValue(target, value);
		}

		private static object GetPrivate(object target, string field)
		{
			Type t = target.GetType();
			FieldInfo f = null;
			while (t != null && f == null)
			{
				f = t.GetField(field, Instance);
				t = t.BaseType;
			}
			LogAssert.IsNotNull(f, $"{target.GetType().Name}.{field} must exist.");
			return f.GetValue(target);
		}

		/// <summary>
		/// Builds an AbilityController that is usable outside a live network session.
		/// </summary>
		/// <remarks>
		/// <c>AddComponent</c> never runs <c>Awake</c> in EditMode, so the collections are built
		/// by hand, and the seed generator is pre-seeded so <c>WritePayload</c> never consults
		/// <c>IsServerStarted</c> — every NetworkBehaviour convenience property throws on an
		/// unspawned object.
		/// </remarks>
		private static AbilityController BuildController(GameObject go, ICharacter character)
		{
			AbilityController controller = go.AddComponent<AbilityController>();
			controller.OnAwake();
			SetPrivate(controller, "abilitySeedGenerator", new DeterministicRNG(1));
			SetPrivate(controller, "abilitySeed", 424242);
			SetPrivate(controller, "currentSeed", 777);
			controller.InitializeOnce(character);
			return controller;
		}

		[Test]
		public void SpawnPayload_OwnerAndObserverShapes_AreBothReadExactly_AndOnlyTheOwnerGetsTheRng()
		{
			GameObject writerGo = new GameObject("PayloadWriter");
			GameObject readerGo = new GameObject("PayloadReader");
			GameObject nobGo = new GameObject("PayloadOwnerNob");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				ProbeAbilityTemplate template = ScriptableObject.CreateInstance<ProbeAbilityTemplate>();
				template.name = "Probe_Payload_Ability";
				template.AddToCache(template.name);
				assets.Add(template);

				AbilityController writerController = BuildController(writerGo, new MockCharacter(1));
				writerController.LearnAbility(new Ability(9001L, template));

				// ── Observer shape: no connection owns this object.
				Writer observerWriter = new Writer();
				writerController.WritePayload(null, observerWriter);
				int observerBytes = observerWriter.Position;

				// ── Owner shape: give the behaviour an object whose owner is the receiver.
				NetworkObject nob = nobGo.AddComponent<NetworkObject>();
				NetworkConnection owner = new NetworkConnection { ClientId = 4 };
				SetPrivate(nob, "_owner", owner);
				SetPrivate(writerController, "_networkObjectCache", nob);
				LogAssert.IsTrue(PayloadVisibility.IsOwner(writerController, owner),
					"The harness must actually present the connection as the owner, or the shape below is not the owner shape.");

				Writer ownerWriter = new Writer();
				writerController.WritePayload(owner, ownerWriter);
				int ownerBytes = ownerWriter.Position;

				TestContext.WriteLine($"MEASURE spawn payload: observer {observerBytes} B, owner {ownerBytes} B");

				/* The difference must be exactly the generator: abilitySeed, currentSeed and the
				 * four xoshiro words, all in FishNet's variable-width packing (so the expected
				 * size is measured rather than assumed). An observer never runs the seed forward —
				 * it is handed the per-cast Seed in each activation broadcast — and 128 bits of
				 * xoshiro state is the entire generator, so anyone holding it can compute every
				 * seed that character will ever cast with. */
				DeterministicRNG probeRng = new DeterministicRNG(1);
				probeRng.CaptureState(out uint s0, out uint s1, out uint s2, out uint s3);
				Writer generatorProbe = new Writer();
				generatorProbe.WriteInt32(424242);
				generatorProbe.WriteInt32(777);
				generatorProbe.WriteUInt32(s0);
				generatorProbe.WriteUInt32(s1);
				generatorProbe.WriteUInt32(s2);
				generatorProbe.WriteUInt32(s3);

				LogAssert.AreEqual(observerBytes + generatorProbe.Position, ownerBytes,
					"The owner shape must carry exactly the generator the observer shape omits, and nothing else.");

				// Both shapes must leave the shared payload stream exactly where the next
				// behaviour expects it — see the framing note in WritePayload.
				AbilityController observerReader = BuildController(readerGo, new MockCharacter(2));

				/* Stamped with a sentinel the payload could not possibly produce, so the assertion
				 * below reads "the observer shape delivered no seed" rather than "the reader
				 * happened to zero a field". BuildController seeds every controller it makes, and a
				 * real observer's controller likewise arrives with whatever local generator state it
				 * had — the property under test is that nothing on the wire can change it. */
				const int untouchedSentinel = -99;
				SetPrivate(observerReader, "abilitySeed", untouchedSentinel);
				SetPrivate(observerReader, "currentSeed", untouchedSentinel);

				Reader r1 = new Reader(observerWriter.GetArraySegment(), null);
				observerReader.ReadPayload(null, r1);
				LogAssert.AreEqual(0, r1.Remaining,
					"The observer payload must be consumed exactly; anything left over is read as the next behaviour's state.");
				LogAssert.AreEqual(1, observerReader.KnownAbilities.Count, "The observer must still learn the caster's abilities.");
				LogAssert.AreEqual(untouchedSentinel, (int)GetPrivate(observerReader, "abilitySeed"),
					"An observer must never receive the caster's ability seed.");
				LogAssert.AreEqual(untouchedSentinel, (int)GetPrivate(observerReader, "currentSeed"),
					"An observer must never receive the caster's current seed either.");

				GameObject ownerReaderGo = new GameObject("PayloadOwnerReader");
				try
				{
					AbilityController ownerReaderController = BuildController(ownerReaderGo, new MockCharacter(3));
					SetPrivate(ownerReaderController, "abilitySeed", 0);
					Reader r2 = new Reader(ownerWriter.GetArraySegment(), null);
					ownerReaderController.ReadPayload(null, r2);
					LogAssert.AreEqual(0, r2.Remaining, "The owner payload must be consumed exactly.");
					LogAssert.AreEqual(424242, (int)GetPrivate(ownerReaderController, "abilitySeed"),
						"The owner must receive the ability seed.");
					LogAssert.AreEqual(777, (int)GetPrivate(ownerReaderController, "currentSeed"),
						"The owner must receive the generator's current seed, not a fresh one derived from abilitySeed.");
				}
				finally
				{
					UnityEngine.Object.DestroyImmediate(ownerReaderGo);
				}
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(nobGo);
				UnityEngine.Object.DestroyImmediate(readerGo);
				UnityEngine.Object.DestroyImmediate(writerGo);
				foreach (UnityEngine.Object asset in assets)
				{
					UnityEngine.Object.DestroyImmediate(asset);
				}
			}
		}

		[Test]
		public void SpawnPayload_CarriesInFlightObjects_ToALateJoiner()
		{
			GameObject writerGo = new GameObject("InFlightWriter");
			GameObject readerGo = new GameObject("InFlightReader");
			GameObject projectileGo = new GameObject("InFlightProjectile");
			GameObject expiringGo = new GameObject("ExpiringProjectile");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				ProbeAbilityTemplate template = ScriptableObject.CreateInstance<ProbeAbilityTemplate>();
				template.name = "Probe_InFlight_Ability";
				template.LifeTime = 5f;
				template.AddToCache(template.name);
				assets.Add(template);

				AbilityController writerController = BuildController(writerGo, new MockCharacter(1));
				Ability ability = new Ability(9002L, template);
				writerController.LearnAbility(ability);

				/* A projectile mid-flight. Fields are set directly rather than through Spawn:
				 * initialising a real AbilityObject needs a spawned caster with a TimeManager, and
				 * what is under test is the payload, not the spawn path. */
				AbilityObject live = projectileGo.AddComponent<AbilityObject>();
				live.Ability = ability;
				live.SpawnSeed = -424_242;
				live.SpawnTick = new PredictionTick(880u);
				live.ElapsedTicks = 45u;
				live.RemainingLifeTime = 3.5f;
				live.SpawnPosition = new Vector3(10f, 2f, -3f);
				live.SpawnRotation = Quaternion.Euler(0f, 90f, 0f);

				// About to expire: not worth an Instantiate and a spawn-event chain on arrival.
				AbilityObject expiring = expiringGo.AddComponent<AbilityObject>();
				expiring.Ability = ability;
				expiring.SpawnSeed = 7;
				expiring.SpawnTick = new PredictionTick(900u);
				expiring.RemainingLifeTime = 0.05f;

				ability.Objects = new Dictionary<int, Dictionary<int, AbilityObject>>()
				{
					{ 1, new Dictionary<int, AbilityObject>() { { 0, live } } },
					{ 2, new Dictionary<int, AbilityObject>() { { 0, expiring } } },
				};

				Writer writer = new Writer();
				writerController.WritePayload(null, writer);

				AbilityController joiner = BuildController(readerGo, new MockCharacter(2));
				Reader reader = new Reader(writer.GetArraySegment(), null);
				joiner.ReadPayload(null, reader);

				LogAssert.AreEqual(0, reader.Remaining,
					"A payload carrying in-flight objects must still be consumed exactly.");

				System.Collections.IList pending = (System.Collections.IList)GetPrivate(joiner, "pendingInFlightObjects");
				LogAssert.AreEqual(1, pending.Count,
					"The late joiner must receive the live projectile and not the one about to expire.");

				object entry = pending[0];
				Type entryType = entry.GetType();
				FieldInfo Field(string name) => entryType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

				LogAssert.AreEqual(9002L, (long)Field("AbilityID").GetValue(entry), "The entry must name the ability it belongs to.");
				LogAssert.AreEqual(-424_242, (int)Field("Seed").GetValue(entry), "The seed drives the reproduction and must survive.");
				LogAssert.AreEqual(880u, (uint)Field("SpawnTick").GetValue(entry), "The spawn tick must survive.");
				LogAssert.AreEqual(new Vector3(10f, 2f, -3f), (Vector3)Field("Position").GetValue(entry),
					"The pose travels for every mode — a live object no longer holds the aim a Camera spawn could be re-derived from.");
				LogAssert.IsTrue(Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), (Quaternion)Field("Rotation").GetValue(entry)) < 0.5f,
					"The spawn rotation must survive 32-bit quaternion packing.");

				/* The server tick the object STARTED on, so the joiner can fast-forward by the
				 * difference rather than starting it at its launch point. This harness has no
				 * TimeManager, so the writer's tick base is 0 and the wrap-safe subtraction is
				 * what is being asserted: 0 - 45 elapsed ticks. */
				LogAssert.AreEqual(unchecked(0u - 45u), (uint)Field("ServerStartTick").GetValue(entry),
					"The start tick must be the current server tick minus the object's elapsed ticks, computed unchecked.");

				// And the fast-forward the joiner will apply from it, less the interpolation it
				// renders its peers behind.
				uint fastForward = AbilityController.ComputeObserverFastForwardTicks(
					estimatedServerTick: 100u, serverSpawnTick: 55u, interpolationTicks: 2u);
				LogAssert.AreEqual(43u, fastForward,
					"A joiner must catch the object up to the server, less the ticks it renders its peers behind.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(expiringGo);
				UnityEngine.Object.DestroyImmediate(projectileGo);
				UnityEngine.Object.DestroyImmediate(readerGo);
				UnityEngine.Object.DestroyImmediate(writerGo);
				foreach (UnityEngine.Object asset in assets)
				{
					UnityEngine.Object.DestroyImmediate(asset);
				}
			}
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
