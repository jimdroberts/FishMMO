using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// What standing on a world is like physically: how hard it pulls, and how thick the air is that
	/// anything falling has to push through.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why weather needs it.</b> A raindrop, a flake or a hailstone is not dropped — it falls at
	/// the speed where the air's drag on it balances its weight, and both halves of that belong to
	/// the world. Rain and snow fell at our own planet's speeds on every world, so snow on a thin-aired
	/// moon drifted exactly as it does here, and the one substance that tried to account for thin air
	/// (nitrogen snow) had to bake the air into the snow.
	/// </para>
	/// </remarks>
	public static class SurfacePhysics
	{
		/// <summary>Standard gravity at our own sea level, m/s².</summary>
		public const float EarthGravity = 9.80665f;

		/// <summary>Our own air at sea level, kg/m³ (the standard atmosphere).</summary>
		public const float EarthAirDensity = 1.225f;

		/// <summary>Surface gravity, m/s².</summary>
		/// <remarks>
		/// A body carries a radius and nothing else of its size, so it is taken to be as dense as our
		/// own world — the convention <see cref="PlanetTides.MassInEarths"/> already uses. Mass then
		/// goes as the radius cubed and gravity, mass over radius squared, as the radius: a world half
		/// our radius pulls half as hard. That over-weights a small, light body (our Moon comes out at
		/// 0.27 g against a true 0.17), in the same direction and for the same reason as the tides.
		/// </remarks>
		public static float Gravity(CelestialBody body)
		{
			float radiusKm = body != null && body.SkyRadiusKm > 0.01f ? body.SkyRadiusKm : PlanetSurface.EarthRadiusKm;
			return EarthGravity * radiusKm / PlanetSurface.EarthRadiusKm;
		}

		/// <summary>Air density at the surface, kg/m³: ours, scaled by how much air the world has.</summary>
		public static float AirDensity(WorldBody body)
		{
			return EarthAirDensity * AtmosphereModel.Density(body != null ? body.Atmosphere : AtmosphereKind.Standard);
		}

		/// <summary>
		/// How many times faster than on our own world something reaches the ground here.
		/// </summary>
		/// <param name="gravity">The world's gravity, m/s².</param>
		/// <param name="airDensity">Its air at the surface, kg/m³.</param>
		/// <param name="fine">
		/// True for grains small enough that the air's stickiness holds them up as much as its bulk:
		/// ash and blown grit. False for drops, stones and flakes.
		/// </param>
		/// <remarks>
		/// <para>
		/// A body falls at the speed where drag balances its weight. For a drop, a hailstone or a
		/// flake the drag is the air's inertia, <c>½ρv²AC</c>, so <c>v ∝ √(g/ρ)</c>: a quarter of the
		/// gravity halves it, and so does four times the air.
		/// </para>
		/// <para>
		/// A fine grain sits between that and Stokes' law, where the drag is the air's viscosity and
		/// the speed goes as <c>g</c> and does not care how dense the air is. A drag coefficient
		/// falling as the square root of the Reynolds number, which is where ash and sand grains
		/// sit, gives <c>v ∝ g^⅔ ρ^−⅓</c>.
		/// </para>
		/// </remarks>
		public static float TerminalSpeedScale(float gravity, float airDensity, bool fine)
		{
			float g = Mathf.Max(0f, gravity) / EarthGravity;
			float rho = Mathf.Max(1e-3f, airDensity / EarthAirDensity);
			return fine ? Mathf.Pow(g, 2f / 3f) * Mathf.Pow(rho, -1f / 3f) : Mathf.Sqrt(g / rho);
		}
	}
}
