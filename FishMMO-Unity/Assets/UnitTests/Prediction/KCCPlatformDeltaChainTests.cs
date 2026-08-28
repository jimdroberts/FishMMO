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
	/// Delta chain coverage for the moving platform's prediction structs, mirroring
	/// <see cref="ReconcileDeltaChainTests"/>.
	/// </summary>
	/// <remarks>
	/// The platform is the case where a late peer is the rule rather than the exception: it is a
	/// scene object that starts ticking when the scene loads and keeps ticking whether or not
	/// anybody is near it, so every client that connects afterwards begins observing a chain that
	/// is already far from its starting baseline. A reconcile encoding that is only decodable by a
	/// peer holding the writer's baseline therefore fails for essentially every client, which is
	/// what makes the absolute-snapshot path load-bearing here rather than a corner case.
	/// </remarks>
	[TestFixture]
	public class KCCPlatformDeltaChainTests
	{
		/// <summary>Server tick rate, matching <c>TimeManager._tickRate</c> on the scene server.</summary>
		private const int ServerTickRate = 30;

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
		}

		/// <summary>
		/// The option <c>NetworkBehaviour.GetDeltaSerializeOption</c> returns for a given tick.
		/// </summary>
		private static DeltaSerializerOption OptionForTick(uint localTick, bool observerAddedThisTick = false)
		{
			if (observerAddedThisTick)
			{
				return DeltaSerializerOption.FullSerialize;
			}
			return localTick % ServerTickRate == 0
				? DeltaSerializerOption.FullSerialize
				: DeltaSerializerOption.RootSerialize;
		}

		/// <summary>Models <c>Reconcile_Send</c>: write against the baseline, then advance it.</summary>
		private static ArraySegment<byte> ServerSend(
			ref KCCPlatform.ReconcileData serverBaseline,
			KCCPlatform.ReconcileData next,
			DeltaSerializerOption option)
		{
			Writer writer = new Writer();
			writer.WriteDelta(serverBaseline, next, option);
			serverBaseline = next;
			return writer.GetArraySegment();
		}

		/// <summary>Models <c>Reconcile_Reader</c>: decode against the baseline, then advance it.</summary>
		private static KCCPlatform.ReconcileData ClientReceive(
			ref KCCPlatform.ReconcileData clientBaseline,
			ArraySegment<byte> payload)
		{
			Reader reader = new Reader(payload, null);
			KCCPlatform.ReconcileData newData = reader.ReadDelta(clientBaseline);
			LogAssert.AreEqual(0, reader.Remaining, "A reconcile payload must be consumed exactly.");
			clientBaseline = newData;
			return newData;
		}

		/// <summary>One tick of platform motion: a step along one axis, cycling the goal index.</summary>
		private static KCCPlatform.ReconcileData Advance(KCCPlatform.ReconcileData current, uint tick)
		{
			Vector3 position = current.Position + new Vector3(0f, 0f, MoveRatePerTick);
			// The goal flips once per leg of the route, not every tick.
			byte goalIndex = (byte)((tick / 40) % 4);
			return new KCCPlatform.ReconcileData(position, goalIndex);
		}

		private static void AssertReconcileEquals(
			KCCPlatform.ReconcileData expected,
			KCCPlatform.ReconcileData actual,
			string because)
		{
			LogAssert.IsTrue(
				(expected.Position - actual.Position).sqrMagnitude < 1e-6f,
				$"{because}: position expected {expected.Position:F4} but decoded {actual.Position:F4}");
			LogAssert.AreEqual(expected.GoalIndex, actual.GoalIndex, $"{because}: goal index");
		}

		[Test]
		public void Chain_SixtyTicks_ClientTracksServerExactly()
		{
			KCCPlatform.ReconcileData serverBaseline = default;
			KCCPlatform.ReconcileData clientBaseline = default;
			KCCPlatform.ReconcileData authoritative = new KCCPlatform.ReconcileData(new Vector3(12f, 3f, -40f), 0);

			for (uint tick = 1; tick <= ServerTickRate * 2; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = ServerSend(ref serverBaseline, authoritative, OptionForTick(tick));
				KCCPlatform.ReconcileData received = ClientReceive(ref clientBaseline, payload);
				AssertReconcileEquals(authoritative, received, $"tick {tick}");
			}
		}

		[Test]
		public void LateObserver_ReceivesAbsoluteSnapshot_AndJoinsTheChain()
		{
			KCCPlatform.ReconcileData serverBaseline = default;
			KCCPlatform.ReconcileData existingClient = default;
			KCCPlatform.ReconcileData authoritative = new KCCPlatform.ReconcileData(new Vector3(12f, 3f, -40f), 0);

			// The platform has been ticking since the scene loaded; its baseline is far from default.
			for (uint tick = 1; tick <= 45; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = ServerSend(ref serverBaseline, authoritative, OptionForTick(tick));
				ClientReceive(ref existingClient, payload);
			}

			/* A client connects here holding nothing. FishNet passes FullSerialize on the tick an
			 * observer is added, but its scalar delta writers are difference-based, so "every field
			 * present" is not the same as "decodable from an empty baseline". */
			KCCPlatform.ReconcileData lateObserver = default;
			authoritative = Advance(authoritative, 46);
			ArraySegment<byte> spawnPayload = ServerSend(ref serverBaseline, authoritative,
				OptionForTick(46, observerAddedThisTick: true));

			KCCPlatform.ReconcileData bootstrapped = ClientReceive(ref lateObserver, spawnPayload);

			AssertReconcileEquals(authoritative, bootstrapped,
				"a late observer must decode the absolute snapshot exactly, from an empty baseline");

			for (uint tick = 47; tick <= 60; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ArraySegment<byte> payload = ServerSend(ref serverBaseline, authoritative, OptionForTick(tick));
				KCCPlatform.ReconcileData received = ClientReceive(ref lateObserver, payload);
				AssertReconcileEquals(authoritative, received, $"late observer at tick {tick}");
			}
		}

		[Test]
		public void StaleBaseline_IsRepairedByTheNextAbsoluteSnapshot()
		{
			KCCPlatform.ReconcileData serverBaseline = default;
			KCCPlatform.ReconcileData authoritative = new KCCPlatform.ReconcileData(new Vector3(12f, 3f, -40f), 0);

			for (uint tick = 1; tick <= 20; tick++)
			{
				authoritative = Advance(authoritative, tick);
				ServerSend(ref serverBaseline, authoritative, OptionForTick(tick));
			}

			// A client whose baseline drifted for any reason at all.
			KCCPlatform.ReconcileData drifted = new KCCPlatform.ReconcileData(new Vector3(-999f, 77f, 5f), 3);

			// The next periodic full serialize must repair it outright.
			authoritative = Advance(authoritative, ServerTickRate);
			ArraySegment<byte> payload = ServerSend(ref serverBaseline, authoritative,
				OptionForTick(ServerTickRate));
			KCCPlatform.ReconcileData repaired = ClientReceive(ref drifted, payload);

			AssertReconcileEquals(authoritative, repaired,
				"the periodic absolute snapshot must repair a drifted baseline");
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
