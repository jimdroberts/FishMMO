using UnityEngine;

namespace FishMMO.Water
{
	/// <summary>
	/// What a river does as it goes over a fall, from the measured hydraulics of free-falling rectangular jets: how
	/// fast and how thick it leaves the brink, how far it falls before it breaks into jets, how far it spreads, and
	/// how thick it is where it lands. Pure arithmetic, for the curtain (<see cref="InlandWaterRenderer"/>) and tests.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The brink.</b> A river running off a free edge passes through critical flow just upstream, at the critical
	/// depth y_c = (q²/g)^⅓ for its discharge per metre of lip q, and the water at the edge itself is shallower,
	/// y_b ≈ 0.715 y_c (Rouse's free overfall), so it leaves at V_b = q / y_b. A 0.6 m deep river at 1 m/s leaves at
	/// 2.5 m/s, 0.24 m thick: twice as fast as the mean speed the curtain used to throw it at, and so twice as far out.
	/// </para>
	/// <para>
	/// <b>Break-up.</b> A falling nappe stays one sheet only for its break-up length L_b, which grows with its
	/// discharge, not its height (Castillo &amp; Carrillo 2016, eq. 1b). Horeni's L_b ≈ 6 q^0.32 agrees within a factor
	/// of two from a trickle to a mountain river (q 0.05–2 m²/s) and not beyond: the brink's Froude number is the same at
	/// every discharge, so Castillo's length grows as the jet's thickness, q^⅔, and twice as fast as Horeni's fit. A
	/// mountain stream's is a few metres: a 50 m fall is a sheet over its brink and ropes of white water for nine tenths of
	/// its height.
	/// </para>
	/// <para>
	/// <b>Spread and impact.</b> Turbulence spreads each edge by ξ = 2φ√h(√(2H) − 2√h), φ = K_φ·T_u, and the jet
	/// lands B_j = q/√(2gH) + 2ξ thick (Castillo &amp; Carrillo 2016, eq. 1a): a few tenths of a metre per side on a
	/// 50 m fall, never the doubling of its width that a share-per-metre spread gave.
	/// </para>
	/// </remarks>
	public static class FallHydraulics
	{
		public const float Gravity = 9.81f;

		/// <summary>
		/// The jet's turbulence intensity leaving the lip: the measured flat crest's 0.013. At that value Castillo's
		/// break-up length agrees with Horeni's within a factor of two from a trickle to a mountain river (q 0.05–2 m²/s;
		/// past that it grows as q^⅔ and Horeni's as q^0.32, so no one value matches both across a big river too); at 0.03
		/// a small stream broke up within 0.6 m of its lip, a third of Horeni's figure. <see cref="InlandWaterRenderer.LipTurbulence"/> tunes it.
		/// </summary>
		public const float DefaultTurbulence = 0.013f;

		/// <summary>K_φ for a three-dimensional nappe (Castillo &amp; Carrillo 2016).</summary>
		public const float NappeSpreadFactor = 1.24f;

		/// <summary>K, the break-up fit (0.85 for the measured nappes).</summary>
		public const float BreakupFit = 0.85f;

		/// <summary>The brink's depth as a share of the critical depth (Rouse's free overfall).</summary>
		public const float BrinkShare = 0.715f;

		/// <summary>Discharge per metre of lip, m²/s, from the river's discharge and width (or its depth × speed).</summary>
		public static float UnitDischarge(float discharge, float width, float depth, float speed)
		{
			float fromDischarge = width > 0.1f && discharge > 0f ? discharge / width : 0f;
			float fromSection = Mathf.Max(0f, depth) * Mathf.Max(0f, speed);
			return Mathf.Max(1e-4f, fromDischarge > 0f ? fromDischarge : fromSection);
		}

		/// <summary>The critical depth y_c = (q²/g)^⅓, metres.</summary>
		public static float CriticalDepth(float q) => Mathf.Pow(q * q / Gravity, 1f / 3f);

		/// <summary>The water's depth at the brink, metres.</summary>
		public static float BrinkDepth(float q) => BrinkShare * CriticalDepth(q);

		/// <summary>The water's speed leaving the brink, m/s.</summary>
		public static float BrinkSpeed(float q) => q / Mathf.Max(1e-4f, BrinkDepth(q));

		/// <summary>The energy head over the lip, metres: 1.5 y_c, critical flow's specific energy.</summary>
		public static float Head(float q) => 1.5f * CriticalDepth(q);

		/// <summary>φ = K_φ T_u.</summary>
		public static float Phi(float turbulence) => NappeSpreadFactor * Mathf.Max(0f, turbulence);

		/// <summary>Horeni's break-up length, 6 q^0.32 metres: the cross-check.</summary>
		public static float BreakupLengthHoreni(float q) => 6f * Mathf.Pow(Mathf.Max(1e-4f, q), 0.32f);

		/// <summary>
		/// The break-up length, metres (Castillo &amp; Carrillo 2016, eq. 1b): L_b / (B_i F_i²) = K / (φ F_i²)^0.82, with the
		/// issuance speed V_i = √(2gh), thickness B_i = q / V_i and Froude number F_i = V_i / √(g B_i).
		/// </summary>
		public static float BreakupLength(float q, float turbulence = DefaultTurbulence)
		{
			float h = Head(q);
			float vi = Mathf.Sqrt(2f * Gravity * h);
			float bi = q / Mathf.Max(1e-4f, vi);
			float fi2 = vi * vi / (Gravity * Mathf.Max(1e-5f, bi));
			float phi = Mathf.Max(1e-4f, Phi(turbulence));
			return BreakupFit * bi * fi2 / Mathf.Pow(phi * fi2, 0.82f);
		}

