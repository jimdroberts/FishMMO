namespace FishMMO.Client
{
	/// <summary>Where the world passes find their assets in the editor when the feature has none assigned.</summary>
	public static class WorldPassAssets
	{
		/// <summary>The compute holding the depth pyramid and light table kernels (core).</summary>
		public const string PassesComputePath = "Assets/FishMMO/Shaders/FishWorldPasses.compute";
	}
}
