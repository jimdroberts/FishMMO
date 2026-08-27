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
	/// Coverage for the aim-direction representation that replaced the replicated camera rotation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The ability system is deterministic — every peer replays the same inputs and derives the same
	/// ability objects locally — so aim has to be bit-identical everywhere. It previously travelled
	/// as a <see cref="Quaternion"/> through FishNet's lossy <c>WriteQuaternion32</c> while the
	/// owning client simulated with its exact camera rotation, so an owner's predicted shot and the
	/// server's authoritative shot diverged on every cast.
	/// </para>
	/// <para>
	/// <see cref="DivergenceIsRealWithTheOldRepresentation_AndGoneWithTheNew"/> is the load-bearing
	/// test: it measures the old scheme's error and the new scheme's, side by side, so the fix
	/// cannot silently regress into "both are zero because nothing is being measured".
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AimDirectionTests
	{
		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			Type[] serializerTypes =
			{
				typeof(CharacterReconcileDataDeltaSerializer),
				typeof(CharacterReplicateDataDeltaSerializer),
				typeof(CharacterTransientGroundingReportDeltaSerializer),
				typeof(KinematicCharacterMotorStateDeltaSerializer),
				typeof(CharacterAttributeResourceStateSerializer),
			};
			foreach (Type t in serializerTypes)
			{
				t.GetMethod("RegisterSerializers", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
					.Invoke(null, null);
			}
		}

		/// <summary>A spread of camera orientations including the awkward ones.</summary>
		private static Quaternion[] SampleCameraRotations()
		{
			return new[]
			{
				Quaternion.identity,
				Quaternion.Euler(0f, 40f, 0f),
				Quaternion.Euler(0f, 41.237f, 0f),
				Quaternion.Euler(12.5f, 217.31f, 0f),
				Quaternion.Euler(-33.7f, 359.9f, 0f),
				Quaternion.Euler(-60f, 180f, 0f),
				Quaternion.Euler(0f, -179.999f, 0f),   // wrap boundary
				Quaternion.Euler(89.5f, 45f, 0f),      // near-vertical
				Quaternion.Euler(-89.5f, 45f, 0f),
			};
		}

		[Test]
		public void Quantize_IsExactlyWhatTheWireCarries()
		{
			double worst = 0.0;
			foreach (Quaternion rotation in SampleCameraRotations())
			{
				Vector3 raw = rotation * Vector3.forward;
				Vector3 quantised = AimDirectionCompression.Quantize(raw);

				// The whole contract: a quantised value survives encode/decode untouched, so the
				// producer can commit to it and every peer reproduces it.
				Vector3 afterWire = AimDirectionCompression.Decode(AimDirectionCompression.Encode(quantised));
				float error = Vector3.Angle(quantised, afterWire);
				worst = Math.Max(worst, error);

				LogAssert.IsTrue(error < 0.0001f,
					$"Quantize must be idempotent through the wire; {rotation.eulerAngles} drifted {error:F6} degrees. " +
					"If this fails the producer cannot commit to the value it sends and determinism is lost.");
				LogAssert.IsTrue(Mathf.Abs(quantised.magnitude - 1f) < 0.001f,
					$"Decoded aim must be a unit vector; got magnitude {quantised.magnitude:F5}.");
			}
			TestContext.WriteLine($"MEASURE quantise idempotence worst error: {worst:F6} degrees");
		}

		[Test]
		public void Quantize_StaysWithinItsAdvertisedResolution()
		{
			double worst = 0.0;
			foreach (Quaternion rotation in SampleCameraRotations())
			{
				Vector3 raw = rotation * Vector3.forward;
				worst = Math.Max(worst, Vector3.Angle(raw, AimDirectionCompression.Quantize(raw)));
			}

			TestContext.WriteLine($"MEASURE quantisation error vs raw aim: {worst:F5} degrees " +
				$"(~{Mathf.Tan((float)worst * Mathf.Deg2Rad) * 50f * 100f:F2}cm lateral at 50m)");

			LogAssert.IsTrue(worst < 0.02f,
				$"Quantisation error reached {worst:F5} degrees, beyond the ~0.0055 degree yaw step this " +
				"representation advertises. Aim precision feeds combat, so a regression here matters.");
		}

		[Test]
		public void DivergenceIsRealWithTheOldRepresentation_AndGoneWithTheNew()
		{
			/* Side-by-side negative control.
			 *
			 * OLD: the owner simulated with its exact camera forward while the wire carried a
			 * Quaternion32; server and observers decoded a slightly different rotation and traced a
			 * slightly different shot. Reproduced here by pushing a rotation through the same
			 * FishNet codec the old field used.
			 *
			 * NEW: the owner quantises first, so what it simulates with is exactly what the wire
			 * carries and every peer reproduces it. */
			double worstOld = 0.0;
			double worstNew = 0.0;

			foreach (Quaternion rotation in SampleCameraRotations())
			{
				Writer writer = new Writer();
				writer.WriteQuaternion32(rotation);
				Quaternion decoded = new Reader(writer.GetArraySegment(), null).ReadQuaternion32();

				Vector3 ownerAimOld = rotation * Vector3.forward;      // what the owner used
				Vector3 peerAimOld = decoded * Vector3.forward;        // what everyone else used
				worstOld = Math.Max(worstOld, Vector3.Angle(ownerAimOld, peerAimOld));

				Vector3 ownerAimNew = AimDirectionCompression.Quantize(rotation * Vector3.forward);
				Vector3 peerAimNew = AimDirectionCompression.Decode(AimDirectionCompression.Encode(ownerAimNew));
				worstNew = Math.Max(worstNew, Vector3.Angle(ownerAimNew, peerAimNew));
			}

			TestContext.WriteLine(
				$"MEASURE owner-vs-peer aim divergence: old(Quaternion32)={worstOld:F5} degrees, " +
				$"new(quantised direction)={worstNew:F6} degrees");
			TestContext.WriteLine(
				$"  at 50m that is {Mathf.Tan((float)worstOld * Mathf.Deg2Rad) * 50f * 100f:F1}cm of shot displacement, " +
				"applied on every cast");

			LogAssert.IsTrue(worstOld > 0.001f,
				$"The old representation must actually diverge ({worstOld:F6} degrees measured) or this test " +
				"is not reproducing the bug it exists to guard, and the comparison below proves nothing.");
			LogAssert.IsTrue(worstNew < 0.0001f,
				$"The new representation must not diverge at all; measured {worstNew:F6} degrees.");
			LogAssert.IsTrue(worstNew < worstOld,
				"The new representation must be strictly better than the one it replaced.");
		}

		[Test]
		public void ReplicateData_AimSurvivesTheRealSerializersExactly()
		{
			foreach (Quaternion rotation in SampleCameraRotations())
			{
				CharacterReplicateData data = default;
				data.AimDirection = AimDirectionCompression.Quantize(rotation * Vector3.forward);
				data.MoveAxisForward = 1f;

				// Full serializer.
				Writer full = new Writer();
				full.Write(data);
				CharacterReplicateData fullBack = new Reader(full.GetArraySegment(), null).Read<CharacterReplicateData>();
				LogAssert.IsTrue(Vector3.Angle(data.AimDirection, fullBack.AimDirection) < 0.0001f,
					$"Full serializer must carry aim exactly; {rotation.eulerAngles} drifted " +
					$"{Vector3.Angle(data.AimDirection, fullBack.AimDirection):F6} degrees.");

				// Delta serializer against a different previous aim.
				CharacterReplicateData prev = default;
				prev.AimDirection = AimDirectionCompression.Quantize(Quaternion.Euler(5f, 5f, 0f) * Vector3.forward);
				Writer delta = new Writer();
				bool wrote = delta.WriteDelta(prev, data, DeltaSerializerOption.RootSerialize);
				LogAssert.IsTrue(wrote, "A changed aim must produce a delta.");
				CharacterReplicateData deltaBack = new Reader(delta.GetArraySegment(), null).ReadDelta(prev);
				LogAssert.IsTrue(Vector3.Angle(data.AimDirection, deltaBack.AimDirection) < 0.0001f,
					$"Delta serializer must carry aim exactly; {rotation.eulerAngles} drifted " +
					$"{Vector3.Angle(data.AimDirection, deltaBack.AimDirection):F6} degrees.");
			}
		}

		[Test]
		public void DegenerateAim_ResolvesDeterministically_NotToNaN()
		{
			/* A zero-length or non-finite aim is the shape a default-initialised NPC produced before
			 * the aim was replicated — the case that had every client tracing from the world origin
			 * along +Z. It must resolve to something fixed rather than to NaN, because a NaN reaching
			 * a deterministic simulation diverges peers permanently. */
			foreach (Vector3 bad in new[]
			{
				Vector3.zero,
				new Vector3(float.NaN, 0f, 0f),
				new Vector3(float.PositiveInfinity, 1f, 0f),
			})
			{
				Vector3 quantised = AimDirectionCompression.Quantize(bad);
				LogAssert.IsTrue(Mathf.Abs(quantised.magnitude - 1f) < 0.001f,
					$"Degenerate aim {bad} must resolve to a unit vector, got {quantised}.");
				LogAssert.IsTrue(Vector3.Angle(quantised, AimDirectionCompression.FallbackDirection) < 0.01f,
					$"Degenerate aim {bad} must resolve to the documented fallback, got {quantised}.");

				Quaternion rotation = AimDirectionCompression.ToRotation(bad);
				LogAssert.IsFalse(float.IsNaN(rotation.x) || float.IsNaN(rotation.y) ||
					float.IsNaN(rotation.z) || float.IsNaN(rotation.w),
					$"ToRotation({bad}) produced NaN, which would desync every peer that replayed it.");
			}
		}

		[Test]
		public void ToRotation_ReproducesTheAimAsForward_IncludingNearVertical()
		{
			// Movement rebuilds a rotation from the aim to form its planar basis, so forward must
			// come back out intact — including where LookRotation is otherwise degenerate.
			foreach (Vector3 direction in new[]
			{
				Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
				new Vector3(0.3f, 0.7f, -0.6f).normalized,
				Vector3.up, Vector3.down,
			})
			{
				Vector3 quantised = AimDirectionCompression.Quantize(direction);
				Vector3 roundTripped = AimDirectionCompression.ToRotation(quantised) * Vector3.forward;
				float error = Vector3.Angle(quantised, roundTripped);
				LogAssert.IsTrue(error < 0.05f,
					$"ToRotation({direction}) lost the aim: forward came back {error:F5} degrees off.");
			}
		}

		[Test]
		public void Benchmark_AimFieldCost_OldVersusNew()
		{
			// Steady turn: consecutive ticks differ by a small yaw step, which is the case the
			// packed representation is meant to win.
			Quaternion prevRot = Quaternion.Euler(3f, 40.0f, 0f);
			Quaternion nextRot = Quaternion.Euler(3f, 41.2f, 0f);

			int oldFullBytes = Bytes(w => w.WriteQuaternion32(nextRot));
			int oldDeltaBytes = Bytes(w => w.WriteDeltaQuaternion(prevRot, nextRot, option: DeltaSerializerOption.RootSerialize));

			Vector3 prevAim = AimDirectionCompression.Quantize(prevRot * Vector3.forward);
			Vector3 nextAim = AimDirectionCompression.Quantize(nextRot * Vector3.forward);
			int newFullBytes = Bytes(w => w.WriteUInt32Unpacked(AimDirectionCompression.Encode(nextAim)));
			int newDeltaBytes = Bytes(w => w.WriteDeltaUInt32(
				AimDirectionCompression.Encode(prevAim), AimDirectionCompression.Encode(nextAim),
				DeltaSerializerOption.RootSerialize));

			TestContext.WriteLine(
				$"MEASURE aim field: old full={oldFullBytes}B delta={oldDeltaBytes}B | " +
				$"new full={newFullBytes}B delta={newDeltaBytes}B");

			LogAssert.IsTrue(newDeltaBytes <= oldDeltaBytes,
				$"The packed aim delta ({newDeltaBytes}B) must not cost more than the quaternion delta " +
				$"({oldDeltaBytes}B) it replaced.");
		}

		private static int Bytes(Action<Writer> write)
		{
			Writer writer = new Writer();
			write(writer);
			return writer.Length;
		}
	}
}