		/// <summary>
		/// How far each edge of the jet has spread <paramref name="fallen"/> metres below the lip, metres: ξ = 2φ√h(√(2H) − 2√h),
		/// none until the fall is deeper than twice the head.
		/// </summary>
		public static float Spread(float q, float fallen, float turbulence = DefaultTurbulence)
		{
			float h = Head(q);
			float rootH = Mathf.Sqrt(h);
			return Mathf.Max(0f, 2f * Phi(turbulence) * rootH * (Mathf.Sqrt(2f * Mathf.Max(0f, fallen)) - 2f * rootH));
		}

		/// <summary>
		/// How thick the water's core is <paramref name="fallen"/> metres below the lip, metres: the same water spread along
		/// a faster stream, q / √(V_b² + 2gH) (it is the brink's depth at the lip, and q/√(2gH) far down).
		/// </summary>
		public static float CoreThickness(float q, float fallen)
		{
			float vb = BrinkSpeed(q);
			return q / Mathf.Sqrt(vb * vb + 2f * Gravity * Mathf.Max(0f, fallen));
		}

		/// <summary>How thick the jet is <paramref name="fallen"/> metres below the lip, its turbulent spread included (B_j at the pool).</summary>
		public static float JetThickness(float q, float fallen, float turbulence = DefaultTurbulence)
		{
			return CoreThickness(q, fallen) + 2f * Spread(q, fallen, turbulence);
		}

		/// <summary>The water's speed after falling <paramref name="fallen"/> metres from a brink speed, drag ignored, m/s.</summary>
		public static float FreeSpeed(float brinkSpeed, float fallen) => Mathf.Sqrt(brinkSpeed * brinkSpeed + 2f * Gravity * Mathf.Max(0f, fallen));

		/// <summary>
		/// The speed clusters of broken water settle toward, m/s. A falling raindrop of 5 mm holds at about 9 m/s; the
		/// ropes and clumps a broken jet sheds are larger and fall faster, and drag pulls them back toward this once the
		/// sheet has broken (so a 50 m fall does not land at the 31 m/s of a stone).
		/// </summary>
		public const float BrokenTerminalSpeed = 16f;

		/// <summary>The terminal speed of a spray droplet, m/s (about a 5 mm drop).</summary>
		public const float DropTerminalSpeed = 9f;

		/// <summary>
		/// What pulls on falling water <paramref name="fallen"/> metres below its lip, m/s²: gravity and, once it has fallen past
		/// its <paramref name="breakup"/> length, the air's drag on its broken ropes and clumps (linear in the velocity, so
		/// <see cref="BrokenTerminalSpeed"/> is exactly the speed it settles to). The curtain's strips are traced by it
		/// (InlandWaterRenderer.FallSheet), and <see cref="Throw"/> by the same steps.
		/// </summary>
		public static Vector3 FreeAcceleration(Vector3 velocity, float fallen, float breakup)
		{
			var acceleration = new Vector3(0f, -Gravity, 0f);
			float broken = Mathf.Clamp01((fallen - breakup) / Mathf.Max(1f, 0.5f * breakup));
			if (broken > 0f)
			{
				acceleration -= velocity * (broken * Gravity / BrokenTerminalSpeed);
			}
			return acceleration;
		}

		/// <summary>The time step falling water is traced at, at a speed, seconds: never more than 12 cm of its path a step.</summary>
		public static float FreeStep(float speed) => Mathf.Min(0.05f, 0.12f / Mathf.Max(0.5f, speed));

		/// <summary>
		/// How far out from its lip water of <paramref name="q"/> m²/s lands on a surface <paramref name="drop"/> metres below the
		/// lip, falling free, metres: the middle of a curtain traced as the renderer traces it (leaving the brink at
		/// <see cref="BrinkSpeed"/>, half its brink depth down, its path stepped by <see cref="FreeStep"/> under
		/// <see cref="FreeAcceleration"/>) with no rock in its way. A fall planned to land in water (a cave's spring into its
		/// stream, a shower into a pool) puts its foot this far out from its lip, the way it pours, so the curtain lands where
		/// the water is.
		/// </summary>
		/// <param name="path">When given, filled with the path's points: x how far out, y how far down from the lip, metres.</param>
		public static float Throw(float q, float drop, float turbulence = DefaultTurbulence, System.Collections.Generic.List<Vector2> path = null)
		{
			q = Mathf.Max(1e-3f, q);
			float breakup = BreakupLength(q, turbulence);
			var p = new Vector3(0f, -0.5f * BrinkDepth(q), 0f);
			var v = new Vector3(Mathf.Max(0.3f, BrinkSpeed(q)), 0f, 0f);
			path?.Add(new Vector2(p.x, -p.y));
			for (int guard = 0; guard < 40000; guard++)
			{
				float dt = FreeStep(v.magnitude);
				v += FreeAcceleration(v, -p.y, breakup) * dt;
				Vector3 next = p + v * dt;
				if (-next.y >= drop)
				{
					float t = Mathf.Clamp01((drop + p.y) / Mathf.Max(1e-6f, p.y - next.y));
					float x = Mathf.Lerp(p.x, next.x, t);
					path?.Add(new Vector2(x, drop));
					return x;
				}
				p = next;
				path?.Add(new Vector2(p.x, -p.y));
			}
			return p.x;
		}
	}
}
