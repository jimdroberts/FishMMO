using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Packs a movement input axis into a single signed byte, and exposes the quantisation so a
	/// producer can commit to the value it is about to send.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Movement axes are inputs in [-1, 1] — usually exactly -1, 0 or +1 from a keyboard, and a
	/// smooth ramp from a stick. They were carried as unpacked floats, four bytes each, paid both in
	/// the absolute entry of every replicate packet and again whenever they changed. A signed byte
	/// resolves 1/127, which is finer than any input device or player can express and well inside
	/// what <c>Vector3.ClampMagnitude</c> does to the pair immediately afterwards.
	/// </para>
	/// <para>
	/// The same rule as <see cref="AimDirectionCompression"/> applies and for the same reason:
	/// <b>quantise at the producer</b>. The owner predicts from the struct it fills in, so storing a
	/// raw axis there would have the owner simulate a movement magnitude the wire cannot carry while
	/// the server and observers simulate the decoded one. The divergence is small per tick and
	/// accumulates over a run of ticks into a position difference that reconcile then has to correct.
	/// </para>
	/// </remarks>
	public static class MoveAxisCompression
	{
		/// <summary>Steps per unit. 127 keeps the encoding symmetric about zero.</summary>
		private const float Steps = 127f;

		/// <summary>
		/// Packs an axis into a signed byte, clamping to [-1, 1].
		/// </summary>
		/// <param name="value">Raw axis value.</param>
		/// <returns>The packed representation.</returns>
		public static sbyte Encode(float value)
		{
			if (float.IsNaN(value))
			{
				return 0;
			}
			// Clamp before scaling: an out-of-range axis from a modified client would otherwise
			// wrap through the cast and reverse the player's movement direction.
			float clamped = Mathf.Clamp(value, -1f, 1f);
			return (sbyte)Mathf.Clamp(Mathf.RoundToInt(clamped * Steps), -127, 127);
		}

		/// <summary>
		/// Unpacks an axis previously produced by <see cref="Encode"/>.
		/// </summary>
		/// <param name="packed">The packed representation.</param>
		/// <returns>
		/// An axis value in [-1, 1] for anything <see cref="Encode"/> can produce, which clamps to
		/// ±127. A raw −128 off the wire decodes to −1.008; the pair is <c>ClampMagnitude</c>'d by
		/// <c>KCCController.SetInputs</c> before it reaches the motor, so the overshoot cannot become
		/// movement, but do not treat this return as pre-clamped.
		/// </returns>
		public static float Decode(sbyte packed) => packed / Steps;

		/// <summary>
		/// Rounds an axis onto the wire's representable set.
		/// </summary>
		/// <remarks>
		/// The call a producer makes before storing movement input. See the note on the class for
		/// why this has to happen on the way in rather than on the way out.
		/// </remarks>
		/// <param name="value">Raw axis value.</param>
		/// <returns>The value the wire will carry.</returns>
		public static float Quantize(float value) => Decode(Encode(value));
	}
}
