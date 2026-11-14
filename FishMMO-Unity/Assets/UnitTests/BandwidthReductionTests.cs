using System;
using System.IO;
using FishNet.Component.Transforming;
using FishNet.Serializing;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The bandwidth round of 2026-11-14, each part lossless or bounded: compact NetworkTransform positions and rotations,
	/// packet compression, quiet reconciles for an idle owner, and the idle send interval.
	/// </summary>
	public class BandwidthReductionTests
	{
		private const float Scale = 100f;

		private static float RoundTripWrapped(float value, float previousSent, float readerReference, out bool wrapped)
		{
			Writer writer = new Writer();
			wrapped = NetworkTransform.TryWriteWrappedPosition(writer, value * Scale, previousSent * Scale);
			if (!wrapped)
			{
				return float.NaN;
			}
			LogAssert.AreEqual(2, writer.Length, "A wrapped axis is two bytes.");
			Reader reader = new Reader(writer.GetArraySegment(), null);
			return NetworkTransform.ReadWrappedPosition(reader, readerReference, Scale);
		}

		private static float Quantised(float value) => (float)Math.Round(value * Scale, MidpointRounding.AwayFromZero) / Scale;

		/// <summary>A wrapped axis decodes to exactly the 24-bit value, across the 16-bit boundary and on both sides of zero.</summary>
		[TestCase(12.34f, 12.0f)]
		[TestCase(655.35f, 655.10f)]
		[TestCase(655.36f, 655.30f)]
		[TestCase(-0.01f, 0.02f)]
		[TestCase(-3276.81f, -3276.70f)]
		[TestCase(81234.56f, 81230.00f)]
		[TestCase(-81234.56f, -81230.00f)]
		public void WrappedPosition_IsExact_AgainstTheLastValue(float value, float previous)
		{
			float decoded = RoundTripWrapped(value, previous, previous, out bool wrapped);
			LogAssert.IsTrue(wrapped, "A small move from the last sent value is wrapped.");
			Assert.AreEqual(Quantised(value), decoded, 0.004f, "The reader restores the full 24-bit value.");
		}

		/// <summary>A reader that missed updates (its reference far behind) still decodes exactly within half the range.</summary>
		[Test]
		public void WrappedPosition_IsExact_ForAReaderThatMissedUpdates()
		{
			// The writer sent 150 m ago; this reader last heard 300 m back (lost or late packets): still within 327 m.
			float decoded = RoundTripWrapped(1300.25f, 1150f, 1000.10f, out bool wrapped);
			LogAssert.IsTrue(wrapped, "Wrapped.");
			Assert.AreEqual(Quantised(1300.25f), decoded, 0.004f, "Exact for any reference within half the 16-bit range.");
		}

		/// <summary>A teleport-sized jump is never wrapped: it goes out in full.</summary>
		[Test]
		public void WrappedPosition_FallsBackToFull_ForALargeJump()
		{
			RoundTripWrapped(500f, 300f, 300f, out bool wrapped);
			LogAssert.IsFalse(wrapped, "A move of 200 m (over the 163.84 m safe distance) is sent in full.");
		}

		/// <summary>An upright rotation is a 16-bit heading, finer than the 32-bit quaternion it replaces.</summary>
		[TestCase(0f)]
		[TestCase(0.004f)]
		[TestCase(90f)]
		[TestCase(179.99f)]
		[TestCase(-45.5f)]
		[TestCase(359.999f)]
		public void CompactRotation_UprightIsTwoBytes_AndPrecise(float yaw)
		{
			Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
			Writer writer = new Writer();
			NetworkTransform.WriteCompactRotation(writer, rotation);
			LogAssert.AreEqual(2, writer.Length, "An upright rotation is two bytes.");
			Quaternion decoded = NetworkTransform.ReadCompactRotation(new Reader(writer.GetArraySegment(), null));
			Assert.Less(Quaternion.Angle(rotation, decoded), 0.006f, "Within half a heading step (0.0055 degrees).");
		}

		/// <summary>A tilted rotation is not forced upright: it is sent as the packed quaternion behind an escape.</summary>
		[Test]
		public void CompactRotation_TiltedKeepsItsTilt()
		{
			Quaternion rotation = Quaternion.Euler(20f, 135f, -5f);
			Writer writer = new Writer();
			NetworkTransform.WriteCompactRotation(writer, rotation);
			LogAssert.AreEqual(6, writer.Length, "Escape plus the 32-bit quaternion.");
			Quaternion decoded = NetworkTransform.ReadCompactRotation(new Reader(writer.GetArraySegment(), null));
			Assert.Less(Quaternion.Angle(rotation, decoded), 0.5f, "As precise as the packed quaternion always was.");
		}

		private static long[] SkewedHistogram(int seed)
		{
			var random = new System.Random(seed);
			long[] histogram = new long[256];
			for (int i = 0; i < 256; ++i)
			{
				// Mostly small values, as varints and flags are; a long tail that never appears at all.
				histogram[i] = i < 16 ? 100000 >> (i / 2) : i < 64 ? random.Next(0, 50) : 0;
			}
			return histogram;
		}

		/// <summary>Trained lengths always form a complete code no longer than 15 bits, even for a steep distribution.</summary>
		[Test]
		public void HuffmanLengths_AreCompleteAndBounded()
		{
			long[] steep = new long[256];
			for (int i = 0; i < 256; ++i)
			{
				steep[i] = i < 40 ? 1L << (40 - i) : 0;
			}
			foreach (long[] histogram in new[] { steep, SkewedHistogram(1), new long[256] })
			{
				byte[] lengths = HuffmanCode.BuildLengths(histogram);
				// The constructor throws unless the lengths are 1..15 and complete.
				HuffmanCode code = new HuffmanCode(lengths);
				LogAssert.AreEqual(256, code.Lengths.Length, "Every byte has a code.");
			}
		}

		/// <summary>Any packet comes back byte for byte, whichever form the sender chose, and never grows by more than one byte.</summary>
		[Test]
		public void PacketCompression_RoundTripsEveryPacket()
		{
			HuffmanCode code = new HuffmanCode(HuffmanCode.BuildLengths(SkewedHistogram(2)));
			var random = new System.Random(3);
			byte[] output = new byte[4096];
			byte[] restored = new byte[4096 * 8];
			int huffman = 0;
			for (int trial = 0; trial < 500; ++trial)
			{
				int length = random.Next(0, 1300);
				byte[] packet = new byte[length];
				bool typical = trial % 2 == 0;
				for (int i = 0; i < length; ++i)
				{
					packet[i] = (byte)(typical ? random.Next(0, 12) : random.Next(0, 256));
				}
				int written = PacketCompression.Compress(code, packet, output);
				Assert.LessOrEqual(written, length + PacketCompression.MaximumGrowth, "Never grows by more than the header.");
				huffman += output[0] == PacketCompression.HuffmanPacket ? 1 : 0;
				int read = PacketCompression.Decompress(code, new ReadOnlySpan<byte>(output, 0, written), restored);
				LogAssert.AreEqual(length, read, "Length restored.");
				LogAssert.IsTrue(new ReadOnlySpan<byte>(restored, 0, read).SequenceEqual(packet), "Bytes restored.");
			}
			Assert.Greater(huffman, 200, "Typical packets were coded, not sent raw.");
		}

		/// <summary>A damaged or foreign packet is refused, never throws and never reads past its end.</summary>
		[Test]
		public void PacketCompression_RefusesDamagedPackets()
		{
			HuffmanCode code = new HuffmanCode(HuffmanCode.BuildLengths(SkewedHistogram(4)));
			byte[] restored = new byte[1 << 16];
			var random = new System.Random(5);
			for (int trial = 0; trial < 2000; ++trial)
			{
				byte[] junk = new byte[random.Next(0, 40)];
				random.NextBytes(junk);
				if (junk.Length > 0 && trial % 2 == 0)
				{
					junk[0] = PacketCompression.HuffmanPacket;
				}
				int read = PacketCompression.Decompress(code, junk, restored);
				LogAssert.IsTrue(read >= -1, "Refused (-1) or decoded; never thrown.");
			}
			LogAssert.AreEqual(-1, PacketCompression.Decompress(code, new byte[] { 7, 1, 2 }, restored), "Unknown header refused.");
			LogAssert.AreEqual(-1, PacketCompression.Decompress(null, new byte[] { PacketCompression.HuffmanPacket, 3, 0 }, restored), "No table, no decode.");
		}

		/// <summary>Untrained builds install nothing, so the wire is unchanged until tables are baked.</summary>
		[Test]
		public void PacketCompression_IsOffUntilBothTablesAreTrained()
		{
			bool trained = PacketCompressionTables.ToClientsLengths != null && PacketCompressionTables.ToServerLengths != null;
			LogAssert.AreEqual(trained, PacketCompression.Available, "Available exactly when both tables exist.");
		}

		/// <summary>Standing still is inert; any movement, flag, action or request is not.</summary>
		[Test]
		public void InertInput_IsStandingStillWithNothingAsked()
		{
			CharacterReplicateData idle = default;
			idle.MoveFlags = 1 << (int)KCCMoveFlags.IsActualData;
			idle.ActivationFlags = 1 << (int)AbilityActivationFlags.IsActualData;
			idle.AimDirection = new Vector3(0.6f, 0f, 0.8f);
			LogAssert.IsTrue(CharacterPredictionController.IsInertInput(idle), "Idle, whatever the aim.");

			CharacterReplicateData moving = idle;
			moving.MoveAxisForward = 1f;
			LogAssert.IsFalse(CharacterPredictionController.IsInertInput(moving), "Moving.");
			CharacterReplicateData crouching = idle;
			crouching.MoveFlags |= 1 << (int)KCCMoveFlags.Crouch;
			LogAssert.IsFalse(CharacterPredictionController.IsInertInput(crouching), "A held key.");
			CharacterReplicateData holding = idle;
			holding.ActivationFlags |= 1 << (int)AbilityActivationFlags.IsHeld;
			LogAssert.IsFalse(CharacterPredictionController.IsInertInput(holding), "Charging an ability.");
			CharacterReplicateData queued = idle;
			queued.QueuedAbilityID = 42;
			LogAssert.IsFalse(CharacterPredictionController.IsInertInput(queued), "An ability asked for.");
			CharacterReplicateData equipping = idle;
			equipping.EquipmentRequest = 1;
			LogAssert.IsFalse(CharacterPredictionController.IsInertInput(equipping), "An equip asked for.");
		}

		/// <summary>A reconcile is skipped only when every condition holds; any one missing sends it.</summary>
		[Test]
		public void QuietReconcile_SkipsOnlyASettledRepeatToAnIdleOwner()
		{
			int inert = CharacterPredictionController.QuietInertTicks;
			int confirmed = CharacterPredictionController.QuietConfirmations;
			uint keepalive = CharacterPredictionController.QuietKeepaliveTicks;
			LogAssert.IsTrue(CharacterPredictionController.ShouldSkipQuietReconcile(false, inert, confirmed, true, 2), "Settled and idle: skipped.");
			LogAssert.IsFalse(CharacterPredictionController.ShouldSkipQuietReconcile(true, inert, confirmed, true, 2), "A one-shot report always goes.");
			LogAssert.IsFalse(CharacterPredictionController.ShouldSkipQuietReconcile(false, inert - 1, confirmed, true, 2), "Not idle long enough.");
			LogAssert.IsFalse(CharacterPredictionController.ShouldSkipQuietReconcile(false, inert, confirmed - 1, true, 2), "Not yet confirmed enough times.");
			LogAssert.IsFalse(CharacterPredictionController.ShouldSkipQuietReconcile(false, inert, confirmed, false, 2), "The state changed.");
			LogAssert.IsFalse(CharacterPredictionController.ShouldSkipQuietReconcile(false, inert, confirmed, true, keepalive), "Keepalive due.");
		}

		/// <summary>The idle interval is off unless it is slower than the server's, capped, and held after any sign of a fight.</summary>
		[Test]
		public void IdleSendInterval_IsClampedAndHeldAfterAFight()
		{
			LogAssert.AreEqual(0, NetworkSendSchedule.ClampIdle(2, 2), "Not slower than the server's: off.");
			LogAssert.AreEqual(0, NetworkSendSchedule.ClampIdle(0, 2), "0 is off.");
			LogAssert.AreEqual(3, NetworkSendSchedule.ClampIdle(3, 2), "3 over 2.");
			LogAssert.AreEqual(NetworkSendSchedule.MaximumIdleInterval, NetworkSendSchedule.ClampIdle(9, 2), "Capped.");

			uint engaged = 1000;
			LogAssert.AreEqual(0, NetworkSendSchedule.ResolveConnectionInterval(3, engaged, engaged), "Engaged: the server's interval.");
			LogAssert.AreEqual(0, NetworkSendSchedule.ResolveConnectionInterval(3, engaged + NetworkSendSchedule.EngagedHoldTicks - 1, engaged), "Within the hold.");
			LogAssert.AreEqual(3, NetworkSendSchedule.ResolveConnectionInterval(3, engaged + NetworkSendSchedule.EngagedHoldTicks, engaged), "Idle after the hold.");
			LogAssert.AreEqual(0, NetworkSendSchedule.ResolveConnectionInterval(0, engaged + 10000, engaged), "Off stays off.");
			LogAssert.IsTrue(NetworkSendSchedule.IsEngaged(true, false), "In combat.");
			LogAssert.IsTrue(NetworkSendSchedule.IsEngaged(false, true), "Near combat.");
			LogAssert.IsFalse(NetworkSendSchedule.IsEngaged(false, false), "Neither.");
		}

		/// <summary>
		/// A payload-seeded resource push sends nothing for the values the payload carried (no first push, no confirmation of
		/// a push that never happened) and still sends the first real change on its tick.
		/// </summary>
		[Test]
		public void ResourcePush_AdoptedFromThePayload_SendsOnlyRealChanges()
		{
			var state = new CharacterAttributeResourceState { Health = 80f, MaxHealth = 100, Mana = 50f, MaxMana = 50, Stamina = 100f, MaxStamina = 100 };
			ObservedResourcePushScheduler.Schedule schedule = new ObservedResourcePushScheduler.Schedule(6, 30, 0u, false);
			ObservedResourcePushScheduler scheduler = default;
			scheduler.Adopt(state);
			for (uint tick = 0; tick < 60; ++tick)
			{
				LogAssert.AreEqual(ObservedResourcePushScheduler.Decision.None, scheduler.Evaluate(tick, state, schedule),
					"Unchanged since the payload: nothing to push and nothing to confirm.");
			}
			CharacterAttributeResourceState hit = state;
			hit.Health = 60f;
			LogAssert.AreEqual(ObservedResourcePushScheduler.Decision.Push, scheduler.Evaluate(60, hit, schedule),
				"The first hit goes out on its tick: adopting leaves no rate limit behind.");

			ObservedResourcePushScheduler fresh = default;
			LogAssert.AreEqual(ObservedResourcePushScheduler.Decision.Push, fresh.Evaluate(0, state, schedule),
				"Without a payload baseline the first evaluation still pushes everything, as before.");
		}

		private static string Source(string relative) =>
			ProjectFiles.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative));

		/// <summary>The three spawn duplicates are seeded from the observer payload, which is what makes them redundant.</summary>
		[Test]
		public void ObserverPayloads_SeedEveryPushBaseline()
		{
			string attributes = Source("Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterAttributeController.cs");
			LogAssert.IsTrue(attributes.Contains("SeedObservedAttributeBaseline();") && attributes.Contains("SeedObservedResourceBaseline();"),
				"The observer attribute payload seeds both the attribute and the resource push.");
			string buffs = Source("Assets/Scripts/Shared/Implementation/Entity/Prediction/Buff/BuffController.cs");
			LogAssert.IsTrue(buffs.Contains("observedBuffBuffer[i].WriteTo(writer);") && buffs.Contains("SeedObservedBuffBaseline();"),
				"The observer buff payload seeds the buff push.");
			string kcc = Source("Assets/Scripts/Shared/Implementation/Entity/Prediction/KCC/KCCPlayer.cs");
			LogAssert.IsTrue(kcc.Contains("public override void OnSpawnServer(NetworkConnection connection)") && !kcc.Contains("MovementModeRefreshTicks"),
				"A new observer is told the movement mode at spawn; there is no periodic repeat to everyone.");
		}

		/// <summary>
		/// A turn settles exactly on its heading instead of creeping toward it in the last bits forever, and the final step is
		/// invisible. The same for coming to rest.
		/// </summary>
		[Test]
		public void TurningAndStopping_SettleOnAFixedPoint()
		{
			Quaternion heading = Quaternion.Euler(0f, 73.25f, 0f);
			Quaternion rotation = Quaternion.Euler(0f, 10f, 0f);
			float t = 1f - Mathf.Exp(-10f / 30f);
			Quaternion previous = rotation;
			int settledAt = -1;
			for (int tick = 0; tick < 300; ++tick)
			{
				rotation = Quaternion.Slerp(rotation, heading, t);
				Quaternion settled = KCCController.SettleRotation(rotation, true, heading, true);
				if (settled != rotation)
				{
					Assert.Less(Quaternion.Angle(rotation, heading), 0.2f, "Only the last, invisible step is replaced.");
				}
				rotation = settled;
				if (settledAt < 0 && rotation.Equals(heading))
				{
					settledAt = tick;
				}
				previous = rotation;
			}
			LogAssert.IsTrue(settledAt >= 0 && settledAt < 60, $"A turn lands exactly on its heading within two seconds (tick {settledAt}).");
			LogAssert.IsTrue(rotation.Equals(heading) && previous.Equals(heading), "And stays there, bit for bit.");
			LogAssert.AreEqual(Quaternion.Euler(0f, 10f, 0f), KCCController.SettleRotation(Quaternion.Euler(0f, 10f, 0f), true, heading, true),
				"Mid-turn is left alone.");
			LogAssert.AreEqual(Quaternion.Euler(0f, 73.249f, 0f), KCCController.SettleRotation(Quaternion.Euler(0f, 73.249f, 0f), true, heading, false),
				"Not upright (a bonus orientation): left alone.");

			Vector3 velocity = new Vector3(3f, 0f, -2f);
			int stoppedAt = -1;
			for (int tick = 0; tick < 600; ++tick)
			{
				velocity = KCCController.SettleVelocity(Vector3.Lerp(velocity, Vector3.zero, 15f / 30f), Vector3.zero);
				if (stoppedAt < 0 && velocity == Vector3.zero && velocity.Equals(Vector3.zero))
				{
					stoppedAt = tick;
				}
			}
			LogAssert.IsTrue(stoppedAt >= 0 && stoppedAt < 30, $"Letting go of the keys comes to an exact stop within a second (tick {stoppedAt}).");
			LogAssert.AreEqual(new Vector3(0.5f, 0f, 0f), KCCController.SettleVelocity(new Vector3(0.5f, 0f, 0f), Vector3.zero), "A moving character is left alone.");
		}

		/// <summary>The baked tables are complete codes for every byte and compress traffic shaped like what they were trained on.</summary>
		[Test]
		public void TheBakedTables_AreCompleteAndCompress()
		{
			LogAssert.IsTrue(PacketCompression.Available, "Both tables are baked, so compression is installed.");
			foreach (HuffmanCode code in new[] { PacketCompression.ToClients, PacketCompression.ToServer })
			{
				var random = new System.Random(11);
				byte[] output = new byte[2048];
				byte[] restored = new byte[2048 * 8];
				for (int trial = 0; trial < 300; ++trial)
				{
					byte[] packet = new byte[random.Next(1, 1200)];
					random.NextBytes(packet);
					int written = PacketCompression.Compress(code, packet, output);
					Assert.LessOrEqual(written, packet.Length + PacketCompression.MaximumGrowth, "Random bytes never grow past the header.");
					int read = PacketCompression.Decompress(code, new ReadOnlySpan<byte>(output, 0, written), restored);
					LogAssert.IsTrue(read == packet.Length && new ReadOnlySpan<byte>(restored, 0, read).SequenceEqual(packet), "Every byte value round trips.");
				}
			}
			// Input-shaped packets (mostly small values, as the trained sample is) compress well with the client table.
			byte[] input = new byte[106];
			var rng = new System.Random(5);
			for (int i = 0; i < input.Length; ++i)
			{
				input[i] = (byte)(rng.Next(10) < 7 ? rng.Next(0, 4) : rng.Next(0, 256));
			}
			byte[] packed = new byte[512];
			int size = PacketCompression.Compress(PacketCompression.ToServer, input, packed);
			Assert.Less(size, input.Length * 0.8, "Small-valued input packets shrink.");
		}
	}
}
