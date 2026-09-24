using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// A planet or moon: anything scenes can sit on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Day and night come from <see cref="RotationHours"/> and where the sun actually is, never
	/// from authored day or night lengths. The length of a full day/night cycle is the rotation
	/// plus a small correction for the orbit (see <see cref="CelestialMath.SolarDayHours"/>); the
	/// axial tilt only decides how that cycle is split at a given latitude and season. A tilt of 0
	/// gives every latitude an even split.
	/// </para>
	/// <para>
	/// A tidally locked moon turns once per orbit, so its planet hangs still in its sky and one
	/// day lasts a synodic month. Locking is optional per moon.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New World Body", menuName = "FishMMO/World/Planet or Moon", order = 12)]
	public class WorldBody : CelestialBody
	{
		public WorldBodyKind Kind = WorldBodyKind.Planet;

		[Header("Rotation")]
		[Tooltip("Real hours for one turn on the axis (sidereal). Ignored when tidally locked.")]
		[Min(0.05f)] public float RotationHours = 6f;
		[Tooltip("Turns the other way: the sun rises in the west and the solar day is slightly shorter than the rotation.")]
		public bool Retrograde;
		[Tooltip("Tilt of the axis against the orbit. Seasons and changing day lengths come from this.")]
		[Range(0f, 90f)] public float AxialTiltDegrees = 23.4f;
		[Tooltip("Which way the axis leans: the direction in the orbital plane, in degrees from the world-epoch equinox, that the north pole tips toward. The tilt says how far the axis leans and this says where to. It decides WHEN the seasons fall — northern midsummer is when the sun stands in this direction — and which star the pole points at. Two worlds with the same tilt and different values here have their summers at different times of the year and different pole stars. 90 is where every axis used to lean, so existing worlds keep their seasons.")]
		[Range(0f, 360f)] public float PoleLongitudeDegrees = DefaultPoleLongitudeDegrees;

		/// <summary>Where every axis leaned before it could be chosen: toward +Y of the ecliptic.</summary>
		public const float DefaultPoleLongitudeDegrees = 90f;
		[Tooltip("Moons: turn once per orbit so the planet never moves in the sky.")]
		public bool TidallyLocked;
		[Tooltip("Which way longitude 0 faces at the world epoch, in degrees.")]
		[Range(0f, 360f)] public float RotationOffsetDegrees;

		[Header("Climate")]
		public AtmosphereKind Atmosphere = AtmosphereKind.Standard;
		[Tooltip("How much dust and haze hangs in the air, against a clear day on the home world at 1. It scatters every colour about alike, so more of it pales the sky, whitens the horizon and dims and reddens a low sun. A desert world or one with active volcanoes is several times this; a clean cold one is less.")]
		[Range(0f, 8f)] public float Haze = 1f;
		[Tooltip("What colour that dust is. White for water haze; tan or rust for a desert world, which is what gives such a world a butterscotch sky under thin air, where there is too little gas to make it blue.")]
		public Color HazeColor = Color.white;
		[Tooltip("How strong the body's magnetic field is, against the home world's at 1. It is what an aurora needs besides air: the field catches the star's wind and brings it down in a ring round each pole. None, and there is no aurora however active the star — a small dead world, or one whose core has cooled. Strong, and the aurora is bright and keeps close to the poles; weak, and it is dim.")]
		[Range(0f, 2f)] public float MagneticField = 1f;
		[Tooltip("How far the magnetic pole stands from the pole the world turns on, in degrees. Ours is about eleven. The auroral ring is centred on the MAGNETIC pole, so with a tilt the ring reaches further toward the equator on one side of the world than the other, and which side changes with the hour as the world turns under it.")]
		[Range(0f, 45f)] public float MagneticPoleTiltDegrees = 11f;
		[Tooltip("Which longitude the magnetic pole is tilted toward, in degrees.")]
		[Range(0f, 360f)] public float MagneticPoleLongitudeDegrees = 250f;

		/// <summary>
		/// The magnetic latitude of a place, in degrees: its latitude as the magnetic pole sees it. What
		/// the aurora's ring is drawn round.
		/// </summary>
		public float MagneticLatitude(float latitudeDegrees, float longitudeDegrees)
		{
			float tilt = MagneticPoleTiltDegrees * Mathf.Deg2Rad;
			if (tilt <= 1e-5f)
			{
				return latitudeDegrees;
			}
			float poleLatitude = Mathf.PI * 0.5f - tilt;
			float poleLongitude = MagneticPoleLongitudeDegrees * Mathf.Deg2Rad;
			float latitude = latitudeDegrees * Mathf.Deg2Rad, longitude = longitudeDegrees * Mathf.Deg2Rad;
			// The angle between the place and the magnetic pole, on the sphere; magnetic latitude is
			// ninety less that.
			float cosine = Mathf.Sin(latitude) * Mathf.Sin(poleLatitude) + Mathf.Cos(latitude) * Mathf.Cos(poleLatitude) * Mathf.Cos(longitude - poleLongitude);
			return 90f - Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f)) * Mathf.Rad2Deg;
		}
		/// <summary>
		/// The seed this world's terrain is generated from. Change it and you get a different
		/// planet; keep it and every server, client and tool agrees about every coastline.
		/// </summary>
		/// <remarks>
		/// Zero means "derive one from the asset name", so a body that has never been given a seed
		/// still has a stable world of its own rather than sharing seed 0 with every other body.
		/// </remarks>
		[Tooltip("Seed for this world's terrain. 0 derives one from the body's name.")]
		public uint TerrainSeed;

		/// <summary>The seed actually used: the authored one, or one derived from the name.</summary>
		public uint ResolvedTerrainSeed => TerrainSeed != 0u
			? TerrainSeed
			: unchecked((uint)(name ?? string.Empty).GetDeterministicHashCode());

		[Tooltip("0 dry … 1 ocean world. Feeds the humidity offset.")]
		[Range(0f, 1f)] public float Water = 0.7f;
		[Tooltip("Base climate model for scenes on this body. Scene settings may still name their own.")]
		public ClimateSettings BaseClimate;
		[Tooltip("How this body's sky looks. Empty: the default sky (black if there is no air).")]
		public SkyProfile Sky;
		[Tooltip("The bands of sky this world's clouds live in: how high its deck sits, how deep, and what stands above it. Empty: the default stack, which is the home world's. How the clouds are RENDERED — materials, quality budgets — is not a body's business and stays in the client's Weather Render Profile.")]
		public CloudStackProfile Clouds;

		[Header("Atlas")]
		[Tooltip("Auto grows the globe to fit its scenes and never shrinks it below the minimum. Manual fixes it.")]
		public AtlasRadiusMode RadiusMode = AtlasRadiusMode.Auto;
		[Tooltip("Used when Radius Mode is Manual, in km.")]
		[Min(1f)] public float ManualRadiusKm = 30f;
		[Tooltip("Auto never goes below this, in km.")]
		[Min(1f)] public float MinimumRadiusKm = 30f;
		[Tooltip("Auto: the most of each layer's surface scenes may cover.")]
		[Range(5f, 90f)] public float MaxCoveragePercent = 35f;
		[Tooltip("The radius the atlas designer last settled on, in km. Read at runtime; set by the designer.")]
		public float CurrentRadiusKm = 30f;
		[Tooltip("Layers scenes on this body can sit in. Empty: the atlas's default layers.")]
		public List<FishMMO.Shared.Atlas.WorldAtlasLayer> Layers = new List<FishMMO.Shared.Atlas.WorldAtlasLayer>();

		public bool HasWeather => Atmosphere != AtmosphereKind.None;

		/// <summary>The atlas radius in km, as last baked (or the manual value).</summary>
		public float AtlasRadiusKm => RadiusMode == AtlasRadiusMode.Manual ? Mathf.Max(1f, ManualRadiusKm) : Mathf.Max(MinimumRadiusKm, CurrentRadiusKm);

		private void OnValidate()
		{
			if (Kind != WorldBodyKind.Moon)
			{
				TidallyLocked = false;
			}
		}
	}
}
