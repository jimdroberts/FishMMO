using FishNet.Serializing;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Guards the rotation and direction encodings that peers must agree on exactly.
	/// </summary>
	/// <remarks>
	/// Two distinct hazards live here. One is resolution — how far a packed rotation can be from
	/// the value that was written. The other, and the one that actually broke prediction, is
	/// whether re-encoding an already-decoded value returns the same bits: the reconcile delta for
	/// ground normals derives its baseline that way on both sides, the writer from the server's raw
	/// motor value and the reader from its own decoded copy, so anything less than an exact fixed
	/// point is added straight onto the reader's result.
	/// </remarks>
	[TestFixture]
	public class RotationPrecisionTests
	{
		/// <summary>
		/// Encoding a decoded direction must return the identical bits.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the invariant the ground-normal delta chain depends on. It used to fail for the
		/// 32 pitch indices nearest the two poles, because <c>Asin</c> is numerically flat there,
		/// and — worst of all — for the perfectly flat ground normal (0,1,0), the most common value
		/// in the game: <c>Cos(pi/2)</c> is a small NEGATIVE number in float, so the decoded vector
		/// pointed a hair backwards and re-encoded with its yaw rotated half a turn. The owner then
		/// replayed the next slope it stepped onto with a normal wrong by twice the slope angle,
		/// which re-projects both its velocity and its movement basis.
		/// </para>
		/// <para>
		/// The poles are the one place the round trip legitimately moves: every yaw describes the
		/// same direction there, so both sides canonicalise it to zero. That canonical form is
		/// itself a fixed point, which is what the invariant needs.
		/// </para>
		/// </remarks>
		[Test]
		public void AimDirection_EncodeOfDecode_IsAFixedPointAcrossTheWholeDomain()
		{
			int[] yaws = { 0, 1, 1234, 16384, 32768, 49152, 65535 };
			foreach (int yaw in yaws)
			{
				for (int pitch = 0; pitch <= 65535; ++pitch)
				{
					AssertFixedPoint(yaw, pitch);
				}
			}

			int[] pitches = { 0, 1, 2, 7, 100, 32768, 65528, 65533, 65534, 65535 };
			foreach (int pitch in pitches)
			{
				for (int yaw = 0; yaw <= 65535; ++yaw)
				{
					AssertFixedPoint(yaw, pitch);
				}
			}
		}

		/// <summary>Asserts one packed value survives a decode/encode round trip.</summary>
		private static void AssertFixedPoint(int yaw, int pitch)
		{
			uint packed = (uint)yaw | ((uint)pitch << 16);
			uint reencoded = AimDirectionCompression.Encode(AimDirectionCompression.Decode(packed));

			// At a pole yaw is meaningless and is canonicalised to zero by both sides.
			uint expected = (pitch == 0 || pitch == 65535) ? ((uint)pitch << 16) : packed;

			Assert.AreEqual(expected, reencoded,
				$"Re-encoding a decoded direction changed it (yaw {yaw}, pitch {pitch}). The ground-normal " +
				"delta derives its baseline this way on both peers, so this must be a fixed point.");
		}

		/// <summary>The flat ground normal — the value the original bug fired on — is stable.</summary>
		[Test]
		public void AimDirection_FlatGroundNormal_SurvivesRepeatedRoundTrips()
		{
			Vector3 up = new Vector3(0f, 1f, 0f);
			uint first = AimDirectionCompression.Encode(up);

			Vector3 current = up;
			for (int i = 0; i < 16; ++i)
			{
				current = AimDirectionCompression.Quantize(current);
				Assert.AreEqual(first, AimDirectionCompression.Encode(current),
					"Repeated quantisation of straight up must not drift; a delta baseline re-encodes every tick.");
			}

			Assert.AreEqual(0f, current.x, 1e-6f);
			Assert.AreEqual(1f, current.y, 1e-6f);
			Assert.AreEqual(0f, current.z, 1e-6f, "A pole must decode with no horizontal component at all.");
		}

		/// <summary>Quantize is a projection: applying it twice equals applying it once.</summary>
		[Test]
		public void AimDirection_QuantizeIsIdempotentOnArbitraryDirections()
		{
			Vector3[] directions =
			{
				new Vector3(0f, 1f, 0f),
				new Vector3(0f, -1f, 0f),
				new Vector3(1f, 0f, 0f),
				new Vector3(0f, 0f, 1f),
				new Vector3(0.3f, 0.9f, -0.2f),
				new Vector3(-0.7f, -0.7f, 0.1f),
				new Vector3(0.001f, 0.9999f, 0.001f),
			};

			foreach (Vector3 direction in directions)
			{
				Vector3 once = AimDirectionCompression.Quantize(direction);
				Vector3 twice = AimDirectionCompression.Quantize(once);
				Assert.AreEqual(AimDirectionCompression.Encode(once), AimDirectionCompression.Encode(twice),
					$"Quantize must be a projection; {direction} moved on the second application.");
			}
		}

		/// <summary>
		/// The motor rotation that the owner replays from must survive its packing tightly.
		/// </summary>
		/// <remarks>
		/// This rides the once-per-second full reconcile snapshot, and the owner applies it to its
		/// motor and slerps away from it, so the error persists for several ticks and feeds the
		/// movement basis through <c>Motor.CharacterUp</c>. The 32-bit packing this replaced was
		/// measured at 0.59 degrees on the rotation below and 1.24 degrees at its worst.
		/// </remarks>
		[Test]
		public void MotorRotation_SurvivesTheReconcileSnapshotPacking()
		{
			Quaternion[] rotations =
			{
				Quaternion.Euler(12f, 200f, 0f),
				Quaternion.Euler(0f, 90f, 0f),
				Quaternion.Euler(45f, 45f, 45f),
				Quaternion.Euler(-30f, 355f, 12f),
				Quaternion.identity,
			};

			foreach (Quaternion rotation in rotations)
			{
				Writer writer = new Writer();
				writer.WriteQuaternion64(rotation);

				Reader reader = new Reader(writer.GetArraySegment(), null);
				Quaternion decoded = reader.ReadQuaternion64();

				Assert.Less(Quaternion.Angle(rotation, decoded), 0.01f,
					$"{rotation.eulerAngles} lost too much precision for a value the owner replays from.");
				Assert.AreEqual(0, reader.Remaining);
			}
		}
	}
}
