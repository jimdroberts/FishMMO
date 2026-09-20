using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// A band of sky between two heights, filled with 3D noise: the unit the whole cloud system is
	/// built from.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A layer is not a cloud and does not contain a list of clouds. It is a slice of the atmosphere
	/// — sea level to 800 m, 800 m to 1400 m, and whatever is stacked above — in which cloud either
	/// is or is not, according to a noise field and how much of it the weather says to keep. What
	/// looks like one cloud is a connected piece of that field; clouds merge, split and grow because
	/// the field does, which is what they do in the sky and what no list of separate clouds ever
	/// quite manages.
	/// </para>
	/// <para>
	/// Every layer is the same machinery with different numbers. A flat stratus deck and a heaped
	/// cumulus field differ by their band's thickness and where the density sits within it; cirrus
	/// differs by being drawn out along the wind; ground fog differs by sitting at sea level. That
	/// is why there is one <see cref="CloudLayer"/> type and not four.
	/// </para>
	/// </remarks>
	[Serializable]
	public class CloudLayer
	{
		public string Name = "Cumulus";

		[Header("Where")]
		[Tooltip("Bottom of the band, in metres above the ground.")]
		[Min(0f)] public float Bottom = 800f;
		[Tooltip("Top of the band, in metres above the ground.")]
		[Min(1f)] public float Top = 1400f;

		[Tooltip("Whether this band's floor follows the weather's condensation level. Air rising off the ground condenses where it cools to its dew point, and that height is lower when the air is damp and higher when it is dry and warm — so a fair-weather deck sits high and a rainy one sits low. The band carries its own thickness with it.")]
		public bool BaseFollowsCondensation = false;

		[Header("Shape")]
		[Tooltip("Metres one tile of the shape noise covers. Bigger means bigger cloud masses.")]
		[Min(100f)] public float NoiseScale = 4200f;
		[Tooltip("Metres one tile of the detail noise covers: the wisps eaten out of the edges.")]
		[Min(20f)] public float DetailScale = 380f;
		[Tooltip("How hard the detail eats in.")]
		[Range(0f, 1f)] public float DetailStrength = 0.45f;
		[Tooltip("How far the noise is drawn out along the wind. 1 is round; higher gives the streaks of cirrus.")]
		[Min(1f)] public float Stretch = 1f;
		[Tooltip("How much of the band's bottom the cloud fills before it thins: low is a flat-based deck, high is a rounded heap.")]
		[Range(0.02f, 0.9f)] public float BaseSoftness = 0.12f;
		[Tooltip("How much of the band's top the cloud thins over. High gives ragged tops.")]
		[Range(0.05f, 1f)] public float TopSoftness = 0.55f;
		[Tooltip("How much the shape changes with height, as a multiple of the band's own thickness. The shape noise is otherwise sampled on the same scale going up as going along, and a band is far thinner than a cloud is wide — so a column came out as one value from floor to ceiling and every cloud was a flat slab. Around 2 gives half a turn of noise over the band's height, which is what puts lumps and hollows into it. Higher breaks a cloud into layers; lower flattens it again.")]
		[Range(0.25f, 8f)] public float VerticalScale = 2f;
		[Tooltip("How much the height a cloud reaches varies from place to place. A cloud's base is flat because condensation happens at one height across a region; its top is lumpy because each rising parcel of air runs out of lift somewhere different. 0 is a level sheet, high is cauliflower.")]
		[Range(0f, 1f)] public float Convection = 0f;

		[Header("How much")]
		[Tooltip("Optical density: how thick and dark the cloud in this band is.")]
		[Range(0f, 4f)] public float Density = 1f;
		[Tooltip("Added to the coverage the weather asks for. Use it for a layer that is always a little there, like cirrus.")]
		[Range(-1f, 1f)] public float CoverageBias = 0f;
		[Tooltip("How much of the weather's cloud cover reaches this layer.")]
		[Range(0f, 2f)] public float CoverageScale = 1f;
		[Tooltip("Coverage above which this layer starts to fill in. A mid sheet only arrives as a front closes the sky over.")]
		[Range(0f, 1f)] public float CoverageOnset = 0f;

		[Header("Motion")]
		[Tooltip("How much faster than the ground wind this band moves. Higher bands run ahead of lower ones.")]
		public float WindScale = 1f;

		[Header("Weather")]
		[Tooltip("Whether the weather's precipitation makes this layer thicker and darker. The rain comes out of the low deck, not out of the cirrus.")]
		public bool CarriesRain = false;
		[Tooltip("Whether a storm cell grows this layer into a tower where it stands.")]
		public bool GrowsStorms = false;
		[Tooltip("The colour a shaded part of this layer tends toward. Ground fog is not the same grey as a thunderhead.")]
		[ColorUsage(false, false)] public Color ShadedTint = new Color(0.62f, 0.68f, 0.82f);

		/// <summary>How thick the band is, in metres.</summary>
		public float Thickness => Mathf.Max(1f, Top - Bottom);

		/// <summary>
		/// How much of this layer the weather asks for, 0..1, given the scene's cloud cover.
		/// </summary>
		public float CoverageFor(float weatherCover)
		{
			float above = CoverageOnset >= 1f ? 0f : Mathf.InverseLerp(CoverageOnset, 1f, weatherCover);
			float asked = Mathf.Clamp01(above * CoverageScale + CoverageBias);
			// A forecast of no cloud has to mean no cloud, whatever a band's own bias would keep in
			// the sky on its own account. Without this gate a band that is "always a little there"
			// is there on a clear day as well, and clearing the weather never clears the sky.
			return asked * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.12f, weatherCover));
		}
	}

	/// <summary>
	/// The layers a sky is made of, lowest first.
	/// </summary>
	public static class CloudLayerDefaults
	{
		/// <summary>
		/// The sky as it comes: the weather band at the bottom, the cloud deck above it, then the
		/// sheet and the fibres. A designer adds bands to this or moves the ones that are here.
		/// </summary>
		public static List<CloudLayer> Sky() => new List<CloudLayer>
		{
			// Sea level to the cloud base: where fog, mist and the weather itself live. Off until
			// the weather calls for it, which is what keeps a fair day clear at eye level.
			new CloudLayer
			{
				Name = "Weather", Bottom = 0f, Top = 800f,
				NoiseScale = 2600f, DetailScale = 180f, DetailStrength = 0.6f,
				BaseSoftness = 0.5f, TopSoftness = 0.75f, Convection = 0.35f,
				Density = 0.55f, CoverageScale = 0f, CoverageBias = 0f, WindScale = 0.6f,
				ShadedTint = new Color(0.78f, 0.82f, 0.88f),
			},
			// The cloud deck: a flat base at 800 m with room to build to 2.4 km. This is the sky a
			// fair day is made of, and the layer the rain comes out of. The depth matters as much as
			// the noise does — a 600 m band gives a cloud nowhere to grow, so every column reaches
			// the ceiling and the deck comes out as a sheet however the convection is set. Given
			// room, the convection decides how much of it each column actually uses.
			new CloudLayer
			{
				Name = "Cumulus", Bottom = 800f, Top = 3200f, BaseFollowsCondensation = true,
				NoiseScale = 12000f, DetailScale = 380f, DetailStrength = 0.45f,
				BaseSoftness = 0.06f, TopSoftness = 0.65f, Convection = 0.8f,
				Density = 1f, CoverageScale = 1f, WindScale = 1f,
				CarriesRain = true, GrowsStorms = true,
			},
			// The sheet a front brings in, well above the heaps and moving faster.
			new CloudLayer
			{
				Name = "Alto", Bottom = 3600f, Top = 4600f,
				NoiseScale = 9000f, DetailScale = 700f, DetailStrength = 0.25f,
				BaseSoftness = 0.3f, TopSoftness = 0.4f, Convection = 0.2f,
				Density = 0.5f, CoverageScale = 1f, CoverageOnset = 0.55f, WindScale = 1.7f,
				ShadedTint = new Color(0.66f, 0.7f, 0.8f),
			},
			// Cirrus: thin, high, and drawn far out along the wind. A little of it in fair weather,
			// less when there is already a sky full of cloud below.
			new CloudLayer
			{
				Name = "Cirrus", Bottom = 7500f, Top = 8600f,
				NoiseScale = 14000f, DetailScale = 1200f, DetailStrength = 0.2f, Stretch = 8f,
				BaseSoftness = 0.35f, TopSoftness = 0.45f,
				Density = 0.18f, CoverageScale = -0.35f, CoverageBias = 0.3f, WindScale = 3f,
				ShadedTint = new Color(0.8f, 0.84f, 0.92f),
			},
		};
	}
}
