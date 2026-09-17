using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.Atlas
{
	/// <summary>
	/// A layer of the world a body's scenes sit in: the Overworld, the Underworld, cloud cities…
	/// Scenes in different layers never overlap each other and are drawn separately.
	/// </summary>
	[CreateAssetMenu(fileName = "New Atlas Layer", menuName = "FishMMO/World/Atlas Layer", order = 15)]
	public class WorldAtlasLayer : CachedScriptableObject<WorldAtlasLayer>, ICachedObject
	{
		public string DisplayName;
		[Tooltip("Lower layers are listed first.")]
		public int SortOrder;
		[Tooltip("Weather for scenes in this layer whose own mode is Auto. Auto here too: dungeons get none, the rest their own.")]
		public WeatherSceneMode DefaultWeather = WeatherSceneMode.Auto;
		[Tooltip("Colour of this layer's scenes and routes in the designer.")]
		public Color Tint = new Color(0.55f, 0.75f, 0.95f, 1f);
		[Tooltip("Underground: the atlas draws it below the surface.")]
		public bool Underground;

		public string ResolvedName => string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName;
	}
}
