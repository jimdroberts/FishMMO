using System;
using FishMMO.Logging;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The player's colour vision: the UI tab's ACCESSIBILITY rows.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two parts, both following <see cref="Mode"/>:
	/// </para>
	/// <list type="number">
	/// <item><b>Safe colours</b>, the part that matters. <see cref="ColorVision.Mode"/> moves the colours that
	/// carry meaning (hostile, friendly, neutral; a better or worse stat) to ones that stay distinct, and
	/// <see cref="UITKThemeManager"/> puts a <c>fish-cvd--*</c> class on every panel root, under which
	/// FishMMO-Theme.uss redefines the meaningful tokens (resource bars, danger, rarity, map markers).</item>
	/// <item><b>The correction filter</b> (<see cref="ColorVisionFeature"/>): a full-screen daltonisation of
	/// the 3D view after post-processing, so the world itself separates better. It runs under the UI,
	/// which already has its safe colours, and costs one full-screen pass while on.</item>
	/// </list>
	/// <para>
	/// The values are held in statics, set at boot (<see cref="ClientSettings.ApplyAll"/>) and on every
	/// change, so the filter reads fields each frame and never the settings store.
	/// </para>
	/// </remarks>
	public static class ClientColorVisionSettings
	{
		/// <summary>The default strength of the correction filter, 0 to 1.</summary>
		public const float DefaultStrength = 1f;

		private static bool filter = true;
		private static float strength = DefaultStrength;

		/// <summary>Raised after any of these settings changes.</summary>
		public static event Action OnChanged;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			filter = true;
			strength = DefaultStrength;
		}

		/// <summary>The colour vision chosen, as stored; Normal for a fresh install.</summary>
		public static ColorVisionMode Mode
		{
			get
			{
				int value = ClientSettings.GetInt(ClientSettings.ColorVisionModeKey, (int)ColorVisionMode.Normal);
				return value >= (int)ColorVisionMode.Normal && value <= (int)ColorVisionMode.Tritanopia
					? (ColorVisionMode)value
					: ColorVisionMode.Normal;
			}
		}

		/// <summary>Whether the correction filter is wanted (it runs only when <see cref="Mode"/> is not Normal).</summary>
		public static bool FilterEnabled => filter;

		/// <summary>How far the filter corrects, 0 (not at all) to 1 (fully).</summary>
		public static float FilterStrength => strength;

		/// <summary>Whether the correction filter draws this frame.</summary>
		public static bool FilterActive => filter && strength > 0f && ColorVision.Mode != ColorVisionMode.Normal;

		/// <summary>Reads the stored settings into effect: the palette, the panels' classes and the filter.</summary>
		public static void ApplySaved()
		{
			filter = ClientSettings.GetBool(ClientSettings.ColorFilterKey, true);
			strength = ClientSettings.GetFloat(ClientSettings.ColorFilterStrengthKey, DefaultStrength, 0f, 1f);
			ColorVision.Mode = Mode;
			UITKThemeManager.ApplyToAll();
		}

		/// <summary>Writes the colour vision and puts it into effect.</summary>
		public static void SetMode(ColorVisionMode mode)
		{
			ClientSettings.Set(ClientSettings.ColorVisionModeKey, (int)mode);
			ColorVision.Mode = mode;
			UITKThemeManager.ApplyToAll();
			Changed();
		}

		/// <summary>Writes whether the correction filter is wanted.</summary>
		public static void SetFilterEnabled(bool value)
		{
			ClientSettings.Set(ClientSettings.ColorFilterKey, value);
			filter = value;
			Changed();
		}

		/// <summary>Writes the correction filter's strength, 0 to 1.</summary>
		public static void SetFilterStrength(float value)
		{
			strength = Mathf.Clamp01(value);
			ClientSettings.Set(ClientSettings.ColorFilterStrengthKey, strength);
			Changed();
		}

		private static void Changed()
		{
			try
			{
				OnChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Log.Error("ClientColorVisionSettings", "A colour-vision subscriber threw.", ex);
			}
		}
	}
}
