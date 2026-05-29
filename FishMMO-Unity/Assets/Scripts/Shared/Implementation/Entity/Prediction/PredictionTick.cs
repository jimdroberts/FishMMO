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
		public uint Value { get; }

		// Internal: only CharacterReplicateData (same assembly) can construct this.
		internal PredictionTick(uint value) => Value = value;

		public static implicit operator uint(PredictionTick t) => t.Value;

		public override string ToString() => Value.ToString();
	}
}