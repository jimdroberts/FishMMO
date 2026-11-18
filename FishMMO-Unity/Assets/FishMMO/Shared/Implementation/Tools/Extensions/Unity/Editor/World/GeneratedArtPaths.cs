namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Where the generated biome art lives (the World Design module writes it; core tools such as the shader prewarm read
	/// it). Core editor (Phase 4, 2026-11-18).
	/// </summary>
	public static class GeneratedArtPaths
	{
		/// <summary>The generated biome art's root folder.</summary>
		public const string Root = "Assets/Prefabs/Shared/Biomes/Generated";
	}

	/// <summary>The weather content's folders: the substances, the sky profiles and the rest of what the weather generator writes.</summary>
	public static class WeatherContentFolders
	{
		/// <summary>The weather content's root folder.</summary>
		public const string Root = "Assets/Templates/Weather";
		/// <summary>The weather substances (rain, snow, ash…).</summary>
		public const string Substances = Root + "/Substances";
	}
}
