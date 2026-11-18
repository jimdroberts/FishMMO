using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// A body's rings: a flat annulus in the plane of its equator.
	/// </summary>
	/// <remarks>
	/// Radii are in the body's own radii, so the rings keep their proportion whatever size the body
	/// is drawn. The texture, when there is one, is a strip read ACROSS the rings — its left edge is
	/// the inner rim and its right edge the outer, colour in rgb and how solid in alpha — which is
	/// all a ring can vary by: it is the same all the way round. Without one the bands are drawn
	/// from the count, each its own weight with a gap between it and the next.
	/// </remarks>
	[System.Serializable]
	public class RingSettings
	{
		[Tooltip("Where the rings begin, in the body's radii. Inside about 1.1 they would touch the surface.")]
		[Range(1.05f, 4f)] public float InnerRadius = 1.3f;
		[Tooltip("Where the rings end, in the body's radii.")]
		[Range(1.2f, 6f)] public float OuterRadius = 2.3f;
		[Tooltip("How many bands the rings are divided into, with a gap between each. One is a single unbroken ring. Ignored when a texture is given.")]
		[Range(1, 24)] public int Bands = 5;
		[Tooltip("How solid the rings are at their densest.")]
		[Range(0f, 1f)] public float Opacity = 0.6f;
		[Tooltip("The colour of the ring material.")]
		public Color Tint = new Color(0.86f, 0.8f, 0.68f, 1f);
		[Tooltip("Optional. A strip read across the rings: left edge the inner rim, right edge the outer; rgb the colour, alpha how solid. It replaces the band count and gives the rings as much structure as the image has.")]
		public Texture2D Texture;

		public float Inner => Mathf.Max(1.05f, InnerRadius);
		public float Outer => Mathf.Max(Inner + 0.05f, OuterRadius);
	}

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
		[Tooltip("What the rings are made of, when the body has them.")]
		public RingSettings Rings = new RingSettings();

		public string ResolvedName => string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName;
	}
}
