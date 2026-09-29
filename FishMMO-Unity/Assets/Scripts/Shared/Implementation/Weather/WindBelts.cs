using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// How a world's spin sorts its winds into belts: the width of one overturning cell, in degrees
	/// of latitude, and which way round everything turns.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Warm air rises at the equator and heads for the poles, and the spinning ground turns it
	/// aside until it can go no further — the edge of the Hadley cell, where it sinks again as the
	/// trade-wind high. How far it gets is the Held–Hou result: the square root of the air's weight
	/// times its depth, over the speed the ground moves at the equator. The depth of an atmosphere
	/// is its scale height <c>RT/g</c>, so gravity cancels and what is left is the air's warmth
	/// against the world's spin: our own air at 288 K on a day of twenty-four hours and a radius of
	/// 6,371 km reaches thirty degrees.
	/// </para>
	/// <para>
	/// Past the Hadley cell the eddies take over and the belts alternate — westerlies, then
	/// easterlies — each about as wide as the first. A world that turns faster, or is smaller, packs
	/// more of them between equator and pole, as Jupiter's bands do; one that turns slowly has a
	/// single cell from equator to pole, as Venus and Titan have.
	/// </para>
	/// </remarks>
	public readonly struct WindBelts
	{
		/// <summary>Degrees of latitude one cell spans.</summary>
		public readonly float CellDegrees;
		/// <summary>+1 for a world that turns the usual way, −1 for one that turns backwards.</summary>
		public readonly float Handedness;

		public WindBelts(float cellDegrees, float handedness)
		{
			CellDegrees = Mathf.Clamp(cellDegrees, 1f, 90f);
			Handedness = handedness < 0f ? -1f : 1f;
		}

		/// <summary>Our own belts: thirty-degree cells.</summary>
		public static WindBelts Earthlike => new WindBelts(30f, 1f);

		/// <summary>The speed our own equator moves at, m/s: what the thirty degrees is measured against.</summary>
		private const float EarthEquatorSpeed = 7.2921e-5f * 6.371e6f;

		/// <summary>The belts a world's air makes.</summary>
		public static WindBelts For(in PlanetAir air)
		{
			if (!air.HasAir)
			{
				return Earthlike;
			}
			float equator = Mathf.Max(1e-3f, Mathf.Abs(air.RotationRate) * air.RadiusMetres);
			float warmth = Mathf.Sqrt(Mathf.Max(20f, air.MeanSurfaceKelvin) * air.GasConstant / (288f * 287.05f));
			// Never narrower than eight degrees: past that the eddies set the spacing, not the spin.
			float cell = Mathf.Clamp(30f * warmth * EarthEquatorSpeed / equator, 8f, 90f);
			return new WindBelts(cell, air.Retrograde ? -1f : 1f);
		}

		/// <summary>A body's belts.</summary>
		public static WindBelts For(SolarSystemProfile system, WorldBody body) => For(PlanetAir.For(system, body));

		/// <summary>The home world's belts, for anything with no body of its own to ask about.</summary>
		public static WindBelts Home
		{
			get
			{
				SolarSystemProfile system = SolarSystemProfile.Active;
				return system != null && system.HomeWorld != null ? For(system, system.HomeWorld) : Earthlike;
			}
		}
	}
}
