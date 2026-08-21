namespace FishMMO.Client
{
	/// <summary>
	/// Bit-flag positions for visual effects on <see cref="UITKWorldLabel"/>.
	/// Combine with <see cref="FishMMO.Shared.IntBitExtensions"/> to enable multiple effects simultaneously.
	/// <example>
	/// <code>
	/// int flags = 0;
	/// flags.EnableBit(LabelEffect.FadeIn);
	/// flags.EnableBit(LabelEffect.FloatUp);
	/// flags.EnableBit(LabelEffect.ScaleUp);
	/// UITKLabelMaker.Display3D("Hit!", pos, Color.red, 2f, 1f, false, flags);
	/// </code>
	/// </example>
	/// </summary>
	public enum LabelEffect
	{
		/// <summary>Fades alpha from 0 to 1 over the first half of the label's lifetime.</summary>
		FadeIn = 0,

		/// <summary>Fades alpha from 1 to 0 over the second half of the label's lifetime.</summary>
		FadeOut = 1,

		/// <summary>Scales the label up from 0 to target font size over its lifetime.</summary>
		ScaleUp = 2,

		/// <summary>Scales the label down from target font size to 0 over its lifetime.</summary>
		ScaleDown = 3,

		/// <summary>Oscillates scale with a sine wave for a pulsing heartbeat effect.</summary>
		Pulse = 4,

		/// <summary>Oscillates Y position with a sine wave for a bouncing effect.</summary>
		Bounce = 5,

		/// <summary>Moves the label upward at a constant speed.</summary>
		FloatUp = 6,

		/// <summary>Moves the label in a random direction with simulated gravity, floating then falling.</summary>
		FloatRandom = 7,

		/// <summary>Oscillates Y position with a sine wave for a gentle wave effect (slower than Bounce).</summary>
		Wave = 8,

		/// <summary>Shakes the label position randomly for an impact/jitter effect.</summary>
		Shake = 9,
	}
}
