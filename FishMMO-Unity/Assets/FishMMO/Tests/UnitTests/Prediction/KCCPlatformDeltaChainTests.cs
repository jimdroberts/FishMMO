using System;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Serializing;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Delta reconcile coverage for the moving platform's prediction structs, mirroring
	/// <see cref="ReconcileDeltaChainTests"/> and using its FishNet wire harness.
	/// </summary>
	/// <remarks>
	/// The platform is the case where a late peer is the rule rather than the exception: it is a
	/// scene object that starts ticking when the scene loads and keeps ticking whether or not
	/// anybody is near it, so every client that connects afterwards starts observing a platform
	/// whose state is far from default. It is also the one reconcile fanned out to every observer
	/// (since issue #228 the platform forwards state), so a lost or misapplied state stands every
	/// rider's deck in the wrong place.
	/// </remarks>
	[TestFixture]
	public class KCCPlatformDeltaChainTests
	{
		/// <summary>Server tick rate, matching <c>TimeManager._tickRate</c> on the scene server.</summary>
		private const ushort ServerTickRate = 30;

		/// <summary>Distance the platform travels per tick, matching a slow moving platform.</summary>
		private const float MoveRatePerTick = 0.12f;

		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			Type[] serializerTypes =
			{
				typeof(KCCPlatformReplicateDataDeltaSerializer),
				typeof(KCCPlatformReconcileDataDeltaSerializer),
			};

			foreach (Type serializerType in serializerTypes)
			{
				MethodInfo register = serializerType.GetMethod("RegisterSerializers",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				LogAssert.IsNotNull(register, $"{serializerType.Name} must expose a RegisterSerializers hook.");
				register.Invoke(null, null);
			}

			LogAssert.IsTrue(DeltaReconcileWire.HasDeltaSerializers<KCCPlatform.ReconcileData>(),
				"KCCPlatform.ReconcileData must have a delta writer AND reader, or FishNet never takes the delta path and these tests prove nothing.");
		}

		/// <summary>One tick of platform motion: a step along one axis, cycling the goal index.</summary>
		private static KCCPlatform.ReconcileData Advance(KCCPlatform.ReconcileData current, uint tick)
		{
			Vector3 position = current.Position + new Vector3(0f, 0f, MoveRatePerTick);
			// The goal flips once per leg of the route, not every tick.
			byte goalIndex = (byte)((tick / 40) % 4);
			return new KCCPlatform.ReconcileData(position, goalIndex);
		}

		private static bool ReconcileEquals(KCCPlatform.ReconcileData expected, KCCPlatform.ReconcileData actual)
		{
			return Vector3.Distance(expected.Position, actual.Position) <= 0.05f && expected.GoalIndex == actual.GoalIndex;
		}

		private static void AssertReconcileEquals(
			KCCPlatform.ReconcileData expected,
			KCCPlatform.ReconcileData actual,
			string because)
		{
			/* Position rides FishNet's quantised float delta writer, so it is compared with the
			 * same 0.05 tolerance ReconcileDeltaChainTests uses for the character motor state. Every
			 * delta is written against the last full reconcile rather than the previous delta, so
			 * the quantisation error is one step's (~0.0005 per axis) and cannot accumulate. */
			LogAssert.IsTrue(
				Vector3.Distance(expected.Position, actual.Position) <= 0.05f,
				$"{because}: position expected {expected.Position:F4} but decoded {actual.Position:F4}");
			LogAssert.AreEqual(expected.GoalIndex, actual.GoalIndex, $"{because}: goal index");
		}

		private static KCCPlatform.ReconcileData Start() => new KCCPlatform.ReconcileData(new Vector3(12f, 3f, -40f), 0);

		[Test]
		public void Chain_SixtyTicks_ClientTracksServer()
		{
			DeltaReconcileSender<KCCPlatform.ReconcileData> server = new DeltaReconcileSender<KCCPlatform.ReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<KCCPlatform.ReconcileData> client = new DeltaReconcileReceiver<KCCPlatform.ReconcileData>();
			KCCPlatform.ReconcileData authoritative = Start();

			for (uint tick = 1; tick <= ServerTickRate * 2; tick++)
			{
				authoritative = Advance(authoritative, tick);
				LogAssert.IsTrue(client.Receive(server.Send(authoritative, tick), tick, out KCCPlatform.ReconcileData received), $"tick {tick}");
				AssertReconcileEquals(authoritative, received, $"tick {tick}");
			}

			TestContext.WriteLine(
				$"MEASURE platform chain of {server.SentCount} reconciles: {server.TotalBytes}B total, " +
				$"{server.TotalBytes / (double)server.SentCount:F1}B/reconcile (header and {server.FullCount} full reconciles included)");
		}

		/// <summary>The negative control: decoded against the wrong full reconcile, a platform delta is wrong.</summary>
		[Test]
		public void Delta_DecodedAgainstTheWrongFull_IsWrong()
		{
			DeltaReconcileSender<KCCPlatform.ReconcileData> server = new DeltaReconcileSender<KCCPlatform.ReconcileData>(ServerTickRate);
			KCCPlatform.ReconcileData authoritative = Advance(Start(), 1);
			server.Send(authoritative, 1);
			authoritative = Advance(authoritative, 2);
			ArraySegment<byte> delta = server.Send(authoritative, 2);

			Reader reader = new Reader(delta, null);
			KCCPlatform.ReconcileData decoded = DeltaReconcileWire.Read(reader, default(KCCPlatform.ReconcileData), out bool isFull, out _);
			LogAssert.IsFalse(isFull, "Tick 2 is a delta.");
			LogAssert.IsFalse(ReconcileEquals(authoritative, decoded),
				"Decoded against an empty baseline the delta must come out wrong — near the world origin, which is where a " +
				"late-joining client used to see the deck. If it does not, the payload is not delta-encoded.");
		}

		[Test]
		public void LateObserver_IsBootstrappedByTheFullReconcileSentWhenItIsAdded()
		{
			DeltaReconcileSender<KCCPlatform.ReconcileData> server = new DeltaReconcileSender<KCCPlatform.ReconcileData>(ServerTickRate);
			KCCPlatform.ReconcileData authoritative = Start();

			// The platform has been ticking since the scene loaded, observed by nobody.
			for (uint tick = 1; tick <= 45; tick++)
			{
				authoritative = Advance(authoritative, tick);
				server.Send(authoritative, tick);
			}

			// A client connects; it is added as an observer on tick 46.
			DeltaReconcileReceiver<KCCPlatform.ReconcileData> late = new DeltaReconcileReceiver<KCCPlatform.ReconcileData>();
			server.ObserverAddedTick = 46;
			authoritative = Advance(authoritative, 46);
			ArraySegment<byte> joinPayload = server.Send(authoritative, 46, out bool joinWasFull);
			LogAssert.IsTrue(joinWasFull, "An observer added since the last full reconcile must get a full one.");
			LogAssert.IsTrue(late.Receive(joinPayload, 46, out KCCPlatform.ReconcileData bootstrapped), "A full reconcile is always usable.");
			AssertReconcileEquals(authoritative, bootstrapped,
				"a late observer must decode the full reconcile exactly, from an empty baseline");

			for (uint tick = 47; tick <= 80; tick++)
			{
				authoritative = Advance(authoritative, tick);
				LogAssert.IsTrue(late.Receive(server.Send(authoritative, tick), tick, out KCCPlatform.ReconcileData received), $"tick {tick}");
				AssertReconcileEquals(authoritative, received, $"late observer at tick {tick}");
			}
		}

		[Test]
		public void DriftedBaseline_IsRepairedByTheNextFull()
		{
			DeltaReconcileSender<KCCPlatform.ReconcileData> server = new DeltaReconcileSender<KCCPlatform.ReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<KCCPlatform.ReconcileData> client = new DeltaReconcileReceiver<KCCPlatform.ReconcileData>();
			KCCPlatform.ReconcileData authoritative = Start();

			for (uint tick = 1; tick <= 20; tick++)
			{
				authoritative = Advance(authoritative, tick);
				client.Receive(server.Send(authoritative, tick), tick, out _);
			}

			// A client whose full reconcile drifted for any reason at all.
			client.CorruptHeldFull(_ => new KCCPlatform.ReconcileData(new Vector3(-999f, 77f, 5f), 3));

			for (uint tick = 21; tick <= ServerTickRate + 1; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = server.Send(authoritative, tick, out bool full);
				client.Receive(payload, tick, out KCCPlatform.ReconcileData received);
				if (full)
				{
					AssertReconcileEquals(authoritative, received, "the periodic full reconcile must repair a drifted baseline");
				}
				else
				{
					LogAssert.IsFalse(ReconcileEquals(authoritative, received),
						$"tick {tick}: against the drifted baseline the delta is expected to be wrong, or this test proves nothing");
				}
			}

			authoritative = Advance(authoritative, ServerTickRate + 2);
			LogAssert.IsTrue(client.Receive(server.Send(authoritative, ServerTickRate + 2), ServerTickRate + 2, out KCCPlatform.ReconcileData after), "after the repair");
			AssertReconcileEquals(authoritative, after, "the first delta after the repair");
		}

		/// <summary>A lost delta costs only itself; a lost full reconcile costs its deltas, discarded rather than misapplied.</summary>
		[Test]
		public void LostDatagrams_AreDiscardedNotMisapplied()
		{
			DeltaReconcileSender<KCCPlatform.ReconcileData> server = new DeltaReconcileSender<KCCPlatform.ReconcileData>(ServerTickRate);
			DeltaReconcileReceiver<KCCPlatform.ReconcileData> client = new DeltaReconcileReceiver<KCCPlatform.ReconcileData>();
			KCCPlatform.ReconcileData authoritative = Start();

			// Ticks 1..30, with delta 11 lost: 12..30 still decode against full 1.
			for (uint tick = 1; tick <= ServerTickRate; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = server.Send(authoritative, tick);
				if (tick == 11)
				{
					continue;
				}
				LogAssert.IsTrue(client.Receive(payload, tick, out KCCPlatform.ReconcileData received),
					$"tick {tick}: a lost delta must not cost the deltas after it");
				AssertReconcileEquals(authoritative, received, $"tick {tick}");
			}
			KCCPlatform.ReconcileData lastGood = client.HeldFull;

			// Tick 31, the next full reconcile, is lost: 32..60 are discarded.
			authoritative = Advance(authoritative, ServerTickRate + 1);
			server.Send(authoritative, ServerTickRate + 1, out bool lostWasFull);
			LogAssert.IsTrue(lostWasFull, "Tick 31 must be the periodic full reconcile.");
			for (uint tick = ServerTickRate + 2; tick <= ServerTickRate * 2; tick++)
			{
				authoritative = Advance(authoritative, tick);
				LogAssert.IsFalse(client.Receive(server.Send(authoritative, tick), tick, out _),
					$"tick {tick}: a delta against a lost full reconcile must be discarded, not decoded against the wrong baseline");
				LogAssert.IsTrue(client.HeldFull.Position == lastGood.Position && client.HeldFull.GoalIndex == lastGood.GoalIndex,
					$"tick {tick}: a discarded delta must leave the client's full reconcile exactly where it was");
			}

			// Tick 61 re-seats it.
			for (uint tick = ServerTickRate * 2 + 1; tick <= ServerTickRate * 2 + 10; tick++)
			{
				authoritative = Advance(authoritative, tick);
				LogAssert.IsTrue(client.Receive(server.Send(authoritative, tick), tick, out KCCPlatform.ReconcileData received),
					$"tick {tick} after the next full reconcile");
				AssertReconcileEquals(authoritative, received, $"tick {tick} after the next full reconcile");
			}
		}

		[Test]
		public void ReplicateDelta_ConsumesNothing_AndStaysAligned()
		{
			// The replicate struct carries only its tick, which FishNet writes separately.
			Writer writer = new Writer();
			writer.WriteDelta(default(KCCPlatform.ReplicateData), default(KCCPlatform.ReplicateData),
				DeltaSerializerOption.RootSerialize);

			// A trailing marker proves the reader consumes exactly what the writer produced.
			writer.WriteUInt8Unpacked(0xAB);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			reader.ReadDelta(default(KCCPlatform.ReplicateData));

			LogAssert.AreEqual(0xAB, reader.ReadUInt8Unpacked(),
				"the replicate delta reader must consume exactly what the writer emitted");
			LogAssert.AreEqual(0, reader.Remaining, "the replicate payload must be consumed exactly");
		}
	}
}
