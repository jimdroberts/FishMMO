using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>What the volumetric clouds cost on one quality tier, as the renderer feature needs it.</summary>
	/// <remarks>
	/// The same system runs on every tier — a browser and a desktop draw the same sky — and only
	/// these numbers change: how much of the screen is marched, how finely the result is rebuilt, how
	/// many steps a ray takes, how much of the fine detail is used, and whether the result is steadied
	/// against the last frame.
	/// </remarks>
	public struct CloudTierSettings
	{
		public float Resolution;
		public int Steps;
		public float Detail;
		public bool Temporal;
		public float TemporalBlend;

		/// <summary>
		/// Share of the screen the steadied clouds are rebuilt at, as the tier asked for it; 0 asks
		/// for <see cref="HistoryScaleFor"/>'s own choice. See <see cref="WeatherTierSettings.CloudHistoryScale"/>.
		/// </summary>
		public float HistoryScale;

		/// <summary>
		/// How many of the rebuilt buffer's pixels, across, one marched texel may be spread over
		/// when a tier leaves the choice to the renderer: 4, so every pixel is marched again once in
		/// sixteen frames.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The steadying hands each frame's marched sample to the pixel it landed on, cycling through
		/// sixteen places inside every texel (FishCloudsFeature.SubPixel). A texel spread over
		/// <c>k × k</c> pixels therefore gives each of them a sample about every <c>k²</c> frames, and
		/// that is how quickly the clouds settle and how long a moving edge has to smear.
		/// </para>
		/// <para>
		/// Sixteen frames is what shipped volumetric skies refresh in — Horizon Zero Dawn marches one
		/// pixel in sixteen a frame, Unreal's clouds one in each 4 × 4 — a quarter of a second at sixty
		/// frames. It was 2.5 (6.25 frames), which marched two and a half times as many rays for a sky
		/// that changes over minutes, and the rays are the cost: measured on the thunderstorm stage,
		/// a quarter of the rays took the march from 224 ms to 67 ms and the steps along each ray
		/// hardly mattered (half of them saved a fifth). With the tiers' marches lowered to match,
		/// Balanced and High still rebuild at the screen's own size and Performant at about two
		/// thirds of it.
		/// </para>
		/// </remarks>
		public const float AutoPixelsPerTexel = 4f;

		/// <summary>
		/// The share of the screen the steadied clouds are rebuilt at, for a tier that asked for
		/// <paramref name="asked"/> (0 or less: the renderer's choice) and marches at
		/// <paramref name="march"/>.
		/// </summary>
		/// <remarks>
		/// Never coarser than the march — a rebuild at less than the march's own resolution would
		/// throw marched samples away — and never finer than the screen.
		/// </remarks>
		public static float HistoryScaleFor(float asked, float march)
		{
			march = Mathf.Clamp(march, 0.01f, 1f);
			float wanted = asked > 0f ? asked : march * AutoPixelsPerTexel;
			return Mathf.Clamp(wanted, march, 1f);
		}

		/// <summary>This tier's rebuild share (<see cref="HistoryScaleFor"/>).</summary>
		public float EffectiveHistoryScale => HistoryScaleFor(HistoryScale, Resolution);
	}
}
