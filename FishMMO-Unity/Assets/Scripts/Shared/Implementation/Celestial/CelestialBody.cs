using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// A body in the solar system: a star, a planet or a moon. The hierarchy is the
	/// <see cref="Parent"/> reference; a star has none.
	/// </summary>
	/// <remarks>
	/// Two sizes exist on purpose. <see cref="SkyRadiusKm"/> is how big the body looks from
	/// elsewhere, in real-world units so a moon at 60 000 km reads as a moon. The atlas radius
	/// that playable scenes are laid out on (a few tens of km) lives on <see cref="WorldBody"/>
	/// and has nothing to do with it; one radius cannot serve both without making planets either
	/// invisible in the sky or absurd on the atlas.
	/// </remarks>
	public abstract class CelestialBody : CachedScriptableObject<CelestialBody>, ICachedObject
	{
		[Tooltip("Shown in tools and in game. Defaults to the asset name.")]
		public string DisplayName;
		[Tooltip("Planets: the star. Moons: their planet. Stars: empty.")]
		public CelestialBody Parent;
		public OrbitSettings Orbit = OrbitSettings.Default;
		[Tooltip("How big the body looks from other bodies, in km.")]
		[Min(1f)] public float SkyRadiusKm = 6371f;
		[Tooltip("Tint used where no texture is drawn.")]
		public Color Tint = Color.white;
		[Tooltip("Surface texture, drawn once the body covers enough of another body's sky.")]
		public Texture2D SurfaceTexture;
		public bool HasRings;

		public string ResolvedName => string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName;
	}
}
