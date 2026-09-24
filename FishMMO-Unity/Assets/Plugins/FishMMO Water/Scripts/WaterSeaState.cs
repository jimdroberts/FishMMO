using UnityEngine;

namespace FishMMO.Water
{
	/// <summary>
	/// How big the sea is, from the wind and how much open water it has blown across.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Fetch-limited, not fully developed — and the difference is the whole point.</b> The
	/// textbook relation Hs = 0.21·U²/g describes a FULLY DEVELOPED sea: the one wind produces
	/// after blowing steadily for a day across hundreds of kilometres of open ocean. A coast almost
	/// never sees one. Its sea is set by the FETCH — how much open water lies upwind — and at the
	/// distances a coastline actually has, that is several times smaller. Measured against the
	/// JONSWAP growth law, a 12 m/s wind gives 3.1 m fully developed and 0.87 m over 20 km of
	/// fetch. Using the first everywhere is what put five-metre seas in front of characters one
	/// to three metres tall.
	/// </para>
	/// <para>
	/// <b>It also makes the wind's DIRECTION matter</b>, as it does on a real coast: blowing onshore
	/// across open water it builds surf; blowing offshore it has almost no fetch and flattens the
	/// beach, however hard it blows.
	/// </para>
	/// </remarks>
	public static class WaterSeaState
	{
		/// <summary>Coefficient of the fully developed limit, gHs/U².</summary>
		public const float DevelopedLimit = 0.21f;

		/// <summary>
		/// Significant wave height, in metres, for a wind blowing across a fetch.
		/// </summary>
		/// <param name="windSpeed">Metres per second at 10 m.</param>
		/// <param name="fetchMetres">Open water upwind, in metres.</param>
		/// <param name="gravity">Surface gravity, m/s².</param>
		/// <remarks>
		/// JONSWAP: gHs/U² = 0.0016·(gF/U²)^½, capped at the fully developed value. The cap is not
		/// a safety rail — past a certain fetch the sea stops growing, because it is already
		/// losing to breaking as much energy as the wind puts in.
		/// </remarks>
		public static float SignificantHeight(float windSpeed, float fetchMetres, float gravity)
		{
			float u = Mathf.Max(0f, windSpeed);
			float g = Mathf.Max(0.05f, gravity);
			if (u < 0.05f)
			{
				return 0f;
			}
			float developed = DevelopedLimit * u * u / g;
			float limited = 0.0016f * u * Mathf.Sqrt(Mathf.Max(0f, fetchMetres) / g);
			return Mathf.Min(developed, limited);
		}

		/// <summary>
		/// Peak period of the sea, in seconds, for a wind blowing across a fetch.
		/// </summary>
		/// <remarks>
		/// <para>
		/// JONSWAP gives the peak FREQUENCY — U·fp/g = 3.5·(gF/U²)^−⅓ — so the period is its
		/// reciprocal, 0.286·(U/g)·(gF/U²)^⅓, capped at the fully developed period.
		/// </para>
		/// <para>
		/// There is no 2π in it, and there was one: it made every period six times too long, so
		/// a gale over twenty kilometres of fetch hit the fully developed cap at 14.6 s when the
		/// real figure is under five. Short fetch makes short, steep chop; only long fetch — or
		/// swell from somewhere else — makes the long, evenly spaced lines of surf.
		/// </para>
		/// </remarks>
		public static float PeakPeriod(float windSpeed, float fetchMetres, float gravity)
		{
			float u = Mathf.Max(0.1f, windSpeed);
			float g = Mathf.Max(0.05f, gravity);
			float developed = 7.14f * u / g;
			float nondimensional = g * Mathf.Max(1f, fetchMetres) / (u * u);
			float limited = 0.286f * (u / g) * Mathf.Pow(nondimensional, 1f / 3f);
			return Mathf.Min(developed, limited);
		}

		/// <summary>Deep-water wavelength of a period, in metres: L = gT²/2π.</summary>
		public static float Wavelength(float period, float gravity)
		{
			return Mathf.Max(0.05f, gravity) * period * period / (2f * Mathf.PI);
		}

		/// <summary>
		/// The wind that would raise this sea if it were fully developed.
		/// </summary>
		/// <remarks>
		/// The FFT spectrum is calibrated against the fully developed relation — measured to within
		/// 5% of it — so the cleanest way to give it a fetch-limited sea is to hand it the wind
		/// that produces that height when developed. Both the height AND the peak wavelength then
		/// come out consistent with each other, which scaling the amplitude alone would not do.
		/// </remarks>
		public static float EquivalentWind(float significantHeight, float gravity)
		{
			return Mathf.Sqrt(Mathf.Max(0f, significantHeight) * Mathf.Max(0.05f, gravity) / DevelopedLimit);
		}
	}
}
