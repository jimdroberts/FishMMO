using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Packs an aim direction into 32 bits as a quantised yaw/pitch pair, and — more importantly —
	/// exposes the quantisation itself so a producer can commit to the value it is about to send.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this exists.</b> The ability system is deterministic: every peer replays the same
	/// input stream through the same simulation and derives the same ability objects locally, so
	/// the inputs must be bit-identical everywhere. Aim used to travel as a
	/// <see cref="Quaternion"/> through FishNet's <c>WriteQuaternion32</c>, which is lossy. The
	/// owning client simulated with its exact camera rotation while the server and every observer
	/// simulated with the decoded one — so an owner's predicted shot and the server's authoritative
	/// shot diverged by the quantisation error on every single cast.
	/// </para>
	/// <para>
	/// <b>The rule this type enforces.</b> Quantise before you predict, never after. A producer
	/// calls <see cref="Quantize"/> and stores the result as the input; the wire then carries a
	/// value that is already exactly representable, so decoding it reproduces what the producer
	/// simulated with. <see cref="Decode"/> of <see cref="Encode"/> is the identity on any value
	/// that came out of <see cref="Quantize"/>, which is what the round-trip tests assert.
	/// </para>
	/// <para>
	/// <b>Why direction rather than a rotation.</b> Aim is only ever consumed as a direction —
	/// <c>KCCController.SetInputs</c> takes <c>rotation * Vector3.forward</c> to build the movement
	/// basis, and the ability path takes the same forward as its trace direction. Carrying a full
	/// rotation shipped a degree of freedom (roll) that nothing reads and that made the value
	/// harder to represent exactly.
	/// </para>
	/// <para>
	/// Resolution is 360/65536 ≈ 0.0055° of yaw and 180/65536 ≈ 0.0027° of pitch — about 5 mm of
	/// lateral error at 50 m, comfortably below the precision any aiming decision depends on.
	/// </para>
	/// </remarks>
	public static class AimDirectionCompression
	{
		/// <summary>Direction used when asked to encode a zero-length or non-finite vector.</summary>
		/// <remarks>
		/// A deterministic fallback matters more than which direction it is: a NaN reaching the
		/// simulation would diverge peers permanently, whereas everyone agreeing on "forward" is
		/// merely wrong in a visible, debuggable way.
		/// </remarks>
		public static readonly Vector3 FallbackDirection = Vector3.forward;

		private const float YawStepsPerTurn = 65536f;
		private const float PitchSpan = Mathf.PI;

		/// <summary>
		/// Packs a direction into 32 bits: low 16 bits yaw, high 16 bits pitch.
		/// </summary>
		/// <param name="direction">Direction to encode. Need not be normalised.</param>
		/// <returns>The packed representation.</returns>
		public static uint Encode(Vector3 direction)
		{
			if (!IsFinite(direction))
			{
				direction = FallbackDirection;
			}

			float sqrMagnitude = direction.sqrMagnitude;
			if (sqrMagnitude < 1e-12f)
			{
				direction = FallbackDirection;
			}
			else
			{
				direction /= Mathf.Sqrt(sqrMagnitude);
			}

			// Yaw around world up, measured so that +Z is 0. Wrapped into [0,1) before scaling so
			// the -pi and +pi ends land on the same step rather than on adjacent ones.
			float yawTurns = Mathf.Atan2(direction.x, direction.z) / (2f * Mathf.PI);
			yawTurns -= Mathf.Floor(yawTurns);
			uint yaw = (uint)Mathf.RoundToInt(yawTurns * YawStepsPerTurn) & 0xFFFFu;

			/* Pitch from the horizontal plane, via Atan2 against the horizontal magnitude rather
			 * than Asin of y.
			 *
			 * The two agree mathematically, but Asin is numerically flat near +/-1: for the 32
			 * pitch indices closest to straight up and straight down, Asin(Sin(theta)) did not
			 * return theta in float, so Encode(Decode(p)) came back as a different index. That
			 * mattered far more than it looks, because the reconcile delta for ground normals
			 * derives its baseline by re-encoding — the writer from the raw motor value, the reader
			 * from its own decoded one — so any disagreement was added straight onto the result.
			 * Atan2 stays exact as the horizontal magnitude collapses to zero. */
			float horizontal = Mathf.Sqrt((direction.x * direction.x) + (direction.z * direction.z));
			float pitchNormalised = (Mathf.Atan2(direction.y, horizontal) / PitchSpan) + 0.5f;
			uint pitch = (uint)Mathf.Clamp(Mathf.RoundToInt(pitchNormalised * 65535f), 0, 65535);

			/* Straight up and straight down have no yaw to speak of, so pin it.
			 *
			 * At the poles every yaw describes the same direction, and Decode returns a vector with
			 * no horizontal component at all — so a re-encode can only ever answer yaw 0. Encoding
			 * yaw 0 here as well is what makes the round trip a fixed point instead of a value that
			 * changes the first time it is re-encoded. */
			if (pitch == 0u || pitch == 65535u)
			{
				yaw = 0u;
			}

			return yaw | (pitch << 16);
		}

		/// <summary>
		/// Unpacks a direction previously produced by <see cref="Encode"/>.
		/// </summary>
		/// <param name="packed">The packed representation.</param>
		/// <returns>A unit direction.</returns>
		public static Vector3 Decode(uint packed)
		{
			float yawRadians = (packed & 0xFFFFu) / YawStepsPerTurn * (2f * Mathf.PI);
			float pitchRadians = (((packed >> 16) & 0xFFFFu) / 65535f - 0.5f) * PitchSpan;

			/* Never negative. Cos(+/-pi/2) is -4.371139e-08 in float, not zero, and that sign is
			 * enough to rotate the recovered yaw by half a turn on the next re-encode — which, for
			 * the perfectly flat ground normal (0,1,0), is the single most common value in the
			 * game. Clamping makes a pole decode to exactly (0,+/-1,0). */
			float cosPitch = Mathf.Max(0f, Mathf.Cos(pitchRadians));
			return new Vector3(
				Mathf.Sin(yawRadians) * cosPitch,
				Mathf.Sin(pitchRadians),
				Mathf.Cos(yawRadians) * cosPitch);
		}

		/// <summary>
		/// Rounds a direction onto the wire's representable set.
		/// </summary>
		/// <remarks>
		/// This is the call a producer makes before storing aim as input. Feeding the simulation
		/// the quantised value — rather than the raw one it happens to hold locally — is what keeps
		/// the owner, the server and every observer simulating identical input.
		/// </remarks>
		/// <param name="direction">Raw direction.</param>
		/// <returns>The direction the wire will carry.</returns>
		public static Vector3 Quantize(Vector3 direction) => Decode(Encode(direction));

		/// <summary>
		/// Rebuilds a rotation from an aim direction, for the code paths that still want one.
		/// </summary>
		/// <remarks>
		/// Roll is not carried — nothing reads it — so this picks a reference up-axis and falls back
		/// to a second one when the direction is close to vertical, where <see cref="Quaternion.LookRotation"/>
		/// is otherwise degenerate. Deterministic for a given direction, which is the property that
		/// matters; a camera pitched to exact vertical would lose only its roll, which no consumer
		/// reads.
		/// </remarks>
		/// <param name="direction">A direction, ideally already quantised.</param>
		/// <returns>A rotation whose forward is <paramref name="direction"/>.</returns>
		public static Quaternion ToRotation(Vector3 direction)
		{
			if (!IsFinite(direction) || direction.sqrMagnitude < 1e-12f)
			{
				direction = FallbackDirection;
			}

			Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.9995f
				? Vector3.forward
				: Vector3.up;

			return Quaternion.LookRotation(direction, up);
		}

		private static bool IsFinite(Vector3 v)
		{
			return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
				&& !float.IsNaN(v.y) && !float.IsInfinity(v.y)
				&& !float.IsNaN(v.z) && !float.IsInfinity(v.z);
		}
	}
}
