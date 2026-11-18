using System;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Serializing;
using KinematicCharacterController;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Correctness coverage for the two payload fields that were narrowed after the per-field cost
	/// audit: movement axes (four bytes each to one) and the three grounding normals (a full
	/// <see cref="Vector3"/> each to a packed direction).
	/// </summary>
	/// <remarks>
	/// Both are lossy encodings feeding a deterministic simulation, so each needs two things
	/// established rather than assumed: that the loss is inside the bound claimed for it, and that
	/// the value a producer commits to survives the wire untouched. Where a change replaced an
	/// existing lossy encoding — the normals did — the new error is measured against the old rather
	/// than merely asserted to be small.
	/// </remarks>
	[TestFixture]
	public class PredictionQuantizationTests
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

		private static readonly float[] AxisSamples =
		{
			-1f, -0.9999f, -0.75f, -0.3333f, -0.0079f, 0f, 0.0079f, 0.3333f, 0.5f, 0.75f, 1f,
		};

		// ── Movement axes ────────────────────────────────────────────────────

		[Test]
		public void MoveAxis_QuantizeIsExactlyWhatTheWireCarries()
		{
			foreach (float raw in AxisSamples)
			{
				float committed = MoveAxisCompression.Quantize(raw);
				float afterWire = MoveAxisCompression.Decode(MoveAxisCompression.Encode(committed));

				LogAssert.AreEqual(committed, afterWire,
					$"Quantize must be idempotent through the wire for {raw}; a producer that cannot " +
					"commit to its own value reintroduces owner-versus-peer divergence.");
			}
		}

		[Test]
		public void MoveAxis_StaysWithinItsAdvertisedResolution()
		{
			double worst = 0.0;
			foreach (float raw in AxisSamples)
			{
				worst = Math.Max(worst, Math.Abs(raw - MoveAxisCompression.Quantize(raw)));
			}

			TestContext.WriteLine($"MEASURE move axis quantisation error: {worst:F6} (1/127 step = {1f / 127f:F6})");

			// Half a step is the most a round-to-nearest can be out.
			LogAssert.IsTrue(worst <= (1.0 / 254.0) + 1e-6,
				$"Move axis error reached {worst:F6}, beyond the half-step this encoding allows.");
		}

		[Test]
		public void MoveAxis_OutOfRangeClamps_WithoutFlippingDirection()
		{
			/* An axis outside [-1,1] should saturate, never wrap. Scaling before clamping would let
			 * a modified client send 3.0 and have it cast through the signed byte into a negative
			 * value — the player would move backwards on the server while walking forwards locally,
			 * which is both an exploit and a desync. */
			foreach (float hostile in new[] { 2f, 5f, 300f, -2f, -5f, -300f, float.MaxValue, float.MinValue })
			{
				float decoded = MoveAxisCompression.Quantize(hostile);

				LogAssert.IsTrue(decoded >= -1.0001f && decoded <= 1.0001f,
					$"Out-of-range axis {hostile} must clamp into [-1,1]; got {decoded}.");
				LogAssert.IsTrue(Mathf.Sign(decoded) == Mathf.Sign(hostile),
					$"Out-of-range axis {hostile} wrapped to {decoded} — the sign flipped, which would " +
					"reverse the player's movement direction on the receiving peer.");
			}

			LogAssert.AreEqual(0f, MoveAxisCompression.Quantize(float.NaN),
				"A NaN axis must resolve to zero, not propagate into the motor.");
		}

		[Test]
		public void MoveAxis_SurvivesTheRealSerializersExactly()
		{
			foreach (float raw in AxisSamples)
			{
				CharacterReplicateData data = default;
				data.AimDirection = Vector3.forward;
				data.MoveAxisForward = MoveAxisCompression.Quantize(raw);
				data.MoveAxisRight = MoveAxisCompression.Quantize(-raw);

				Writer full = new Writer();
				full.Write(data);
				CharacterReplicateData fullBack = new Reader(full.GetArraySegment(), null).Read<CharacterReplicateData>();
				LogAssert.AreEqual(data.MoveAxisForward, fullBack.MoveAxisForward, $"Full serializer, forward axis {raw}.");
				LogAssert.AreEqual(data.MoveAxisRight, fullBack.MoveAxisRight, $"Full serializer, right axis {raw}.");

				CharacterReplicateData prev = default;
				prev.AimDirection = Vector3.forward;
				Writer delta = new Writer();
				delta.WriteDelta(prev, data, DeltaSerializerOption.RootSerialize);
				CharacterReplicateData deltaBack = new Reader(delta.GetArraySegment(), null).ReadDelta(prev);
				LogAssert.AreEqual(data.MoveAxisForward, deltaBack.MoveAxisForward, $"Delta serializer, forward axis {raw}.");
				LogAssert.AreEqual(data.MoveAxisRight, deltaBack.MoveAxisRight, $"Delta serializer, right axis {raw}.");
			}
		}

		// ── Grounding normals ────────────────────────────────────────────────

		private static Vector3[] NormalSamples()
		{
			return new[]
			{
				Vector3.up,
				new Vector3(0.15f, 0.98f, 0.05f).normalized,
				new Vector3(-0.42f, 0.87f, 0.26f).normalized,
				new Vector3(0.5f, 0.5f, 0.7071f).normalized,      // 60 degree slope, the stability limit
				new Vector3(0.01f, 0.9999f, -0.01f).normalized,
				new Vector3(0.7071f, 0.7071f, 0f).normalized,
			};
		}

		[Test]
		public void GroundNormals_PackedBeatsTheVector3DeltaTheyReplaced()
		{
			/* Side-by-side, because the claim being made is not "the new error is small" but "the
			 * new error is smaller than what shipped before". FishNet's Vector3 delta quantises
			 * components to 0.001, which on a unit normal is around a tenth of a degree; the packed
			 * direction resolves ~0.0055 degrees of yaw. */
			double worstOld = 0.0;
			double worstNew = 0.0;

			foreach (Vector3 normal in NormalSamples())
			{
				Vector3 prev = Vector3.up;

				Writer oldWriter = new Writer();
				oldWriter.WriteDeltaVector3(prev, normal, DeltaSerializerOption.RootSerialize);
				Vector3 oldBack = new Reader(oldWriter.GetArraySegment(), null).ReadDeltaVector3(prev);
				worstOld = Math.Max(worstOld, Vector3.Angle(normal, oldBack));

				Vector3 newBack = AimDirectionCompression.Decode(AimDirectionCompression.Encode(normal));
				worstNew = Math.Max(worstNew, Vector3.Angle(normal, newBack));
			}

			TestContext.WriteLine(
				$"MEASURE ground normal error: old(Vector3 delta)={worstOld:F5} degrees, new(packed)={worstNew:F5} degrees");

			LogAssert.IsTrue(worstNew <= worstOld + 1e-4,
				$"The packed normal ({worstNew:F5} degrees) must be at least as precise as the Vector3 delta " +
				$"it replaced ({worstOld:F5} degrees), or this trade is losing accuracy as well as bytes.");
			LogAssert.IsTrue(worstNew < 0.02f,
				$"Packed normal error {worstNew:F5} degrees exceeds the encoding's advertised resolution.");
		}

		[Test]
		public void GroundNormals_SurviveTheMotorStateRoundTrip()
		{
			foreach (Vector3 normal in NormalSamples())
			{
				KinematicCharacterMotorState state = default;
				state.Rotation = Quaternion.identity;
				state.GroundingStatus = default;
				state.GroundingStatus.FoundAnyGround = true;
				state.GroundingStatus.IsStableOnGround = true;
				state.GroundingStatus.GroundNormal = normal;
				state.GroundingStatus.InnerGroundNormal = normal;
				state.GroundingStatus.OuterGroundNormal = normal;

				// Full serializer.
				Writer full = new Writer();
				full.Write(state);
				KinematicCharacterMotorState fullBack = new Reader(full.GetArraySegment(), null).Read<KinematicCharacterMotorState>();
				AssertNormal(normal, fullBack.GroundingStatus.GroundNormal, "full/GroundNormal");
				AssertNormal(normal, fullBack.GroundingStatus.InnerGroundNormal, "full/InnerGroundNormal");
				AssertNormal(normal, fullBack.GroundingStatus.OuterGroundNormal, "full/OuterGroundNormal");

				// Delta serializer against a flat-ground baseline.
				KinematicCharacterMotorState prev = default;
				prev.Rotation = Quaternion.identity;
				prev.GroundingStatus = default;
				prev.GroundingStatus.GroundNormal = Vector3.up;
				prev.GroundingStatus.InnerGroundNormal = Vector3.up;
				prev.GroundingStatus.OuterGroundNormal = Vector3.up;

				Writer delta = new Writer();
				delta.WriteDelta(prev, state, DeltaSerializerOption.RootSerialize);
				KinematicCharacterMotorState deltaBack = new Reader(delta.GetArraySegment(), null).ReadDelta(prev);
				AssertNormal(normal, deltaBack.GroundingStatus.GroundNormal, "delta/GroundNormal");
				AssertNormal(normal, deltaBack.GroundingStatus.InnerGroundNormal, "delta/InnerGroundNormal");
				AssertNormal(normal, deltaBack.GroundingStatus.OuterGroundNormal, "delta/OuterGroundNormal");
			}
		}

		[Test]
		public void GroundNormals_SlopeClassificationIsUnaffected()
		{
			/* What the normals are actually for. The player prefab sets MaxStableSlopeAngle to 60,
			 * so the only way this encoding could matter to gameplay is by moving a normal across
			 * that threshold. Probed either side of the line at a hundredth of a degree, which is
			 * already finer than the encoding's own step. */
			const float maxStableSlopeAngle = 60f;

			foreach (float slope in new[] { 59.99f, 60f, 60.01f, 0f, 30f, 89.9f })
			{
				Vector3 normal = Quaternion.Euler(slope, 0f, 0f) * Vector3.up;
				Vector3 decoded = AimDirectionCompression.Quantize(normal);

				bool stableBefore = Vector3.Angle(Vector3.up, normal) <= maxStableSlopeAngle;
				bool stableAfter = Vector3.Angle(Vector3.up, decoded) <= maxStableSlopeAngle;

				// A normal sitting exactly on the boundary can legitimately fall either side of it;
				// anything further out must not move.
				if (Mathf.Abs(slope - maxStableSlopeAngle) > 0.005f)
				{
					LogAssert.AreEqual(stableBefore, stableAfter,
						$"A {slope} degree slope changed stability classification through the encoding " +
						$"({stableBefore} -> {stableAfter}). That would make a character slide where the " +
						"server says it stands.");
				}
			}
		}

		[Test]
		public void Benchmark_NarrowedFields_OldVersusNew()
		{
			Vector3[] normals =
			{
				new Vector3(0.15f, 0.98f, 0.05f).normalized,
				new Vector3(0.14f, 0.98f, 0.06f).normalized,
				new Vector3(0.16f, 0.97f, 0.04f).normalized,
			};

			int normalsOldAbsolute = Bytes(w => { foreach (Vector3 n in normals) w.WriteVector3(n); });
			int normalsNewAbsolute = Bytes(w => { foreach (Vector3 n in normals) w.WriteUInt32Unpacked(AimDirectionCompression.Encode(n)); });

			int normalsOldDelta = Bytes(w =>
			{
				foreach (Vector3 n in normals) w.WriteDeltaVector3(Vector3.up, n, DeltaSerializerOption.RootSerialize);
			});
			int normalsNewDelta = Bytes(w =>
			{
				foreach (Vector3 n in normals)
					w.WriteDeltaUInt32(AimDirectionCompression.Encode(Vector3.up), AimDirectionCompression.Encode(n),
						DeltaSerializerOption.RootSerialize);
			});

			int axesOld = Bytes(w => { w.WriteSingle(0.75f); w.WriteSingle(-0.3333f); });
			int axesNew = Bytes(w =>
			{
				w.WriteInt8Unpacked(MoveAxisCompression.Encode(0.75f));
				w.WriteInt8Unpacked(MoveAxisCompression.Encode(-0.3333f));
			});

			TestContext.WriteLine(
				$"MEASURE 3 ground normals: absolute {normalsOldAbsolute}B -> {normalsNewAbsolute}B, " +
				$"delta {normalsOldDelta}B -> {normalsNewDelta}B");
			TestContext.WriteLine($"MEASURE 2 move axes: {axesOld}B -> {axesNew}B");

			LogAssert.IsTrue(normalsNewAbsolute < normalsOldAbsolute, "Packed normals must be smaller absolutely.");
			LogAssert.IsTrue(normalsNewDelta < normalsOldDelta, "Packed normals must be smaller on the delta path.");
			LogAssert.IsTrue(axesNew < axesOld, "Packed axes must be smaller.");
		}

		private static void AssertNormal(Vector3 expected, Vector3 actual, string context)
		{
			float error = Vector3.Angle(expected, actual);
			LogAssert.IsTrue(error < 0.02f,
				$"{context}: normal came back {error:F5} degrees off, beyond the encoding's resolution.");
			LogAssert.IsTrue(Mathf.Abs(actual.magnitude - 1f) < 0.001f,
				$"{context}: normal must stay unit length; magnitude was {actual.magnitude:F5}.");
		}

		private static int Bytes(Action<Writer> write)
		{
			Writer writer = new Writer();
			write(writer);
			return writer.Length;
		}
	}
}
