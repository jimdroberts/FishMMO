using System;
using System.Reflection;
using FishMMO.Shared;
using FishNet.Serializing;
using KinematicCharacterController;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A reconcile must restore the server's motor state BIT FOR BIT. FishNet's vector deltas rounded position and velocity
	/// to 1 mm (and dropped smaller changes), so the owner replayed from a state the server never had and millimetre
	/// thresholds (the 5 mm ground probe on steep slopes, the climb checks) could decide differently.
	/// </summary>
	public class LosslessReconcileTests
	{
		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			foreach (Type type in new[] { typeof(KinematicCharacterMotorStateDeltaSerializer), typeof(CharacterTransientGroundingReportDeltaSerializer) })
			{
				MethodInfo register = type.GetMethod("RegisterSerializers", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				LogAssert.IsNotNull(register, $"{type.Name} exposes RegisterSerializers.");
				register.Invoke(null, null);
			}
		}

		private static bool SameBits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

		[Test]
		public void EveryFloatDifferenceRoundTripsExactly()
		{
			float[] values = { 0f, -0f, 1e-30f, -1e-30f, 0.001f, -0.001f, 0.0034567f, 1f, -1f, 523.12345f, -523.12346f,
				8191.999f, -8192.001f, float.MaxValue, float.MinValue, float.Epsilon, -float.Epsilon };
			foreach (float a in values)
			{
				foreach (float b in values)
				{
					float restored = LosslessFloatDelta.Apply(a, LosslessFloatDelta.Difference(a, b));
					LogAssert.IsTrue(SameBits(b, restored), $"{a} -> {b} restored as {restored}");
				}
			}
		}

		[Test]
		public void AMotorStateSurvivesBothFormsBitForBit()
		{
			var rng = new System.Random(1234);
			KinematicCharacterMotorState prev = RandomState(rng);
			for (int i = 0; i < 200; ++i)
			{
				KinematicCharacterMotorState next = RandomState(rng);
				// Small moves too, the ones a rounding delta lost: well under a millimetre.
				if (i % 2 == 0)
				{
					next = prev;
					next.Position += new Vector3(0.0003f, -0.0002f, 0.0004f);
					next.BaseVelocity += new Vector3(0.0001f, 0f, -0.0003f);
					next.Rotation = Quaternion.Euler(0f, 0.01f, 0f) * prev.Rotation;
				}

				var full = new Writer();
				full.WriteKinematicCharacterMotorState(next);
				KinematicCharacterMotorState decodedFull = new Reader(full.GetArraySegment(), null).ReadKinematicCharacterMotorState();
				AssertSame(next, decodedFull, $"full #{i}");

				var delta = new Writer();
				LogAssert.IsTrue(delta.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize), $"delta written #{i}");
				var deltaReader = new Reader(delta.GetArraySegment(), null);
				KinematicCharacterMotorState decodedDelta = deltaReader.ReadDelta(prev);
				AssertSame(next, decodedDelta, $"delta #{i}");
				LogAssert.AreEqual(0, deltaReader.Remaining, $"delta #{i} consumed exactly");
				prev = next;
			}
		}

		private static KinematicCharacterMotorState RandomState(System.Random rng)
		{
			float R(float scale) => (float)((rng.NextDouble() * 2d - 1d) * scale);
			return new KinematicCharacterMotorState
			{
				Position = new Vector3(R(4000f), R(300f), R(4000f)),
				Rotation = Quaternion.Euler(R(5f), R(180f), R(5f)),
				BaseVelocity = new Vector3(R(8f), R(15f), R(8f)),
				AttachedRigidbodyVelocity = new Vector3(R(2f), 0f, R(2f)),
				IsSwimming = rng.Next(2) == 0,
				IsFlying = rng.Next(4) == 0,
				ClimbTicks = (byte)rng.Next(256),
			};
		}

		private static void AssertSame(KinematicCharacterMotorState expected, KinematicCharacterMotorState actual, string label)
		{
			LogAssert.IsTrue(SameBits(expected.Position.x, actual.Position.x) && SameBits(expected.Position.y, actual.Position.y) &&
				SameBits(expected.Position.z, actual.Position.z), $"{label}: position exact");
			LogAssert.IsTrue(SameBits(expected.Rotation.x, actual.Rotation.x) && SameBits(expected.Rotation.y, actual.Rotation.y) &&
				SameBits(expected.Rotation.z, actual.Rotation.z) && SameBits(expected.Rotation.w, actual.Rotation.w), $"{label}: rotation exact");
			LogAssert.IsTrue(SameBits(expected.BaseVelocity.x, actual.BaseVelocity.x) && SameBits(expected.BaseVelocity.y, actual.BaseVelocity.y) &&
				SameBits(expected.BaseVelocity.z, actual.BaseVelocity.z), $"{label}: velocity exact");
			LogAssert.IsTrue(SameBits(expected.AttachedRigidbodyVelocity.x, actual.AttachedRigidbodyVelocity.x) &&
				SameBits(expected.AttachedRigidbodyVelocity.z, actual.AttachedRigidbodyVelocity.z), $"{label}: attached velocity exact");
			LogAssert.AreEqual(expected.IsSwimming, actual.IsSwimming, $"{label}: swimming");
			LogAssert.AreEqual(expected.IsFlying, actual.IsFlying, $"{label}: flying");
			LogAssert.AreEqual(expected.ClimbTicks, actual.ClimbTicks, $"{label}: climb ticks");
		}
	}
}
