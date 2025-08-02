using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Abstract base class for canvas type settings handlers.
	/// </summary>
	public abstract class BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies settings from configuration to the given UI component.
		/// </summary>
		/// <param name="component">The UI component to apply settings to.</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public abstract void ApplySettings(object component, Configuration configuration);

		/// <summary>
		/// Parses a color from configuration using the given color name prefix.
		/// </summary>
		/// <param name="name">The color name prefix (e.g., "Primary").</param>
		/// <param name="configuration">The configuration object containing color values.</param>
		/// <returns>The parsed Unity Color.</returns>
		public Color ParseColor(string name, Configuration configuration)
		{
			// Try to get and parse each color channel; default to 0 if missing or invalid
			if (!configuration.TryGet($"{name}ColorR", out string colorR) ||
				!byte.TryParse(colorR, out byte R))
			{
				R = 0;
			}
			if (!configuration.TryGet($"{name}ColorG", out string colorG) ||
				!byte.TryParse(colorG, out byte G))
			{
				G = 0;
			}
			if (!configuration.TryGet($"{name}ColorB", out string colorB) ||
				!byte.TryParse(colorB, out byte B))
			{
				B = 0;
			}
			if (!configuration.TryGet($"{name}ColorA", out string colorA) ||
				!byte.TryParse(colorA, out byte A))
			{
				A = 0;
			}
			// Convert to Unity Color using TinyColor helper
			return TinyColor.ToUnityColor(new TinyColor(R, G, B, A));
		}
	}
}