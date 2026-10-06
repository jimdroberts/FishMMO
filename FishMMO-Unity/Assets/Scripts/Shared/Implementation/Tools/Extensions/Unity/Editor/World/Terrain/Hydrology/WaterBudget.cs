#if UNITY_EDITOR
using System;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The climate's rain as water: metres a year falling, what of it runs off the land, and what an
	/// open water surface loses to the air. What drainage and lake levels are worked out from.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Rain in metres.</b> The moisture model's precipitation is a 0 … 1 index whose middle
	/// (<c>MoistureModel.Midpoint</c>) is the median of an Earth-like world's land; that median is
	/// about <see cref="MedianLandRainMetres"/> a year on Earth, which scales the rest.
	/// </para>
	/// <para>
	/// <b>Evaporation from warmth.</b> Open water loses about a metre a year in a temperate climate and
	/// over two in the hot subtropics; the rate follows the saturation vapour pressure, roughly
	/// doubling every 12 K (<see cref="PotentialEvaporationMetres"/>).
	/// </para>
	/// <para>
	/// <b>Runoff by Budyko's curve</b>, in Fu's form: the share of rain the land gives back to the air
	/// rises with the aridity index (evaporative demand over rain), so a rainforest sheds most of its
	/// rain to rivers and a desert almost none — the observed relation across the world's catchments.
	/// </para>
	/// </remarks>
	public static class WaterBudget
	{
		/// <summary>The median rain on an Earth-like world's land, metres a year.</summary>
		public const float MedianLandRainMetres = 0.7f;

		/// <summary>Open-water evaporation at 15 °C, metres a year.</summary>
		public const float ReferenceEvaporationMetres = 1.0f;

		/// <summary>Fu's catchment parameter: about 2.6 for the world's catchments on average.</summary>
		public const double FuOmega = 2.6;

		/// <summary>Rain in metres a year from the moisture model's 0 … 1 index and its median.</summary>
		public static float RainMetres(float precipitation, float midpoint)
		{
			return Math.Max(0f, precipitation) / Math.Max(1e-4f, midpoint) * MedianLandRainMetres;
		}

		/// <summary>What an open water surface loses a year at a mean temperature, metres.</summary>
		public static float PotentialEvaporationMetres(float celsius)
		{
			double e = ReferenceEvaporationMetres * Math.Exp(0.055 * (celsius - 15.0));
			// Ice-covered most of the year: it sublimates a little and no more.
			if (celsius < -10f)
			{
				e = Math.Min(e, 0.05);
			}
			return (float)Math.Max(0.02, Math.Min(3.0, e));
		}

		/// <summary>What of a year's rain runs off the land, metres: Budyko's curve in Fu's form.</summary>
		public static float RunoffMetres(float rainMetres, float potentialEvaporationMetres)
		{
			if (rainMetres <= 0f)
			{
				return 0f;
			}
			double phi = Math.Max(0.0, potentialEvaporationMetres) / rainMetres;
			double evaporated = 1.0 + phi - Math.Pow(1.0 + Math.Pow(phi, FuOmega), 1.0 / FuOmega);
			return (float)(rainMetres * Math.Max(0.0, 1.0 - evaporated));
		}
	}
}
#endif
