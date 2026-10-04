using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Mist in patches near the ground: wisps a couple of metres across up to banks of tens of metres,
	/// wherever the air just over the ground is brought to saturation — and nowhere else.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The fog layer (<see cref="FogLayer"/>) is the weather's fog: a layer over the whole scene, its banks
	/// hundreds of metres across. This is the other thing a humid place does. In a rain forest, by a
	/// river at dawn, over wet ground after a shower, the air at screen height is a degree or two short
	/// of its dew point, and the few metres next to the ground close that gap in some places and not in
	/// others: a hollow pools the chilled air, wet leaves and soil breathe water into the air over them,
	/// and the eddies of a light wind carry it about in shreds. The result is scattered, local and small.
	/// </para>
	/// <para>
	/// So the readiness here is the air's dew-point spread at the ground, less what the ground can do
	/// to close it — radiative cooling on a clear, calm night (a few kelvin by dawn: Oke, Boundary Layer
	/// Climates), and evaporation from wet ground and from rain that is falling into near-saturated air
	/// — and a wind that mixes the near-ground air with the drier air above takes it away. Within
	/// <see cref="OnsetSpread"/> kelvin of saturation the first patches appear in the most favoured
	/// spots; at saturation they are everywhere the ground lets them be.
	/// </para>
	/// <para>
	/// That is the open ground's figure (<see cref="Deficit"/>). Each spot then closes the gap further on
	/// its own (FishMist.hlsl, the twin of <see cref="Local"/>): a hollow pools the night's chilled air
	/// (<see cref="HollowCooling"/>), open water breathes into the air over and beside it
	/// (<see cref="WaterMoisture"/>), and a tree canopy transpires and keeps the floor under it shaded and
	/// still (<see cref="CanopyMoisture"/>). So a lake shore or a forest makes mist on an evening the open
	/// fields beside it stay clear, and the noise decides the shreds within.
	/// </para>
	/// <para>
	/// Plain arithmetic on plain inputs, so the client and its tests agree on it.
	/// </para>
	/// </remarks>
	public static class GroundMist
	{
		/// <summary>How far short of saturation (K) the near-ground air can stand and still make the first wisps.</summary>
		public const float OnsetSpread = 2.5f;
		/// <summary>The most a clear, calm night cools the air at the ground below the air above it, K.</summary>
		public const float NightCooling = 4f;
		/// <summary>What soaked ground adds to the near-ground air's dew point, K.</summary>
		public const float WetGround = 2.5f;
		/// <summary>What rain falling into the air adds as it evaporates, K.</summary>
		public const float FallingRain = 1f;
		/// <summary>What the deepest hollow adds by chilling the air that drains into it, K, on a clear calm night (<see cref="NightCalm"/>).</summary>
		public const float HollowCooling = 1.5f;
		/// <summary>How deep a dip counts as a full hollow, m (twin: FishMist.hlsl).</summary>
		public const float FullHollow = 4f;
		/// <summary>What open water adds over and right beside it, K: half by day, all of it on a calm night, when the water is the warmer.</summary>
		public const float WaterMoisture = 2f;
		/// <summary>What a closed tree canopy adds over the floor under it, K.</summary>
		public const float CanopyMoisture = 1.5f;

		/// <summary>How thin the mist is at its thinnest drawn: visibility inside a patch, m (a mist is a kilometre or more).</summary>
		public const float ThinVisibility = 900f;
		/// <summary>How thick at its thickest: visibility inside a patch, m.</summary>
		public const float ThickVisibility = 150f;
		/// <summary>The shallowest and deepest the patches stand, m.</summary>
		public const float ShallowDepth = 3f;
		public const float DeepDepth = 40f;

		/// <summary>
		/// How much the night chills the ground, 0..1: from sunset, most by dawn, gone within an hour or so
		/// of sunrise; only under a clear sky, which the ground radiates to, and only in a calm, since a
		/// breeze of a few m/s mixes the chilled air up.
		/// </summary>
		public static float NightCalm(float windMetresPerSecond, float clearSky, float sunAltitudeDegrees)
		{
			float calm = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 6f, Mathf.Max(0f, windMetresPerSecond)));
			float night = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, -4f, sunAltitudeDegrees));
			return Mathf.Clamp01(clearSky) * night * calm;
		}

		/// <summary>How much of the mist a wind leaves, 0..1: a fresh wind tears it up and mixes it into the air above.</summary>
		public static float Stirred(float windMetresPerSecond)
		{
			return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 10f, Mathf.Max(0f, windMetresPerSecond)));
		}

		/// <summary>
		/// How far short of saturation the air over open ground stays, K: the dew-point spread, less the
		/// night's chill, wet ground and falling rain. At or under zero the open ground itself makes mist.
		/// </summary>
		/// <param name="spreadKelvin">The air's temperature less its dew point at the ground, K.</param>
		/// <param name="windMetresPerSecond">The ten-metre wind, m/s.</param>
		/// <param name="wetness">How wet the ground is, 0..1.</param>
		/// <param name="rain">How much rain is falling, 0..1.</param>
		/// <param name="clearSky">How much of the sky is clear, 0..1: what the ground radiates its heat to.</param>
		/// <param name="sunAltitudeDegrees">The sun's height, degrees.</param>
		public static float Deficit(float spreadKelvin, float windMetresPerSecond, float wetness, float rain, float clearSky, float sunAltitudeDegrees)
		{
			if (float.IsNaN(spreadKelvin))
			{
				return float.PositiveInfinity;
			}
			float cooling = NightCooling * NightCalm(windMetresPerSecond, clearSky, sunAltitudeDegrees);
			float moistening = WetGround * Mathf.Clamp01(wetness) + FallingRain * Mathf.Clamp01(rain);
			return Mathf.Max(0f, spreadKelvin) - cooling - moistening;
		}

		/// <summary>
		/// What a spot does to the open ground's deficit, K: its hollow (m of dip), how near open water it is
		/// (0..1) and how closed the canopy over it is (0..1). The twin of FishMist.hlsl's.
		/// </summary>
		public static float LocalClosing(float hollowMetres, float water, float canopy, float nightCalm)
		{
			float hollow = Mathf.Clamp01(hollowMetres / FullHollow);
			return HollowCooling * hollow * Mathf.Clamp01(nightCalm)
				+ WaterMoisture * Mathf.Clamp01(water) * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(nightCalm))
				+ CanopyMoisture * Mathf.Clamp01(canopy);
		}

		/// <summary>How ready the air at a spot is, 0..1, from its own deficit (K) and the wind's stirring.</summary>
		public static float Local(float deficitKelvin, float stirred)
		{
			return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(OnsetSpread, 0f, deficitKelvin)) * Mathf.Clamp01(stirred);
		}

		/// <summary>
		/// How ready the air over open ground is to make mist, 0 not at all … 1 everywhere the ground lets it.
		/// </summary>
		public static float Potential(float spreadKelvin, float windMetresPerSecond, float wetness, float rain, float clearSky, float sunAltitudeDegrees)
		{
			float deficit = Deficit(spreadKelvin, windMetresPerSecond, wetness, rain, clearSky, sunAltitudeDegrees);
			return float.IsInfinity(deficit) ? 0f : Local(deficit, Stirred(windMetresPerSecond));
		}

		/// <summary>
		/// How ready the most favoured spot could be — a deep hollow under trees at the water's edge — so
		/// the mist is drawn at all whenever anywhere might hold some.
		/// </summary>
		public static float BestPotential(float deficitKelvin, float stirred, float nightCalm)
		{
			if (float.IsInfinity(deficitKelvin))
			{
				return 0f;
			}
			return Local(deficitKelvin - LocalClosing(FullHollow, 1f, 1f, nightCalm), stirred);
		}

		/// <summary>The extinction inside a patch at full strength, 1/m (Koschmieder: 3.912 over the visibility).</summary>
		public static float Extinction(float potential)
		{
			float p = Mathf.Clamp01(potential);
			return p <= 0f ? 0f : 3.912f / Mathf.Lerp(ThinVisibility, ThickVisibility, p);
		}

		/// <summary>How deep the deepest patches stand at this readiness, m.</summary>
		public static float Depth(float potential)
		{
			return Mathf.Lerp(ShallowDepth * 2f, DeepDepth, Mathf.Clamp01(potential));
		}
	}
}
