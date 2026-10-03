using UnityEngine;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

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
	/// <para>
	/// <b>On the climate field's own absolute scale (2026-10-02).</b> The temperature here used to be
	/// the orbit offsets' "how much colder than home", tested against −0.75 … +0.85, while the field
	/// that paints the same world reads absolute temperature, 0 at freezing. The two disagreed by a
	/// whole world: Galris, 210 K, read −0.53 here — liquid seas and an ice world at once — while
	/// every point of its field read −1. Now <see cref="MeanTemperature"/> IS the field's mean
	/// (<see cref="ClimateModel.MeanSurfaceKelvin(SolarSystemProfile, WorldBody)"/>), and the water
	/// and ice tests ask the field's own question: is the warmest ground it can paint above freezing?
	/// See <see cref="WarmestTemperature"/>.
	/// </para>
	/// </remarks>
	public struct BiomeWorldConditions
	{
		public AtmosphereKind Atmosphere;

		/// <summary>
		/// Mean surface temperature on the climate scale, UNCLAMPED: 0 where water freezes, about 33 K
		/// a unit (<see cref="ClimateModel.ToScaleUnclamped"/>). The same number as
		/// <see cref="PlanetClimateField.MeanTemperature"/>.
		/// </summary>
		/// <remarks>
		/// Unclamped because the scale's ±1 stops at 240 K and 306 K, and the questions asked of it —
		/// boiled off? frozen through? — lie well outside that: Venus sits near +5, Europa near −5.
		/// </remarks>
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

		/// <summary>
		/// The share of <see cref="InternalHeat"/> that comes from tides, 0 … 1: how hard the world it
		/// orbits flexes it.
		/// </summary>
		/// <remarks>
		/// Its own number because the two sources build different ground. An Earth-sized planet is
		/// hot inside from its size and has volcanoes; only a moon being kneaded by a giant has its
		/// crust pulled open and shut every orbit. <see cref="InternalHeat"/> is the larger of the
		/// two, so it cannot tell them apart.
		/// </remarks>
		public float TidalHeat;

		/// <summary>
		/// What the world's sky condenses, from its mean surface temperature
		/// (<see cref="AirPhysics.CondensateFor"/>) — the same answer the weather and the moisture
		/// model read from <see cref="PlanetAir"/>.
		/// </summary>
		/// <remarks>
		/// On an airless body there is no sky, but the band still says how cold the ground is: below
		/// 72 K nitrogen is a solid. Zero, the default, is water, so conditions built by hand without
		/// it read as an ordinary world rather than a cryogenic one.
		/// </remarks>
		public Condensate Condensate;

		/// <summary>
		/// The magnetic field of the giant planet this body orbits, against the home world's at 1; 0
		/// for anything that does not orbit a giant.
		/// </summary>
		public float GiantMagneticField;

		/// <summary>The home world: air, water, temperate. What every scene assumed before bodies existed.</summary>
		public static BiomeWorldConditions Earthlike => new BiomeWorldConditions
		{
			Atmosphere = AtmosphereKind.Standard,
			// 288 K, Earth's mean and PlanetAir.Earthlike's.
			MeanTemperature = (float)ClimateModel.ToScaleUnclamped(EarthlikeKelvin),
			Water = 0.7f,
			InternalHeat = ClimateModel.EarthlikeInternalHeat,
			TidalHeat = 0f,
			Condensate = Condensate.Water,
			GiantMagneticField = 0f,
		};

		/// <summary>Earth's mean surface temperature, K: what <see cref="Earthlike"/> stands at.</summary>
		public const double EarthlikeKelvin = 288.0;

		/// <summary>
		/// The warmest reading the climate field can give anywhere on this world, on the same scale:
		/// the sub-solar sea level with the regional wobble at its warmest.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>An exact bound, not an estimate.</b> The field's temperature at any point is its
		/// sub-solar figure (the mean plus <see cref="ClimateModel.SubSolarExcess"/>), less a latitude
		/// term that is never positive, less a lapse that is never negative, plus a regional term that
		/// never exceeds <see cref="PlanetClimateField.RegionalVariation"/>. So nothing the globe or a
		/// scene paints is ever warmer than this, and when this is below freezing every point is.
		/// </para>
		/// <para>
		/// Derived from <see cref="MeanTemperature"/> by constants alone, so the conditions never have
		/// to build a field to know it — <see cref="PlanetClimateField.For"/> builds these conditions
		/// itself, and asking the field back would go round in a circle.
		/// </para>
		/// </remarks>
		public float WarmestTemperature => MeanTemperature + WarmestExcess;

		/// <summary>How far the field's warmest point stands above its mean, in scale units.</summary>
		public static readonly float WarmestExcess =
			(float)(ClimateModel.SubSolarExcess / ClimateModel.KelvinPerUnit) + PlanetClimateField.RegionalVariation;

		/// <summary>Where water freezes on the climate scale: its zero, by definition.</summary>
		public const float FreezingTemperature = 0f;

		/// <summary>
		/// Water that is liquid somewhere on the surface: some water, some air to hold it, ground
		/// somewhere above freezing, and a world not so hot its oceans boil away.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Somewhere, not on average.</b> The test is the field's warmest ground
		/// (<see cref="WarmestTemperature"/>), not its mean, because open water at the warm belt
		/// with ice to the tropics is a real climate — the "waterbelt" state that climate models keep
		/// stable short of a full snowball (Abbot, Voigt and Koll 2011) — and a world showing it has a
		/// sea and the coasts that go with one. A mean of −1 °C is a world with a belt of open ocean
		/// round its equator, not an ice moon.
		/// </para>
		/// <para>
		/// <b>Boiled off above the boiling point of its own air</b> (<see cref="AirPhysics.BoilingKelvin"/>):
		/// about 95 °C under an atmosphere like ours, 53 °C under a thin one; and never past
		/// <see cref="AirPhysics.RunawayGreenhouseKelvin"/>, where water condenses nowhere and the
		/// clouds are acid. So a world above the melting point of rock is never a sea, however much
		/// water it was made with (<see cref="SurfaceLiquids"/> turns its low ground to magma).
		/// </para>
		/// </remarks>
		public bool HasLiquidWater => Water > 0.02f
			&& Atmosphere != AtmosphereKind.None
			&& HoldsLiquidWater(MeanTemperature, Atmosphere);

		/// <summary>
		/// The temperature half of <see cref="HasLiquidWater"/>: whether a world with this mean
		/// (climate scale, unclamped) under this air keeps water liquid somewhere.
		/// </summary>
		/// <remarks>For tools that hold a temperature without a body: the orrery's goldilocks band is this test, ring by ring.</remarks>
		public static bool HoldsLiquidWater(float meanTemperature, AtmosphereKind atmosphere)
		{
			return meanTemperature + WarmestExcess > FreezingTemperature
				&& ClimateModel.ToKelvin(meanTemperature) < LiquidCeilingKelvin(atmosphere);
		}

		/// <summary>The mean surface temperature above which a world's water cannot stay liquid, K.</summary>
		public static float LiquidCeilingKelvin(AtmosphereKind atmosphere)
		{
			switch (atmosphere)
			{
				case AtmosphereKind.None: return BoilingNone;
				case AtmosphereKind.Thin: return BoilingThin;
				case AtmosphereKind.Thick: return BoilingThick;
				default: return BoilingStandard;
			}
		}

		// Worked out once: the resolver asks HasLiquidWater for every biome at every point.
		private static readonly float BoilingNone = Ceiling(AtmosphereKind.None);
		private static readonly float BoilingThin = Ceiling(AtmosphereKind.Thin);
		private static readonly float BoilingStandard = Ceiling(AtmosphereKind.Standard);
		private static readonly float BoilingThick = Ceiling(AtmosphereKind.Thick);

		private static float Ceiling(AtmosphereKind atmosphere)
		{
			float pressure = AirPhysics.StandardPressure * AtmosphereModel.Density(atmosphere);
			return Mathf.Min(AirPhysics.BoilingKelvin(pressure, Condensate.Water), (float)AirPhysics.RunawayGreenhouseKelvin);
		}

		/// <summary>The surface is being rebuilt from below: lava, fresh basalt, sulphur.</summary>
		public bool IsVolcanic => InternalHeat >= ClimateModel.VolcanicThreshold;

		/// <summary>
		/// Water that is frozen solid across the whole world: an ice moon.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Ice needs water to be made of, so a dry rock at the same temperature is not an ice moon
		/// however cold it gets — which is the difference between Europa and our own Moon.
		/// </para>
		/// <para>
		/// <b>Frozen across the whole world means exactly that:</b> the field's warmest ground is at or
		/// below freezing, so every point of it is (<see cref="WarmestTemperature"/>). That makes this
		/// the exact complement of the temperature half of <see cref="HasLiquidWater"/>, and the
		/// planet bake's per-point ice (<see cref="IsIceAt"/>) covers the whole globe whenever this
		/// holds. It used to be a mean 11 K below home, which also caught a cold world whose seas were
		/// still open.
		/// </para>
		/// </remarks>
		public bool IsIceWorld => Water > IceWorldWater && WarmestTemperature <= FreezingTemperature;

		/// <summary>The least water a world needs for its frozen surface to be a shell of ice rather than frost on rock.</summary>
		public const float IceWorldWater = 0.05f;

		/// <summary>
		/// Whether ground at this temperature on a world with this much water is ice: the planet
		/// bake's per-point test, of which <see cref="IsIceWorld"/> is the whole-world case.
		/// </summary>
		/// <param name="water">The world's <see cref="Water"/>.</param>
		/// <param name="temperature">The field's temperature at the point (climate scale).</param>
		public static bool IsIceAt(float water, float temperature) => water > IceWorldWater && temperature <= FreezingTemperature;

		/// <summary>
		/// Ice being worked from beneath: fractured plains, plumes, a liquid ocean under the shell.
		/// </summary>
		/// <remarks>
		/// Europa and Enceladus. Frozen at the surface and warm inside, which no single temperature
		/// can express — it takes both numbers, which is the whole reason internal heat is its own.
		/// </remarks>
		public bool IsCryovolcanic => IsIceWorld && InternalHeat >= 0.3f;

		/// <summary>
		/// Tidal flexing strong enough to break the crust open: Io and Europa, never our Moon.
		/// </summary>
		/// <remarks>
		/// The same 0.3 that makes an ice world cryovolcanic: measured, Europa's tides come to 0.44 on
		/// this scale and keep an ocean liquid under its shell, Io's to 0.80, and our Moon's — its
		/// parent a thousandth of Jupiter's mass, squared — to 0.01.
		/// </remarks>
		public bool IsTidallyHeated => TidalHeat >= TidalHeatingThreshold;

		/// <summary>The tidal share of internal heat at which a crust is pulled open.</summary>
		public const float TidalHeatingThreshold = 0.3f;

		/// <summary>A sky that condenses methane, air to hold it liquid, and some of it on the ground. Titan.</summary>
		public bool HasMethaneCycle => Atmosphere != AtmosphereKind.None && Condensate == Condensate.Methane && Water > 0.02f;

		/// <summary>A sky cold enough to condense methane or nitrogen: the photochemistry that makes tholins.</summary>
		public bool HasCryogenicAir => Atmosphere != AtmosphereKind.None
			&& (Condensate == Condensate.Methane || Condensate == Condensate.Nitrogen);

		/// <summary>Cold enough that nitrogen is a solid on the ground: Triton, Pluto.</summary>
		public bool FreezesNitrogen => Condensate == Condensate.Nitrogen;

		/// <summary>A runaway greenhouse: past 400 K, where water condenses nowhere and the clouds are acid. Venus.</summary>
		public bool IsRunawayGreenhouse => Condensate == Condensate.SulphuricAcid;

		/// <summary>Orbiting a giant with a field strong enough to trap radiation belts round it.</summary>
		/// <remarks>Half the home world's field. Jupiter's magnetic moment is some twenty thousand times Earth's, so a giant with any real field qualifies and one whose core has frozen does not.</remarks>
		public bool InGiantMagnetosphere => GiantMagneticField >= 0.5f;

		/// <summary>
		/// The surface is rock rather than an ice shell: anything but a world whose water is frozen
		/// through.
		/// </summary>
		public bool HasRockSurface => !IsFrozenThrough;

		/// <summary>Water on the world at all, liquid or frozen: what ice can be made of.</summary>
		/// <remarks>A trace counts — Mars, at a couple of percent, has polar caps.</remarks>
		public bool HasWater => Water > 0.01f;

		/// <summary>
		/// An ice world with nothing open on it: cold and wet, and no liquid water anywhere.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A cold Earth — ice caps with open seas between them — is never an ice moon: its ice belongs
		/// to the Earth biomes (Glacier, Permanent Ice), and an ice moon's plumes and vents need a
		/// shell with nothing open on top of it.
		/// </para>
		/// <para>
		/// Since both tests read the field's warmest ground (2026-10-02) <see cref="IsIceWorld"/> can
		/// no longer hold beside liquid water, so this is the same as <see cref="IsIceWorld"/>. It is
		/// kept as the name the requirements read, so the rule "frozen THROUGH" stays stated where
		/// it is used rather than resting on two thresholds happening to meet.
		/// </para>
		/// </remarks>
		public bool IsFrozenThrough => IsIceWorld && !HasLiquidWater;

		/// <summary>
		/// Molten rock open at the surface: a magma ocean, or lava lakes — whatever
		/// <see cref="SurfaceLiquids.Decide"/> calls lava.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The lava code's own decision, asked of this world's own mean (the field's absolute scale,
		/// back in kelvin), so a biome of molten ground stands exactly where the scene floods its low
		/// ground with lava and the globe glows: at or past the solidus
		/// (<see cref="SurfaceLiquids.SolidusKelvin"/>), or thin-aired, unfrozen and heated from within
		/// past <see cref="SurfaceLiquids.LavaLakeHeat"/> (<see cref="SurfaceLiquids.HasLavaLakes"/>).
		/// Never with open water, which quenches it.
		/// </para>
		/// <para>
		/// Kept apart from <see cref="IsVolcanic"/>, which an Earth-sized world is from its size alone:
		/// Venus is volcanic by that test and its surface is long-set basalt under thick air.
		/// </para>
		/// </remarks>
		public bool HasMoltenRock => SurfaceLiquids.Decide(this, ClimateModel.ToKelvin(MeanTemperature)) == SurfaceLiquid.Lava;

		/// <summary>True when this world meets every one of <paramref name="required"/>.</summary>
		public bool Meets(BiomeWorldRequirement required)
		{
			if (required == BiomeWorldRequirement.None)
			{
				return true;
			}
			// Frozen THROUGH for the two ice flags (see IsFrozenThrough): a cold Earth is not an ice moon.
			return (!Has(required, BiomeWorldRequirement.IceWorld) || IsFrozenThrough)
				&& (!Has(required, BiomeWorldRequirement.Cryovolcanic) || (IsFrozenThrough && IsCryovolcanic))
				&& (!Has(required, BiomeWorldRequirement.Volcanic) || IsVolcanic)
				&& (!Has(required, BiomeWorldRequirement.TidallyHeated) || IsTidallyHeated)
				&& (!Has(required, BiomeWorldRequirement.NoLiquidWater) || !HasLiquidWater)
				&& (!Has(required, BiomeWorldRequirement.MethaneCycle) || HasMethaneCycle)
				&& (!Has(required, BiomeWorldRequirement.CryogenicAir) || HasCryogenicAir)
				&& (!Has(required, BiomeWorldRequirement.FrozenNitrogen) || FreezesNitrogen)
				&& (!Has(required, BiomeWorldRequirement.RunawayGreenhouse) || IsRunawayGreenhouse)
				&& (!Has(required, BiomeWorldRequirement.GiantMagnetosphere) || InGiantMagnetosphere)
				&& (!Has(required, BiomeWorldRequirement.RockSurface) || HasRockSurface)
				&& (!Has(required, BiomeWorldRequirement.SurfaceWater) || HasWater)
				// Either route to molten ground (see BiomeWorldRequirement.MoltenRock): the OR lives in
				// the predicate, so the flags themselves stay AND-ed.
				&& (!Has(required, BiomeWorldRequirement.MoltenRock) || HasMoltenRock);
		}

		private static bool Has(BiomeWorldRequirement mask, BiomeWorldRequirement flag) => (mask & flag) != 0;

		/// <summary>Whether this world could carry that biome at all, climate aside.</summary>
		/// <remarks>
		/// Three questions, all physical: the air it needs, the liquid water it needs, and what it
		/// needs of the world itself (<see cref="BiomeTemplate.Requires"/>). The last is what keeps
		/// an ice moon's vents out of an Earth-like polar sea that happens to read the same climate.
		/// </remarks>
		public bool Allows(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return false;
			}
			return Allows(biome.Atmosphere, biome.RequiresLiquidWater, biome.Requires);
		}

		/// <summary>The same test from a biome's three requirements, for tools that hold them without a template.</summary>
		public bool Allows(BiomeAtmosphereRequirement atmosphere, bool requiresLiquidWater, BiomeWorldRequirement requires)
		{
			if ((atmosphere & Mask(Atmosphere)) == 0)
			{
				return false;
			}
			if (requiresLiquidWater && !HasLiquidWater)
			{
				return false;
			}
			return Meets(requires);
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
			// The MEAN, not this moment's: a world does not stop supporting oceans in its winter. The
			// field's own absolute figure, so the conditions and the ground they are tested against
			// are on one scale (see the type's remarks).
			float temperature = (float)ClimateModel.ToScaleUnclamped(ClimateModel.MeanSurfaceKelvin(system, body));
			// A giant's moon sits in its field; the field's strength is the giant's own.
			float giantField = body.Parent is WorldBody parent && parent.Kind == WorldBodyKind.GasGiant
				? Mathf.Max(0f, parent.MagneticField)
				: 0f;
			return new BiomeWorldConditions
			{
				Atmosphere = body.Atmosphere,
				MeanTemperature = temperature,
				Water = body.Water,
				InternalHeat = ClimateModel.InternalHeat(system, body),
				TidalHeat = ClimateModel.TidalHeat(system, body),
				// The orbit-mean air the weather and the moisture model already read, so the sky a
				// biome is chosen under is the sky that rains on it.
				Condensate = PlanetAir.For(system, body).Condensate,
				GiantMagneticField = giantField,
			};
		}
	}
}
