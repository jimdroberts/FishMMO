using System;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// What a biome physically needs of its WORLD, beyond air and liquid water: the conditions that
	/// make an ice moon, a volcanic moon or a methane world what it is. A mask; every flag set must hold.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the climate envelope cannot do this.</b> A biome's temperature and humidity envelope
	/// says what climate it likes, and every alien biome's envelope has a twin somewhere on an
	/// Earth-like world: a polar sea is as cold and as wet as the ocean under Europa's shell, and a
	/// hot dry lowland reads the same numbers as Io's sulphur. Measured before these existed, about
	/// 12% of an Earth-like globe went to biomes that cannot exist on it — Subsurface Ocean Vent
	/// took the polar seas, Tidal Fracture and Lava Tube the cold coasts, Sulphur Flats and
	/// Wasteland the hot interiors. What separates them is not the local climate but what the world
	/// is: whether its water is frozen through, whether it is heated from below, what its sky
	/// condenses.
	/// </para>
	/// <para>
	/// <b>Each flag is one predicate on <see cref="BiomeWorldConditions"/></b>, and every one of
	/// those is derived from the body's orbit, size, parent, atmosphere and water — none is
	/// authored on the world. So a moon becomes an ice moon by being where an ice moon is, and its
	/// biomes follow.
	/// </para>
	/// <para>
	/// <b>All, not any.</b> A biome with two flags needs both: Sulphur Flats need a volcanic world
	/// AND one with no water to dissolve the sulphur. <see cref="None"/>, the default, asks nothing,
	/// so every biome authored before this existed behaves exactly as it did. Where nature itself
	/// offers two routes to one ground, the flag is the either-or, stated once in its own predicate
	/// rather than left to two flags that could only be AND-ed: <see cref="MoltenRock"/> is a magma
	/// ocean OR lava lakes.
	/// </para>
	/// <para>
	/// Kept apart from <see cref="BiomeAtmosphereRequirement"/> and
	/// <see cref="BiomeTemplate.RequiresLiquidWater"/>, which stay what they were: those say what a
	/// biome needs of the air and the water, these what it needs of the world under them.
	/// </para>
	/// </remarks>
	[Flags]
	public enum BiomeWorldRequirement : ushort
	{
		None = 0,

		/// <summary>
		/// The world's water is frozen through: <see cref="BiomeWorldConditions.IsIceWorld"/>, with no
		/// liquid water open anywhere (<see cref="BiomeWorldConditions.HasLiquidWater"/> false).
		/// </summary>
		/// <remarks>
		/// Ice needs water to be made of: a dry rock as cold is not an ice world — Europa, not the
		/// Moon. And a cold world with air whose seas are still open between its ice caps is a cold
		/// Earth, whose ice is Glacier and Permanent Ice, not an ice moon's.
		/// </remarks>
		IceWorld = 1 << 0,

		/// <summary>
		/// An ice world worked from below (<see cref="BiomeWorldConditions.IsCryovolcanic"/>, frozen
		/// through as <see cref="IceWorld"/> is): plumes, cryolava, an ocean kept liquid under the shell.
		/// </summary>
		/// <remarks>Europa and Enceladus. Frozen outside and warm inside, which takes both the water and the internal heat.</remarks>
		Cryovolcanic = 1 << 1,

		/// <summary>
		/// The surface is being rebuilt from below (<see cref="BiomeWorldConditions.IsVolcanic"/>):
		/// lava, fresh basalt, sulphur.
		/// </summary>
		/// <remarks>
		/// True of an Earth-sized world from its size alone, so on its own it never keeps a biome off
		/// the home world — it keeps lava off dead ones. A small cold rock has no lava however warm
		/// its sunlight.
		/// </remarks>
		Volcanic = 1 << 2,

		/// <summary>
		/// Flexed by the tides of the world it orbits (<see cref="BiomeWorldConditions.IsTidallyHeated"/>).
		/// </summary>
		/// <remarks>
		/// Only a moon of a massive parent on an eccentric orbit: Io and Europa, not our Moon, and
		/// never a planet. Kept apart from <see cref="Volcanic"/> because an Earth-sized planet is hot
		/// inside from its size and is not being pulled open by anything.
		/// </remarks>
		TidallyHeated = 1 << 3,

		/// <summary>
		/// No liquid water anywhere on the surface (<see cref="BiomeWorldConditions.HasLiquidWater"/> false).
		/// </summary>
		/// <remarks>
		/// The counterpart of <see cref="BiomeTemplate.RequiresLiquidWater"/>: ground that only
		/// exists where nothing flows, dissolves or grows. Lava tubes keep their roofs where no rain
		/// wears them; sulphur lies on the surface only where no water carries it off; a wasteland
		/// with nothing living in it is a world with no water to live in. On a world with oceans the
		/// same climate is a desert.
		/// </remarks>
		NoLiquidWater = 1 << 4,

		/// <summary>
		/// Air whose weather runs on methane, with some of it lying on the ground
		/// (<see cref="BiomeWorldConditions.HasMethaneCycle"/>).
		/// </summary>
		/// <remarks>
		/// Titan. Methane is liquid between about 91 and 112 K at a bar and condenses in a sky of
		/// 72–130 K (<c>AirPhysics.CondensateFor</c>); it needs air above it to stay liquid at all,
		/// and the body's water fraction stands for the lakes of it, as it does in the moisture model.
		/// </remarks>
		MethaneCycle = 1 << 5,

		/// <summary>
		/// Air cold enough to condense methane or nitrogen (<see cref="BiomeWorldConditions.HasCryogenicAir"/>).
		/// </summary>
		/// <remarks>
		/// The photochemistry that makes tholins: sunlight breaking methane and nitrogen in a cold
		/// sky, the products settling as an organic crust. Titan, Pluto, Triton. Needs the air, not
		/// lakes.
		/// </remarks>
		CryogenicAir = 1 << 6,

		/// <summary>
		/// Cold enough for nitrogen to freeze at the surface (<see cref="BiomeWorldConditions.FreezesNitrogen"/>).
		/// </summary>
		/// <remarks>Below about 72 K mean: Triton and Pluto. Nitrogen ice is a solid there the way water ice is here.</remarks>
		FrozenNitrogen = 1 << 7,

		/// <summary>
		/// A runaway greenhouse (<see cref="BiomeWorldConditions.IsRunawayGreenhouse"/>): a surface
		/// past 400 K under a sky whose clouds are acid.
		/// </summary>
		/// <remarks>Venus. Thick air alone is not enough — a thick atmosphere far from its star is Titan, not Venus.</remarks>
		RunawayGreenhouse = 1 << 8,

		/// <summary>
		/// Inside a giant planet's magnetic field (<see cref="BiomeWorldConditions.InGiantMagnetosphere"/>).
		/// </summary>
		/// <remarks>
		/// The radiation belts a giant traps round itself: a moon orbiting one is swept by them, which
		/// is why Europa's surface is bathed in it and our own Moon's is not.
		/// </remarks>
		GiantMagnetosphere = 1 << 9,

		/// <summary>
		/// The surface is rock, not an ice shell: anything but a world frozen through
		/// (<see cref="BiomeWorldConditions.HasRockSurface"/>).
		/// </summary>
		/// <remarks>
		/// Regolith is powdered rock, a rille is a collapsed lava channel, a lava tube a drained one.
		/// An ice moon's crust is water ice to a depth of kilometres, and none of those can form in
		/// it — Europa has no regolith plains, whatever its airlessness says.
		/// </remarks>
		RockSurface = 1 << 10,

		/// <summary>
		/// Some water on the world at all, liquid or frozen (<see cref="BiomeWorldConditions.HasWater"/>).
		/// </summary>
		/// <remarks>
		/// What ice is made of. A glacier on a world with none is the same mistake as an ice moon
		/// built from a dry rock: Venus's summits are cold for Venus and still bare stone, and the
		/// Moon's poles are cold enough for anything and hold no ice sheet. Mars, with a trace,
		/// keeps its caps.
		/// </remarks>
		SurfaceWater = 1 << 11,

		/// <summary>
		/// Molten rock open at the surface (<see cref="BiomeWorldConditions.HasMoltenRock"/>): the world
		/// is hot enough from its starlight to be a magma ocean, OR heated from below hard enough to
		/// keep lava standing in its basins — exactly where <see cref="SurfaceLiquids"/> puts lava.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Either route, because rock melts either way.</b> Above the solidus
		/// (<see cref="SurfaceLiquids.SolidusKelvin"/>, 1300 K) the low ground is molten from sunlight
		/// alone, as on the hot super-Earths hugging their stars. Below it, melt reaches the surface
		/// only where something heats the rock from inside past <see cref="SurfaceLiquids.LavaLakeHeat"/>
		/// and there is no thick air to have set it long ago and no ice shell to quench it: Io's paterae.
		/// One flag with OR semantics, since the requirements are AND-ed and no pair of them could say it.
		/// </para>
		/// <para>
		/// <b>Not merely <see cref="Volcanic"/>, and not merely hot.</b> Volcanic is true of any
		/// Earth-sized world from its size alone and a warm airless rock is not molten: tested on either,
		/// Molten Surface took the regolith of a 324 K airless planet and the plains of a runaway greenhouse,
		/// which is 434 K of placid, long-set basalt. Molten rock needs the heat to melt rock.
		/// </para>
		/// <para>
		/// Water first, as in <see cref="SurfaceLiquids.Decide"/>: a world with open seas quenches its
		/// lava into pillow basalt and is never this, however hot inside.
		/// </para>
		/// </remarks>
		MoltenRock = 1 << 12,
	}
}
