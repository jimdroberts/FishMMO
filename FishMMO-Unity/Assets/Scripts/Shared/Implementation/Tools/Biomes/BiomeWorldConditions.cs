using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// What a world physically offers, as the biome resolver needs it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The counterpart to a biome's requirements. A world states its conditions once — what air it
	/// has, whether water is liquid on its surface — and every biome states its needs once; the
	/// resolver matches them. Neither side has to know the other exists, so adding a biome does not
	/// mean revisiting every world, and adding a world does not mean listing every biome.
	/// </para>
	/// <para>
	/// <b>Liquid water is derived, not authored.</b> A world has water if it has any at all, has air
	/// to keep it from boiling away, and is warm enough not to have frozen solid. Authoring it as a
	/// flag would let a designer tick "has oceans" on an airless rock at four AU and get a coastline.
	/// </para>
	/// </remarks>
	public struct BiomeWorldConditions
	{
		public AtmosphereKind Atmosphere;

		/// <summary>Mean surface temperature, -1 frozen … +1 scorching.</summary>
		public float MeanTemperature;

		/// <summary>How much of the surface is water at all, frozen or not.</summary>
		public float Water;

		/// <summary>
		/// Heat the world makes for itself, 0 dead to 1 molten.
		/// </summary>
		/// <remarks>
		/// Kept apart from <see cref="MeanTemperature"/> because the two are genuinely independent:
		/// Io's surface is −140 °C and it is the most volcanic body in the solar system, while
		/// Venus is 460 °C and geologically placid. Without this, a volcanic biome could only be
		/// chosen by a world being hot in the sunlight — so Molten Surface, Sulphur Flats, Lava
		/// Tube and Tidal Fracture were unreachable however the planet was built.
		/// </remarks>
		public float InternalHeat;

		/// <summary>The home world: air, water, temperate. What every scene assumed before bodies existed.</summary>
		public static BiomeWorldConditions Earthlike => new BiomeWorldConditions
		{
			Atmosphere = AtmosphereKind.Standard,
			MeanTemperature = 0.35f,
			Water = 0.7f,
			InternalHeat = ClimateModel.EarthlikeInternalHeat,
		};

		/// <summary>
		/// Water that is liquid somewhere on the surface: some water, some air to hold it, and a
		/// temperature between frozen through and boiled off.
		/// </summary>
		public bool HasLiquidWater => Water > 0.02f
			&& Atmosphere != AtmosphereKind.None
			&& MeanTemperature > -0.75f
			&& MeanTemperature < 0.85f;

		/// <summary>The surface is being rebuilt from below: lava, fresh basalt, sulphur.</summary>
		public bool IsVolcanic => InternalHeat >= ClimateModel.VolcanicThreshold;

		/// <summary>
		/// Water that is frozen solid across the whole world: an ice moon.
		/// </summary>
		/// <remarks>
		/// Ice needs water to be made of, so a dry rock at the same temperature is not an ice moon
		/// however cold it gets — which is the difference between Europa and our own Moon.
		/// </remarks>
		public bool IsIceWorld => Water > 0.05f && MeanTemperature <= -0.35f;

		/// <summary>
		/// Ice being worked from beneath: fractured plains, plumes, a liquid ocean under the shell.
		/// </summary>
		/// <remarks>
		/// Europa and Enceladus. Frozen at the surface and warm inside, which no single temperature
		/// can express — it takes both numbers, which is the whole reason internal heat is its own.
		/// </remarks>
		public bool IsCryovolcanic => IsIceWorld && InternalHeat >= 0.3f;

		/// <summary>Whether this world could carry that biome at all, climate aside.</summary>
		public bool Allows(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return false;
			}
			if ((biome.Atmosphere & Mask(Atmosphere)) == 0)
			{
				return false;
			}
			return !biome.RequiresLiquidWater || HasLiquidWater;
		}

		private static BiomeAtmosphereRequirement Mask(AtmosphereKind kind)
		{
			switch (kind)
			{
				case AtmosphereKind.None: return BiomeAtmosphereRequirement.Airless;
				case AtmosphereKind.Thin: return BiomeAtmosphereRequirement.Thin;
				case AtmosphereKind.Thick: return BiomeAtmosphereRequirement.Thick;
				default: return BiomeAtmosphereRequirement.Standard;
			}
		}

		/// <summary>
		/// The conditions on a body, worked out from where it is rather than from anything authored
		/// about its climate.
		/// </summary>
		public static BiomeWorldConditions For(SolarSystemProfile system, WorldBody body)
		{
			if (body == null)
			{
				return Earthlike;
			}
			// The MEAN, not this moment's: a world does not stop supporting oceans in its winter.
			CelestialMath.MeanClimateOffsets(system, body, out float temperature, out _);
			return new BiomeWorldConditions
			{
				Atmosphere = body.Atmosphere,
				MeanTemperature = temperature,
				Water = body.Water,
				InternalHeat = ClimateModel.InternalHeat(system, body),
			};
		}
	}
}
