using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// What a scene on a gas giant stands in: no ground at all, only the giant's air, at the depth where that air is
	/// temperate and water condenses. Islands of rock float in it over a cloud deck that has no floor.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The habitable layer.</b> A giant has no surface; going down, its air only gets warmer and denser. Somewhere on
	/// the way it passes the temperature water condenses at, which is where Jupiter's and Saturn's water clouds are. That
	/// layer is where its scenes are: the air there is warm, wet and breathable for the purposes of the game, whatever the
	/// giant is made of and however far it is from its star (its warmth is its own, from the heat it still makes). So the
	/// climate, the biomes and the weather of a giant's scenes are asked about that layer, never about the cloud tops the
	/// globe draws: <see cref="LayerKelvin"/>, <see cref="LayerWater"/> and <see cref="LayerAtmosphere"/>.
	/// </para>
	/// <para>
	/// <b>Nothing about the globe changes.</b> The banded texture, the giant's colour and its place in the sky are its
	/// cloud tops'; only what lives in the layer reads these.
	/// </para>
	/// </remarks>
	public static class CloudWorld
	{
		/// <summary>The mean temperature of a giant's habitable layer, kelvin: a mild day on a temperate world.</summary>
		public const double LayerKelvin = 290.0;

		/// <summary>How wet the layer's air is, on <see cref="WorldBody.Water"/>'s scale: humid, since it is cloud all round.</summary>
		public const float LayerWater = 0.65f;

		/// <summary>The air the layer's life breathes: an ordinary atmosphere's, for its biomes, weather and pressure.</summary>
		public const AtmosphereKind LayerAtmosphere = AtmosphereKind.Standard;

		/// <summary>
		/// The colour of the atmosphere over the layer, the veil every light reaching it comes through: the giant's own,
		/// as its cloud tops show it from space (<see cref="WorldBody.Tint"/>).
		/// </summary>
		public static Color VeilOf(WorldBody body) => body != null ? new Color(body.Tint.r, body.Tint.g, body.Tint.b, 1f) : Color.white;

		/// <summary>True when scenes on this body are cloud worlds: a gas giant's.</summary>
		public static bool Is(WorldBody body) => body != null && body.Kind == WorldBodyKind.GasGiant;

		/// <summary>The water the body's climate reads: the layer's on a giant, the body's own anywhere else.</summary>
		public static float WaterOf(WorldBody body) => body == null ? 0.7f : Is(body) ? LayerWater : body.Water;

		/// <summary>The air the body's climate reads: the layer's on a giant, the body's own anywhere else.</summary>
		public static AtmosphereKind AtmosphereOf(WorldBody body) => body == null ? AtmosphereKind.Standard : Is(body) ? LayerAtmosphere : body.Atmosphere;
	}
}
