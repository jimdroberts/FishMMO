using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>A kind of colour vision the game can adapt its colours to.</summary>
	public enum ColorVisionMode
	{
		/// <summary>The game's own colours.</summary>
		Normal = 0,
		/// <summary>Red-weak or red-blind: red and green are confused, and red looks dark.</summary>
		Protanopia = 1,
		/// <summary>Green-weak or green-blind, the most common kind: red and green are confused.</summary>
		Deuteranopia = 2,
		/// <summary>Blue-weak or blue-blind: blue and green, and yellow and violet, are confused.</summary>
		Tritanopia = 3,
	}

	/// <summary>
	/// The colours that carry meaning in the game (hostile or friendly, better or worse), for the colour
	/// vision the player has chosen.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The game's own colours lean on red against green: an enemy's name is red and an ally's green, a
	/// worse stat red and a better one green. That is the pair the commonest colour blindness (red-green,
	/// about one man in twelve) cannot tell apart, so the one thing those colours exist to say is lost.
	/// For the three kinds of colour blindness the meanings move to colours from the Okabe-Ito palette,
	/// chosen to stay distinct under each: hostile vermillion against friendly sky blue for red-green,
	/// hostile vermillion against friendly bluish green for blue-yellow, with neutral apart from both.
	/// </para>
	/// <para>
	/// Shared, because the meaning is decided in shared code (FactionController, the tooltip builders);
	/// the client sets <see cref="Mode"/> from the player's options. A server never sets it and keeps
	/// the game's own colours.
	/// </para>
	/// </remarks>
	public static class ColorVision
	{
		private static ColorVisionMode mode = ColorVisionMode.Normal;

		/// <summary>Raised when <see cref="Mode"/> changes.</summary>
		public static event Action OnModeChanged;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			mode = ColorVisionMode.Normal;
		}

		/// <summary>The colour vision the game's colours are adapted to.</summary>
		public static ColorVisionMode Mode
		{
			get => mode;
			set
			{
				if (mode == value)
				{
					return;
				}
				mode = value;
				OnModeChanged?.Invoke();
			}
		}

		// Okabe-Ito (Okabe and Ito 2008): distinct under all three dichromacies.
		private static readonly Color Vermillion = new Color32(213, 94, 0, 255);
		private static readonly Color SkyBlue = new Color32(86, 180, 233, 255);
		private static readonly Color BluishGreen = new Color32(0, 158, 115, 255);
		private static readonly Color Yellow = new Color32(240, 228, 66, 255);
		private static readonly Color LightGrey = new Color32(205, 205, 205, 255);

		/// <summary>A hostile or enemy character's colour.</summary>
		public static Color Hostile => mode == ColorVisionMode.Normal ? TinyColor.ToUnityColor(TinyColor.red) : Vermillion;

		/// <summary>An allied character's colour.</summary>
		public static Color Friendly
		{
			get
			{
				switch (mode)
				{
					case ColorVisionMode.Protanopia:
					case ColorVisionMode.Deuteranopia: return SkyBlue;
					case ColorVisionMode.Tritanopia: return BluishGreen;
					default: return TinyColor.ToUnityColor(TinyColor.green);
				}
			}
		}

		/// <summary>A neutral character's colour.</summary>
		public static Color Neutral
		{
			get
			{
				switch (mode)
				{
					case ColorVisionMode.Protanopia:
					case ColorVisionMode.Deuteranopia: return Yellow;
					case ColorVisionMode.Tritanopia: return LightGrey;
					default: return TinyColor.ToUnityColor(TinyColor.skyBlue);
				}
			}
		}

		/// <summary>An improvement, a met requirement: the tooltip's good tone.</summary>
		public static Color Good
		{
			get
			{
				switch (mode)
				{
					case ColorVisionMode.Protanopia:
					case ColorVisionMode.Deuteranopia: return SkyBlue;
					case ColorVisionMode.Tritanopia: return BluishGreen;
					default: return new Color32(55, 168, 107, 255);
				}
			}
		}

		/// <summary>A penalty, an unmet requirement: the tooltip's bad tone.</summary>
		public static Color Bad => mode == ColorVisionMode.Normal ? (Color)new Color32(200, 56, 74, 255) : Vermillion;
	}
}
