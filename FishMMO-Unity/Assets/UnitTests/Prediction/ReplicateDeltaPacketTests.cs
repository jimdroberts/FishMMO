using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Serializing;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Round-trip coverage for the self-contained delta replicate packet.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Drives the real <c>Writer.WriteDeltaReplicate</c> and <c>Reader.ReadDeltaReplicate</c> through
	/// reflection — both are <c>internal</c> to FishNet.Runtime and take
	/// <c>ReplicateDataContainer&lt;T&gt;</c>, so there is no public surface to call them from. The
	/// point of reflecting rather than reimplementing is that these tests fail if the production
	/// methods change, which a hand-rolled model would not.
	/// </para>
	/// <para>
	/// The property that matters most here is <b>self-containment</b>. Replicates are sent on
	/// <see cref="FishNet.Transporting.Channel.Unreliable"/> and carry redundant past inputs
	/// precisely so a dropped packet does not cost the inputs it held. Encoding a packet against the
	/// previous one — which is what upstream did — makes redundancy worthless, because one loss
	/// leaves the reader without the baseline everything after it was encoded against. The decisive
	/// test below decodes a packet having never seen the packet before it.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ReplicateDeltaPacketTests
	{
		private const int RedundancyCount = 3;

		private static Type ContainerType;
		private static MethodInfo WriteQueueOverload;
		private static MethodInfo ReadOverload;

		[OneTimeSetUp]
		public void ResolveProductionMethods()
		{
			Type[] serializerTypes =
			{
				typeof(CharacterReplicateDataDeltaSerializer),
				typeof(CharacterTransientGroundingReportDeltaSerializer),
				typeof(KinematicCharacterMotorStateDeltaSerializer),
				typeof(CharacterAttributeResourceStateSerializer),
				typeof(CharacterReconcileDataDeltaSerializer),
			};
			foreach (Type t in serializerTypes)
			{
				t.GetMethod("RegisterSerializers", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
					.Invoke(null, null);
			}

			Assembly fishNet = typeof(Writer).Assembly;
			Type openContainer = fishNet.GetType("FishNet.Object.Prediction.ReplicateDataContainer`1");
			LogAssert.IsNotNull(openContainer, "ReplicateDataContainer<T> must exist in FishNet.Runtime.");
			ContainerType = openContainer.MakeGenericType(typeof(CharacterReplicateData));

			// The BasicQueue overload — the one Replicate_SendNonAuthoritative uses to relay a
			// client's inputs to every observer, which is the per-entity-per-observer cost.
			foreach (MethodInfo m in typeof(Writer).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
			{
				if (m.Name != "WriteDeltaReplicate" || !m.IsGenericMethodDefinition)
				{
					continue;
				}
				ParameterInfo[] ps = m.GetParameters();
				if (ps.Length == 2 && ps[0].ParameterType.Name.StartsWith("BasicQueue"))
				{
					WriteQueueOverload = m.MakeGenericMethod(typeof(CharacterReplicateData));
				}
			}
			LogAssert.IsNotNull(WriteQueueOverload,
				"Writer.WriteDeltaReplicate(BasicQueue, int) must exist — the FISHMMO EDIT overload.");

			foreach (MethodInfo m in typeof(Reader).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
			{
				if (m.Name != "ReadDeltaReplicate" || !m.IsGenericMethodDefinition)
				{
					continue;
				}
				ParameterInfo[] ps = m.GetParameters();
				if (ps.Length == 1 && ps[0].ParameterType == typeof(uint))
				{
					ReadOverload = m.MakeGenericMethod(typeof(CharacterReplicateData));
				}
			}
			LogAssert.IsNotNull(ReadOverload,
				"Reader.ReadDeltaReplicate<T>(uint) must exist — the FISHMMO EDIT overload.");
		}

		/// <summary>Builds a BasicQueue&lt;ReplicateDataContainer&lt;CharacterReplicateData&gt;&gt; holding the given inputs.</summary>
		private static object BuildQueue(CharacterReplicateData[] entries)
		{
			/* Taken from the resolved method signature rather than looked up by name: BasicQueue
			 * lives in the GameKit assembly, not FishNet.Runtime, and this is already the exact
			 * closed generic the production method expects. */
			Type queueType = WriteQueueOverload.GetParameters()[0].ParameterType;
			object queue = Activator.CreateInstance(queueType);
			MethodInfo enqueue = queueType.GetMethod("Enqueue");

			ConstructorInfo ctor = ContainerType.GetConstructor(
				new[] { typeof(CharacterReplicateData), typeof(FishNet.Transporting.Channel), typeof(uint), typeof(bool) });
			LogAssert.IsNotNull(ctor, "ReplicateDataContainer needs its (data, channel, tick, isCreated) constructor.");

			for (int i = 0; i < entries.Length; i++)
			{
				object container = ctor.Invoke(new object[]
				{
					entries[i], FishNet.Transporting.Channel.Unreliable, (uint)(100 + i), true
				});
				enqueue.Invoke(queue, new[] { container });
			}
			return queue;
		}

		private static ArraySegment<byte> WritePacket(CharacterReplicateData[] entries)
		{
			object queue = BuildQueue(entries);
			Writer writer = new Writer();
			WriteQueueOverload.Invoke(writer, new object[] { queue, entries.Length });
			return writer.GetArraySegment();
		}

		/// <summary>Decodes a packet and returns the per-entry data plus the stamped ticks.</summary>
		private static (CharacterReplicateData[] data, uint[] ticks) ReadPacket(ArraySegment<byte> payload, uint tick, out int remaining)
		{
			Reader reader = new Reader(payload, null);
			object list = ReadOverload.Invoke(reader, new object[] { tick });
			remaining = reader.Remaining;

			IList entries = (IList)list;
			CharacterReplicateData[] data = new CharacterReplicateData[entries.Count];
			uint[] ticks = new uint[entries.Count];
			FieldInfo dataField = ContainerType.GetField("Data");

			for (int i = 0; i < entries.Count; i++)
			{
				object container = entries[i];
				data[i] = (CharacterReplicateData)dataField.GetValue(container);
				// The container stamps the tick onto the data itself (ReplicateDataContainer
				// forwards to Data.SetTick), so IReplicateData.GetTick is the authority.
				ticks[i] = data[i].GetTick();
			}
			return (data, ticks);
		}

		/// <summary>Three consecutive ticks of a walking player — the steady-state packet shape.</summary>
		private static CharacterReplicateData[] WalkingPacket(float startYaw)
		{
			CharacterReplicateData t0 = default;
			t0.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(0f, startYaw, 0f) * Vector3.forward);
			t0.MoveAxisForward = 1f;
			t0.MoveAxisRight = 0.35f;
			t0.MoveFlags = 1;
			t0.ActivationFlags = 0;
			t0.QueuedAbilityID = 0;

			CharacterReplicateData t1 = t0;
			t1.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(0f, startYaw + 1.2f, 0f) * Vector3.forward);

			CharacterReplicateData t2 = t1;
			t2.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(0f, startYaw + 2.4f, 0f) * Vector3.forward);
			t2.MoveAxisRight = 0.4f;

			return new[] { t0, t1, t2 };
		}

		private static void AssertReplicateEquals(CharacterReplicateData expected, CharacterReplicateData actual, string context)
		{
			LogAssert.AreEqual(expected.MoveFlags, actual.MoveFlags, $"{context}: MoveFlags");
			LogAssert.AreEqual(expected.ActivationFlags, actual.ActivationFlags, $"{context}: ActivationFlags");
			LogAssert.AreEqual(expected.QueuedAbilityID, actual.QueuedAbilityID, $"{context}: QueuedAbilityID");
			LogAssert.IsTrue(Mathf.Abs(expected.MoveAxisForward - actual.MoveAxisForward) < 0.01f,
				$"{context}: MoveAxisForward {expected.MoveAxisForward} vs {actual.MoveAxisForward}");
			LogAssert.IsTrue(Mathf.Abs(expected.MoveAxisRight - actual.MoveAxisRight) < 0.01f,
				$"{context}: MoveAxisRight {expected.MoveAxisRight} vs {actual.MoveAxisRight}");
			LogAssert.IsTrue(Vector3.Angle(expected.AimDirection, actual.AimDirection) < 0.05f,
				$"{context}: AimDirection off by {Vector3.Angle(expected.AimDirection, actual.AimDirection):F4} degrees. " +
				"Aim is quantised at the producer, so a round-trip must be exact to well under a tenth of a degree.");
		}

		[Test]
		public void Packet_RoundTrips_AllEntriesAndTicks()
		{
			CharacterReplicateData[] entries = WalkingPacket(40f);
			ArraySegment<byte> payload = WritePacket(entries);

			// Replicate_Reader passes the tick the LAST entry runs on; the reader back-dates.
			const uint lastEntryTick = 500;
			var (decoded, ticks) = ReadPacket(payload, lastEntryTick, out int remaining);

			LogAssert.AreEqual(entries.Length, decoded.Length, "Every entry in the packet must come back.");
			LogAssert.AreEqual(0, remaining, "The packet must be consumed exactly — no trailing bytes.");

			for (int i = 0; i < entries.Length; i++)
			{
				AssertReplicateEquals(entries[i], decoded[i], $"entry {i}");
				LogAssert.AreEqual(lastEntryTick - (uint)(entries.Length - 1 - i), ticks[i],
					$"entry {i} must be stamped with a back-dated tick, oldest first.");
			}

			TestContext.WriteLine($"MEASURE replicate packet ({entries.Length} entries): {payload.Count}B");
		}

		[Test]
		public void Packet_IsSelfContained_DecodesWithoutThePreviousPacket()
		{
			/* The decisive property. Two packets are produced from a continuous input stream; the
			 * first is thrown away unread, exactly as a dropped UDP datagram would be. The second
			 * must still decode perfectly. If entry 0 were encoded against the previous packet — the
			 * upstream shape — this is the test that would fail. */
			CharacterReplicateData[] first = WalkingPacket(40f);
			CharacterReplicateData[] second = WalkingPacket(43.6f);

			ArraySegment<byte> droppedPacket = WritePacket(first);
			LogAssert.IsTrue(droppedPacket.Count > 0, "The first packet must actually have been produced.");
			// Never read.

			ArraySegment<byte> payload = WritePacket(second);
			var (decoded, _) = ReadPacket(payload, 600, out int remaining);

			LogAssert.AreEqual(second.Length, decoded.Length, "The surviving packet must decode in full.");
			LogAssert.AreEqual(0, remaining, "The surviving packet must be consumed exactly.");
			for (int i = 0; i < second.Length; i++)
			{
				AssertReplicateEquals(second[i], decoded[i], $"after a dropped packet, entry {i}");
			}
		}

		[Test]
		public void Packet_IdleInput_CollapsesToNearlyNothing()
		{
			// Three identical inputs — a player standing still. Entries 1 and 2 should cost a
			// flags byte each and no more.
			CharacterReplicateData idle = default;
			idle.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(0f, 40f, 0f) * Vector3.forward);
			CharacterReplicateData[] entries = { idle, idle, idle };

			ArraySegment<byte> payload = WritePacket(entries);
			var (decoded, _) = ReadPacket(payload, 700, out int remaining);

			LogAssert.AreEqual(0, remaining, "An idle packet must be consumed exactly.");
			for (int i = 0; i < entries.Length; i++)
			{
				AssertReplicateEquals(entries[i], decoded[i], $"idle entry {i}");
			}

			// count byte + absolute entry 0 + channel, then 2 x (flags byte + channel byte).
			int absoluteOnly = 1 + 27 + 1;
			TestContext.WriteLine($"MEASURE idle replicate packet ({entries.Length} entries): {payload.Count}B " +
				$"(entry 0 alone would be {absoluteOnly}B)");
			LogAssert.IsTrue(payload.Count <= absoluteOnly + (entries.Length - 1) * 4,
				$"Identical redundancy entries must collapse to a few bytes each; packet was {payload.Count}B.");
		}

		[Test]
		public void Packet_CarriesAbilityActivation_Exactly()
		{
			/* Ability input rides the same payload as movement (ActivationFlags + QueuedAbilityID).
			 * These are discrete values where an off-by-one is a wrong ability, not a small visual
			 * error, so they are asserted exactly rather than with tolerance. */
			CharacterReplicateData[] entries = WalkingPacket(40f);
			entries[1].ActivationFlags = 3;
			entries[1].QueuedAbilityID = 8842;
			entries[2].ActivationFlags = 1;
			entries[2].QueuedAbilityID = 8842;

			ArraySegment<byte> payload = WritePacket(entries);
			var (decoded, _) = ReadPacket(payload, 800, out int remaining);

			LogAssert.AreEqual(0, remaining, "The packet must be consumed exactly.");
			for (int i = 0; i < entries.Length; i++)
			{
				LogAssert.AreEqual(entries[i].ActivationFlags, decoded[i].ActivationFlags,
					$"entry {i}: ability activation flags must survive exactly.");
				LogAssert.AreEqual(entries[i].QueuedAbilityID, decoded[i].QueuedAbilityID,
					$"entry {i}: queued ability id must survive exactly.");
			}
		}

		[Test]
		public void Packet_DriftAcrossEntries_StaysWithinCodecPrecision()
		{
			/* Entries after the first are deltas against the writer's EXACT previous value, while
			 * the reader applies them to its DECODED previous value. Lossy field codecs (camera
			 * rotation is Quaternion32) therefore drift across a packet. Bounded to n-1 steps and
			 * reset by the absolute entry 0 of the next packet, but worth measuring rather than
			 * assuming. */
			CharacterReplicateData[] entries = WalkingPacket(40f);
			ArraySegment<byte> payload = WritePacket(entries);
			var (decoded, _) = ReadPacket(payload, 900, out _);

			float worstAngle = 0f;
			float worstPosition = 0f;
			for (int i = 0; i < entries.Length; i++)
			{
				worstAngle = Mathf.Max(worstAngle, Vector3.Angle(entries[i].AimDirection, decoded[i].AimDirection));
				// Aim origin is no longer replicated; only the direction can drift. See CharacterAimOrigin.
			}

			TestContext.WriteLine(
				$"MEASURE within-packet drift over {entries.Length} entries: " +
				$"rotation {worstAngle:F4} degrees, camera position {worstPosition:F5}m");

			/* Now that aim is quantised at the producer and delta'd as a packed integer, this is
			 * not merely "small" — it must be EXACT. Any drift at all means the packed value is
			 * being reconstructed differently on the two sides. */
			LogAssert.IsTrue(worstAngle < 0.0001f,
				$"Aim drift across a packet reached {worstAngle:F6} degrees. Quantised aim is delta'd as an " +
				"integer, so a round-trip must be exact; anything else means writer and reader disagree.");
			LogAssert.IsTrue(worstPosition < 0.02f,
				$"Camera position drift across a packet reached {worstPosition:F5}m; see above.");
		}
	}
}
