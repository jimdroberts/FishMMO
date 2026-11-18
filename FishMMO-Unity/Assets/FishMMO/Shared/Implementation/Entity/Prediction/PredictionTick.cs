namespace FishMMO.Shared
{
	/// <summary>
	/// A tick value that can only be legitimately produced from a replicate input
	/// via <see cref="CharacterReplicateData.GetPredictionTick"/>.
	/// Because the constructor is internal, callers outside this assembly cannot
	/// construct one from a raw uint (e.g. TimeManager.LocalTick) without an
	/// explicit, intentional conversion — the compiler enforces correct tick sourcing
	/// on every Apply() call in the prediction path.
	/// </summary>
	public readonly struct PredictionTick
	{
		/// <summary>
		/// The underlying tick value.
		/// </summary>
		public uint Value { get; }

		/// <summary>
		/// Creates a <see cref="PredictionTick"/> from a raw tick value.
		/// Internal: only <see cref="CharacterReplicateData"/> can construct this.
		/// </summary>
		/// <param name="value">The raw network tick value.</param>
		internal PredictionTick(uint value) => Value = value;

		/// <summary>
		/// Implicitly converts a <see cref="PredictionTick"/> to its underlying <see cref="uint"/> value.
		/// </summary>
		/// <param name="t">The tick to convert.</param>
		public static implicit operator uint(PredictionTick t) => t.Value;

		/// <summary>
		/// Returns the tick value as a string.
		/// </summary>
		public override string ToString() => Value.ToString();
	}
}