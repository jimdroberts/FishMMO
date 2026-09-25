namespace FishMMO.Water
{
	/// <summary>
	/// The one term the ocean's "Show one term" setting draws on its own, opaque — for finding what a
	/// finished image cannot say. The numbers are the forward pass's own (<c>_DebugView</c> in
	/// FishWaterForwardPass.hlsl) and must stay in step with it.
	/// </summary>
	/// <remarks>
	/// An enum type because the material's dropdown names it: Unity's <c>[Enum(...)]</c> drawer takes
	/// at most seven name and value pairs written out inline, and with thirteen it failed to build
	/// and logged an error every time the material was touched.
	/// </remarks>
	public enum WaterDebugView
	{
		Off = 0,
		Sediment = 1,
		ClarityField = 2,
		Transmittance = 3,
		Fresnel = 4,
		Opacity = 5,
		LightOnTheWater = 6,
		Shadow = 7,
		DepthTo10m = 8,
		BodyColour = 9,
		BehindTheSurface = 10,
		Reflection = 11,
		Foam = 12,
	}
}
