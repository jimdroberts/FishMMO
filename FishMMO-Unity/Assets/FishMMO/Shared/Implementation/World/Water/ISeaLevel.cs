namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// A scene's sea as the water contract needs it: its level at a tick. <c>SeaTide</c> (the tide driven by the world's
	/// moons) implements it; a game without that module can register a flat sea.
	/// </summary>
	public interface ISeaLevel
	{
		/// <summary>The sea's surface height at <paramref name="tick"/>.</summary>
		float LevelAt(uint tick);
	}
}
