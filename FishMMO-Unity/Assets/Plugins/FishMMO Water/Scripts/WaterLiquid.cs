namespace FishMMO.Water
{
	/// <summary>
	/// What a <see cref="WaterSurface"/> is the surface of.
	/// </summary>
	/// <remarks>
	/// <b>Lava is not a kind of sea.</b> It is ten million times as viscous as water, so it carries no
	/// waves and breaks on no shore, and it is opaque, so there is nothing under it to refract, light
	/// with caustics or swim through. A lava surface keeps only what the two have in common — a level,
	/// a disc that follows the camera, a clock — and draws <c>FishMMO/Water/Lava</c> on it; the FFT,
	/// the shore, the breakers, the caustics and the underwater pass all stand down.
	/// </remarks>
	public enum WaterLiquid
	{
		/// <summary>An ocean: <c>FishMMO/Water/Ocean</c> and everything that goes with it.</summary>
		Water = 0,

		/// <summary>Molten rock: <c>FishMMO/Water/Lava</c>, opaque and flat, with nothing of the sea's.</summary>
		Lava = 1,
	}
}
