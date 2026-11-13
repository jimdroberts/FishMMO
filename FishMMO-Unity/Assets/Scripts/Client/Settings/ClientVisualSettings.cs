using System;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// A detail level the player picks for one visual system, over the overall Quality preset.
	/// </summary>
	public enum VisualLevel
	{
		/// <summary>Follow the Quality preset.</summary>
		Auto = 0,
		/// <summary>The Performant preset's budget for this system.</summary>
		Low = 1,
		/// <summary>The Balanced preset's budget for this system.</summary>
		Medium = 2,
		/// <summary>The High Fidelity preset's budget for this system.</summary>
		High = 3,
	}

	/// <summary>
	/// The player's sky, weather and ambient-life settings: the Graphics tab's SKY, WEATHER and AMBIENT
	/// LIFE rows.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Overlays, never edits.</b> Every one of these systems takes its budget from the Weather Render
	/// Profile, a ScriptableObject: writing a player's choice into it would, in the editor, rewrite the
	/// asset. So each setting is read where the system reads its profile and combined with it there
	/// (switched off, or a different tier's numbers), and the profile is left as authored.
	/// </para>
	/// <para>
	/// <b>Levels are tiers.</b> A <see cref="VisualLevel"/> other than Auto picks which Quality preset's
	/// numbers a system uses (<see cref="TierLevel"/> → <c>WeatherRenderProfile.TierFor</c>), so Low
	/// clouds on the High Fidelity preset are exactly the Performant preset's clouds: values already
	/// tuned and measured, not a multiplier nobody has looked at.
	/// </para>
	/// <para>
	/// <b>Read every frame, cheaply.</b> The values are read from the settings store once per frame at
	/// most and held, so a system asking several times a frame pays a field read; a change made in the
	/// options panel, a profile load or a reset is picked up on the next frame.
	/// </para>
	/// </remarks>
	public static class ClientVisualSettings
	{
		/// <summary>Raised when any of these settings is changed through this class.</summary>
		public static event Action OnChanged;

		/// <summary>One row the Graphics tab builds: a switch or a level, in a section.</summary>
		public readonly struct Row
		{
			public readonly string Section;
			public readonly string Key;
			public readonly string Label;
			public readonly bool IsLevel;
			public readonly bool DefaultOn;
			public readonly string Tooltip;

			public Row(string section, string key, string label, bool isLevel, bool defaultOn, string tooltip)
			{
				Section = section;
				Key = key;
				Label = label;
				IsLevel = isLevel;
				DefaultOn = defaultOn;
				Tooltip = tooltip;
			}
		}

		/// <summary>The section of the Graphics tab a row belongs to, as the name of its list container.</summary>
		public const string SkySection = "options-sky-list";
		/// <summary>See <see cref="SkySection"/>.</summary>
		public const string WeatherSection = "options-weather-list";
		/// <summary>See <see cref="SkySection"/>.</summary>
		public const string LifeSection = "options-life-list";

		/// <summary>
		/// Every row, in the order shown. The defaults here are the defaults the systems act on: a fresh
		/// install draws everything, at the Quality preset's own level.
		/// </summary>
		public static readonly Row[] Rows =
		{
			new Row(SkySection, ClientSettings.CloudQualityKey, "Cloud Quality", true, true,
				"How finely the clouds are marched. Auto follows the Quality preset; Low costs a fraction of High."),
			new Row(SkySection, ClientSettings.CloudShadowsKey, "Cloud Shadows", false, true,
				"The clouds' shadows moving over the ground."),
			new Row(SkySection, ClientSettings.GodRaysKey, "Sun Shafts", false, true,
				"Shafts of light through gaps in the cloud."),

			new Row(WeatherSection, ClientSettings.WeatherEffectsKey, "Weather Effects", true, true,
				"How much rain, snow, dust, distant storm rain and the like is drawn. Auto follows the Quality preset."),
			new Row(WeatherSection, ClientSettings.VolumetricFogKey, "Volumetric Fog", false, true,
				"Fog with depth and light near you. Off, the fog is still drawn, more simply."),
			new Row(WeatherSection, ClientSettings.HeatHazeKey, "Heat Haze", false, true,
				"The shimmer over hot ground and lava. A full-screen distortion."),
			new Row(WeatherSection, ClientSettings.LightningFlashesKey, "Lightning Flashes", false, true,
				"Lightning lighting up the whole scene. Off, bolts are still drawn but the sky does not flash."),
			new Row(WeatherSection, ClientSettings.FallingLeavesKey, "Falling Leaves", false, true,
				"Leaves drifting down under trees."),

			new Row(LifeSection, ClientSettings.WildlifeKey, "Wildlife", false, true,
				"Birds, bats, small animals and insects."),
			new Row(LifeSection, ClientSettings.WildlifeDensityKey, "Wildlife Amount", true, true,
				"How many animals can be about at once. Auto is the full amount."),
			new Row(LifeSection, ClientSettings.SeaLifeKey, "Sea Life", false, true,
				"Fish, rays, whales and the like under the water."),
			new Row(LifeSection, ClientSettings.FloatingDebrisKey, "Floating Debris", false, true,
				"Weed, driftwood and ice floating on the sea."),
		};

		private static int cachedFrame = -1;
		private static VisualLevel cloudQuality;
		private static bool cloudShadows = true;
		private static bool godRays = true;
		private static VisualLevel weatherEffects;
		private static bool volumetricFog = true;
		private static bool heatHaze = true;
		private static bool lightningFlashes = true;
		private static bool fallingLeaves = true;
		private static bool wildlife = true;
		private static VisualLevel wildlifeDensity;
		private static bool seaLife = true;
		private static bool floatingDebris = true;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			cachedFrame = -1;
		}

		/// <summary>The cloud march's level (<see cref="SkySystem"/> SetCloudGlobals).</summary>
		public static VisualLevel CloudQuality { get { Refresh(); return cloudQuality; } }
		/// <summary>Whether the clouds shade the ground.</summary>
		public static bool CloudShadows { get { Refresh(); return cloudShadows; } }
		/// <summary>Whether the sun's shafts through the cloud are drawn.</summary>
		public static bool GodRays { get { Refresh(); return godRays; } }
		/// <summary>The precipitation, motes, splashes, rain curtains, vortices and meteors' level.</summary>
		public static VisualLevel WeatherEffects { get { Refresh(); return weatherEffects; } }
		/// <summary>Whether the froxel fog volume runs (FishVolumetricFogFeature); the height fog draws in its place.</summary>
		public static bool VolumetricFog { get { Refresh(); return volumetricFog; } }
		/// <summary>Whether the heat shimmer is drawn (FishHeatShimmerFeature).</summary>
		public static bool HeatHaze { get { Refresh(); return heatHaze; } }
		/// <summary>Whether lightning lights the scene; the bolts are drawn either way.</summary>
		public static bool LightningFlashes { get { Refresh(); return lightningFlashes; } }
		/// <summary>Whether leaves fall under the trees.</summary>
		public static bool FallingLeaves { get { Refresh(); return fallingLeaves; } }
		/// <summary>Whether the land's ambient life is drawn (AmbientLifeSystem, its caves included).</summary>
		public static bool Wildlife { get { Refresh(); return wildlife; } }
		/// <summary>The share of the ambient-life budget drawn: Low a third, Medium two thirds, Auto and High all of it.</summary>
		public static float WildlifeScale
		{
			get
			{
				Refresh();
				switch (wildlifeDensity)
				{
					case VisualLevel.Low: return 0.35f;
					case VisualLevel.Medium: return 0.65f;
					default: return 1f;
				}
			}
		}
		/// <summary>Whether the sea's life is drawn (SeaLifeSystem).</summary>
		public static bool SeaLife { get { Refresh(); return seaLife; } }
		/// <summary>Whether the sea's floating weed, wood and ice are drawn (SeaFloatSystem).</summary>
		public static bool FloatingDebris { get { Refresh(); return floatingDebris; } }

		/// <summary>The Quality level whose tier a system at <paramref name="level"/> takes its budget from.</summary>
		public static int TierLevel(VisualLevel level)
		{
			switch (level)
			{
				case VisualLevel.Low: return 0;
				case VisualLevel.Medium: return 1;
				case VisualLevel.High: return 2;
				default: return QualitySettings.GetQualityLevel();
			}
		}

		/// <summary>The Quality level the clouds take their march from.</summary>
		public static int CloudTierLevel => TierLevel(CloudQuality);

		/// <summary>The Quality level the weather's particles, curtains, vortices and meteors take their budget from.</summary>
		public static int WeatherTierLevel => TierLevel(WeatherEffects);

		/// <summary>Reads a switch row's value.</summary>
		public static bool GetBool(string key, bool fallback) => ClientSettings.GetBool(key, fallback);

		/// <summary>Reads a level row's value.</summary>
		public static VisualLevel GetLevel(string key)
		{
			int value = ClientSettings.GetInt(key, (int)VisualLevel.Auto);
			return value >= (int)VisualLevel.Auto && value <= (int)VisualLevel.High ? (VisualLevel)value : VisualLevel.Auto;
		}

		/// <summary>Writes a switch row and tells whoever listens.</summary>
		public static void SetBool(string key, bool value)
		{
			ClientSettings.Set(key, value);
			Changed();
		}

		/// <summary>Writes a level row and tells whoever listens.</summary>
		public static void SetLevel(string key, VisualLevel level)
		{
			ClientSettings.Set(key, (int)level);
			Changed();
		}

		private static void Changed()
		{
			cachedFrame = -1;
			try
			{
				OnChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Log.Error("ClientVisualSettings", "A visual-settings subscriber threw.", ex);
			}
		}

		private static void Refresh()
		{
			int frame = Time.frameCount;
			if (frame == cachedFrame)
			{
				return;
			}
			cachedFrame = frame;
			cloudQuality = GetLevel(ClientSettings.CloudQualityKey);
			cloudShadows = GetBool(ClientSettings.CloudShadowsKey, true);
			godRays = GetBool(ClientSettings.GodRaysKey, true);
			weatherEffects = GetLevel(ClientSettings.WeatherEffectsKey);
			volumetricFog = GetBool(ClientSettings.VolumetricFogKey, true);
			heatHaze = GetBool(ClientSettings.HeatHazeKey, true);
			lightningFlashes = GetBool(ClientSettings.LightningFlashesKey, true);
			fallingLeaves = GetBool(ClientSettings.FallingLeavesKey, true);
			wildlife = GetBool(ClientSettings.WildlifeKey, true);
			wildlifeDensity = GetLevel(ClientSettings.WildlifeDensityKey);
			seaLife = GetBool(ClientSettings.SeaLifeKey, true);
			floatingDebris = GetBool(ClientSettings.FloatingDebrisKey, true);
		}
	}
}
