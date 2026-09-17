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
		[Tooltip("Moons: turn once per orbit so the planet never moves in the sky.")]
		public bool TidallyLocked;
		[Tooltip("Which way longitude 0 faces at the world epoch, in degrees.")]
		[Range(0f, 360f)] public float RotationOffsetDegrees;

		[Header("Climate")]
		public AtmosphereKind Atmosphere = AtmosphereKind.Standard;
		[Tooltip("0 dry … 1 ocean world. Feeds the humidity offset.")]
		[Range(0f, 1f)] public float Water = 0.7f;
		[Tooltip("Base climate model for scenes on this body. Scene settings may still name their own.")]
		public ClimateSettings BaseClimate;
		[Tooltip("How this body's sky looks. Empty: the default sky (black if there is no air).")]
		public SkyProfile Sky;

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
