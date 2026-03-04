using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Pre-parsed UI theme settings built once from a <see cref="Configuration"/>.
	/// Caches all color, layout, scroll, font, and transition values to eliminate
	/// repeated string-to-value parsing during crawling.
	/// </summary>
	public sealed class UITheme
	{
		/// <summary>Primary UI element color (e.g., backgrounds, normal states).</summary>
		public readonly Color Primary;

		/// <summary>Secondary UI element color (e.g., pressed/selected states).</summary>
		public readonly Color Secondary;

		/// <summary>Highlight color for hovered/focused UI elements.</summary>
		public readonly Color Highlight;

		/// <summary>General background color.</summary>
		public readonly Color Background;

		/// <summary>Default text color.</summary>
		public readonly Color Text;

		/// <summary>Health bar color.</summary>
		public readonly Color Health;

		/// <summary>Mana bar color.</summary>
		public readonly Color Mana;

		/// <summary>Stamina bar color.</summary>
		public readonly Color Stamina;

		/// <summary>Crosshair color.</summary>
		public readonly Color Crosshair;

		/// <summary>Spacing between elements in layout groups (pixels).</summary>
		public readonly float LayoutSpacing;

		/// <summary>Padding applied to left edge of layout groups (pixels).</summary>
		public readonly int PaddingLeft;

		/// <summary>Padding applied to right edge of layout groups (pixels).</summary>
		public readonly int PaddingRight;

		/// <summary>Padding applied to top edge of layout groups (pixels).</summary>
		public readonly int PaddingTop;

		/// <summary>Padding applied to bottom edge of layout groups (pixels).</summary>
		public readonly int PaddingBottom;

		/// <summary>Spacing between cells in a GridLayoutGroup (pixels).</summary>
		public readonly Vector2 GridSpacing;

		/// <summary>Padding applied to GridLayoutGroup containers.</summary>
		public readonly RectOffset GridPadding;

		/// <summary>Scroll sensitivity for ScrollRect components.</summary>
		public readonly float ScrollSensitivity;

		/// <summary>Movement type for ScrollRect components (0=Unrestricted, 1=Elastic, 2=Clamped).</summary>
		public readonly int ScrollMovementType;

		/// <summary>Elasticity for elastic ScrollRect movement.</summary>
		public readonly float ScrollElasticity;

		/// <summary>Whether inertia is enabled on ScrollRect components.</summary>
		public readonly bool ScrollInertia;

		/// <summary>Deceleration rate for ScrollRect inertia.</summary>
		public readonly float ScrollDecelerationRate;

		/// <summary>Fade duration in seconds for Selectable color transitions.</summary>
		public readonly float SelectableFadeDuration;

		/// <summary>Default font size for TMP_Text components (0 = do not override).</summary>
		public readonly float FontSize;

		/// <summary>Minimum auto-size font size for TMP_Text components (0 = do not override).</summary>
		public readonly float FontSizeMin;

		/// <summary>Maximum auto-size font size for TMP_Text components (0 = do not override).</summary>
		public readonly float FontSizeMax;

		/// <summary>
		/// Constructs a theme by parsing all entries from configuration once.
		/// </summary>
		/// <param name="configuration">The configuration containing theme values.</param>
		public UITheme(Configuration configuration)
		{
			Primary = ParseColor("Primary", configuration);
			Secondary = ParseColor("Secondary", configuration);
			Highlight = ParseColor("Highlight", configuration);
			Background = ParseColor("Background", configuration);
			Text = ParseColor("Text", configuration);
			Health = ParseColor("Health", configuration);
			Mana = ParseColor("Mana", configuration);
			Stamina = ParseColor("Stamina", configuration);
			Crosshair = ParseColor("Crosshair", configuration);

			configuration.TryGetFloat("LayoutSpacing", out float spacing, 4f);
			LayoutSpacing = spacing;

			configuration.TryGetInt("PaddingLeft", out int padLeft, 4);
			PaddingLeft = padLeft;

			configuration.TryGetInt("PaddingRight", out int padRight, 4);
			PaddingRight = padRight;

			configuration.TryGetInt("PaddingTop", out int padTop, 4);
			PaddingTop = padTop;

			configuration.TryGetInt("PaddingBottom", out int padBottom, 4);
			PaddingBottom = padBottom;

			configuration.TryGetFloat("GridSpacingX", out float gridSpacingX, 4f);
			configuration.TryGetFloat("GridSpacingY", out float gridSpacingY, 4f);
			GridSpacing = new Vector2(gridSpacingX, gridSpacingY);

			configuration.TryGetInt("GridPaddingLeft", out int gridPadLeft, 4);
			configuration.TryGetInt("GridPaddingRight", out int gridPadRight, 4);
			configuration.TryGetInt("GridPaddingTop", out int gridPadTop, 4);
			configuration.TryGetInt("GridPaddingBottom", out int gridPadBottom, 4);
			GridPadding = new RectOffset(gridPadLeft, gridPadRight, gridPadTop, gridPadBottom);

			configuration.TryGetFloat("ScrollSensitivity", out float scrollSens, 10f);
			ScrollSensitivity = scrollSens;

			configuration.TryGetInt("ScrollMovementType", out int scrollMove, 2);
			ScrollMovementType = scrollMove;

			configuration.TryGetFloat("ScrollElasticity", out float scrollElastic, 0.1f);
			ScrollElasticity = scrollElastic;

			configuration.TryGetBool("ScrollInertia", out bool scrollInertia, true);
			ScrollInertia = scrollInertia;

			configuration.TryGetFloat("ScrollDecelerationRate", out float scrollDecel, 0.135f);
			ScrollDecelerationRate = scrollDecel;

			configuration.TryGetFloat("SelectableFadeDuration", out float fadeDuration, 0.1f);
			SelectableFadeDuration = fadeDuration;

			configuration.TryGetFloat("FontSize", out float fontSize);
			FontSize = fontSize;

			configuration.TryGetFloat("FontSizeMin", out float fontSizeMin);
			FontSizeMin = fontSizeMin;

			configuration.TryGetFloat("FontSizeMax", out float fontSizeMax);
			FontSizeMax = fontSizeMax;
		}

		/// <summary>
		/// Parses a color from configuration using the given name prefix.
		/// Uses TryGetByte directly to avoid intermediate string allocation.
		/// </summary>
		/// <param name="name">The color name prefix (e.g., "Primary" reads "PrimaryColorR/G/B/A").</param>
		/// <param name="configuration">The configuration containing color byte values.</param>
		/// <returns>The parsed Unity Color. Defaults to (0,0,0,0) for missing entries.</returns>
		private static Color ParseColor(string name, Configuration configuration)
		{
			configuration.TryGetByte($"{name}ColorR", out byte r);
			configuration.TryGetByte($"{name}ColorG", out byte g);
			configuration.TryGetByte($"{name}ColorB", out byte b);
			configuration.TryGetByte($"{name}ColorA", out byte a);
			return TinyColor.ToUnityColor(new TinyColor(r, g, b, a));
		}
	}
}