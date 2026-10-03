using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Biomes
{
	/// <summary>What stands in a world's low ground: nothing, a sea, or molten rock.</summary>
	public enum SurfaceLiquid
	{
		/// <summary>Dry ground all the way down.</summary>
		None = 0,

		/// <summary>Liquid water: an ocean at the body's sea level.</summary>
		Water = 1,

		/// <summary>Molten rock open to the sky: a magma ocean, or lava lakes in the lowest basins.</summary>
		Lava = 2,
	}

	/// <summary>
	/// Decides what liquid, if any, a world's surface holds, and at what height — from the body's own
	/// physics, never from a flag on it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Water first.</b> A world with liquid water on it is not a lava world however hot its
	/// interior: water quenches lava into pillow basalt, and the basins it would pool in are full of
	/// sea. Earth makes as much internal heat as any world in this model and has no lava seas. The
	/// order is safe at the hot end because liquid water never holds above
	/// <see cref="Weather.AirPhysics.RunawayGreenhouseKelvin"/>, far below the solidus: a magma-ocean
	/// world is never turned back into a sea, whatever water it was made with.
	/// </para>
	/// <para>
	/// <b>Then two ways for rock to stand molten at the surface, and they are different places.</b>
	/// A <i>magma ocean</i> is a world whose surface is above the melting point of rock from its
	/// starlight alone (the hot super-Earths hugging their stars, 55 Cancri e, CoRoT-7b); the
	/// lowlands are molten the way an ocean world's are wet, so the liquid stands at the body's own
	/// datum, as a sea would. <i>Lava lakes</i> are Io: a frozen surface over an interior kneaded
	/// molten, where melt reaches the surface only in the deepest calderas. Flooding that world to
	/// its datum would put half of Io under lava, so the lakes stand just above its lowest ground.
	/// </para>
	/// <para>
	/// <b>The globe is the reference.</b> The lake rule is the planet bake's own
	/// (<c>PlanetSurfaceBaker.Shade</c>): an airless or thin-aired world that is not an ice world
	/// and makes at least <see cref="LavaLakeHeat"/> of internal heat draws molten ground glowing
	/// in its lowest places, fading out over the bottom 14% of its relief. A scene cut from that
	/// globe must find lava exactly where the globe shows it, so the lake level is the middle of
	/// that fade, measured from the same floor.
	/// </para>
	/// </remarks>
	public static class SurfaceLiquids
	{
		/// <summary>
		/// Where a world's surface is molten from its starlight alone: a magma ocean, in kelvin.
		/// </summary>
		/// <remarks>
		/// Basalt starts to melt at about 1000–1100 °C and is wholly liquid by about 1200 °C. The
		/// solidus, 1300 K, is the honest threshold for "the low ground is molten": below it a crust
		/// holds; above it the crust cannot exist except as rafts on the melt.
		/// </remarks>
		public const double SolidusKelvin = 1300.0;

		/// <summary>
		/// Internal heat at or above which an airless or thin-aired world keeps lava standing in its basins.
		/// </summary>
		/// <remarks>Also the planet bake's threshold for drawing molten ground — it reads this constant, so the globe and the scene cannot disagree. Io computes 0.80.</remarks>
		public const float LavaLakeHeat = 0.8f;

		/// <summary>
		/// The height of a lava lake's surface, as a share of the relief above the world's floor.
		/// </summary>
		/// <remarks>
		/// The bake fades its molten glow from full at the floor to none 14% of the way up the
		/// relief; half way through that fade is where the globe reads as half lava, half rock, and
		/// that is the shoreline. On a bell-shaped height field it is a few percent of the surface —
		/// crater and caldera floors, not plains — which is what Io looks like.
		/// </remarks>
		public const float LavaLakeLevel = 0.07f;

		/// <summary>
		/// What a world's surface holds, given what it offers and how warm its surface is in kelvin.
		/// </summary>
		/// <param name="world">The world's conditions (<see cref="BiomeWorldConditions.For"/>).</param>
		/// <param name="meanSurfaceKelvin">Its globe-mean surface temperature, UNCLAMPED: the climate
		/// scale stops at +1 (about 306 K), a thousand kelvin short of melting rock.</param>
		public static SurfaceLiquid Decide(in BiomeWorldConditions world, double meanSurfaceKelvin)
		{
			if (world.HasLiquidWater)
			{
				return SurfaceLiquid.Water;
			}
			if (meanSurfaceKelvin >= SolidusKelvin || HasLavaLakes(world))
			{
				return SurfaceLiquid.Lava;
			}
			return SurfaceLiquid.None;
		}

		/// <summary>
		/// True for an Io: thin or no air, not frozen over, and heated from within past
		/// <see cref="LavaLakeHeat"/>.
		/// </summary>
		/// <remarks>
		/// Air matters because the bake only draws lava on airless and thin-aired worlds: a
		/// thick-aired volcanic world is Venus, resurfaced by flood basalts that have long since set,
		/// with no lakes standing open. Ice matters because a frozen shell over a warm interior is
		/// Europa — cryovolcanism, water welling up, not rock.
		/// </remarks>
		public static bool HasLavaLakes(in BiomeWorldConditions world)
		{
			bool thinAir = world.Atmosphere == AtmosphereKind.None || world.Atmosphere == AtmosphereKind.Thin;
			return thinAir && !world.IsIceWorld && world.InternalHeat >= LavaLakeHeat;
		}

		/// <summary>What a body's surface holds.</summary>
		public static SurfaceLiquid For(SolarSystemProfile system, WorldBody body) => For(system, body, out _);

		/// <summary>
		/// What a body's surface holds, and where its surface stands.
		/// </summary>
		/// <param name="levelMetres">
		/// The liquid's surface in the PLANET's metres above its datum — what
		/// <see cref="PlanetSurface.AltitudeMetres"/> measures from. 0 for a sea and a magma ocean,
		/// which stand at the datum; below it for lava lakes. A scene multiplies by its own vertical
		/// scale to place it.
		/// </param>
		public static SurfaceLiquid For(SolarSystemProfile system, WorldBody body, out float levelMetres)
		{
			levelMetres = 0f;
			double kelvin = MeanSurfaceKelvin(system, body);
			SurfaceLiquid liquid = Decide(BiomeWorldConditions.For(system, body), kelvin);
			if (liquid == SurfaceLiquid.Lava && kelvin < SolidusKelvin && body != null)
			{
				levelMetres = LavaLakeLevelMetres(body);
			}
			return liquid;
		}

		/// <summary>
		/// A body's globe-mean surface temperature in kelvin, unclamped: from the starlight it
		/// receives over its orbit, its albedo and its greenhouse.
		/// </summary>
		/// <remarks>
		/// <see cref="ClimateModel.MeanSurfaceKelvin(SolarSystemProfile, WorldBody)"/>, the one absolute
		/// temperature the climate field and the world's conditions also read — so the solidus test
		/// here and the liquid-water test (<see cref="BiomeWorldConditions.HasLiquidWater"/>) are asked
		/// of the same number. They used to differ by the orbit: this took hour zero's starlight.
		/// </remarks>
		public static double MeanSurfaceKelvin(SolarSystemProfile system, WorldBody body)
		{
			return ClimateModel.MeanSurfaceKelvin(system, body);
		}

		/// <summary>
		/// How much of lava's fume lingers over it under this air, 0 none to 1 a full plume.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A lava lake breathes out sulphur dioxide and magmatic water vapour, and what makes that a
		/// visible plume is AIR: the gas mixes into it, cools, and the vapour and the sulphate condense
		/// into droplets that hang over the lake and drift. With no air there is nothing to mix into or
		/// condense against — the gas leaves at the speed of its own expansion, a kilometre a second on
		/// Io, on ballistic paths that fall back as frost far away — so nothing hangs over the lake at
		/// all. Thin air (a Mars) holds a faint haze, the gas dispersing fast through a hundredth of the
		/// pressure. Air like ours and thicker hold the full plume.
		/// </para>
		/// <para>
		/// A share, not a density: the material says how dense a full plume is, and this says how much
		/// of it the world allows. WaterSurface.LavaFumes carries it.
		/// </para>
		/// </remarks>
		public static float FumeDensity(AtmosphereKind atmosphere)
		{
			switch (atmosphere)
			{
				case AtmosphereKind.None: return 0f;
				case AtmosphereKind.Thin: return 0.25f;
				default: return 1f;
			}
		}

		/// <summary>
		/// Where a lava lake's surface stands on a body, in the planet's metres above its datum.
		/// </summary>
		/// <remarks>
		/// Measured from the floor the bake measures its glow from: the lowest ground on a dry world,
		/// the sea on a wet one. A dry world's datum is its MEDIAN ground, so this is well below 0 —
		/// the lakes are in the pits, not at the half-way mark.
		/// </remarks>
		public static float LavaLakeLevelMetres(WorldBody body)
		{
			uint seed = body != null ? body.ResolvedTerrainSeed : 1u;
			PlanetSurface.PlanetProfile profile = PlanetSurface.ProfileOf(seed, body);
			float floor = body != null && body.Water > 0f ? profile.SeaLevel : profile.Lowest;
			float height = floor + LavaLakeLevel * (profile.Highest - floor);
			return PlanetSurface.AltitudeFromHeight(height, profile, PlanetSurface.ReliefMetres(body));
		}
	}
}
