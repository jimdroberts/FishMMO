using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A way light's own open fire (<see cref="SceneEmitterKind.Pyre"/>, 2026-10-26, the biome suite's way lights): what its seed
	/// says of it and how bright it burns. One definition read by the client's site lights (SiteLightPool) and its flames and
	/// sparks (SiteEmbers), so the light a beacon throws and the flames over it are kindled at the same instant.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Its seed.</b> The low 16 bits are the flicker's phase, as every fire's (a hash of where it stands); bit 16 marks a
	/// beacon (<see cref="DuskBit"/>: kindled at dusk), bit 17 a brimstone flame (<see cref="BlueBit"/>: burning blue). The
	/// site writes both bits from its piece (StructurePieces.PyreLookOf), never the hash's.
	/// </para>
	/// <para>
	/// <b>Kindled at dusk, on the shared clock.</b> A beacon's fire is dark by day: it is lit as the lamps are, once the sun is
	/// low enough (SkySystem's _FishNightLights, a pure function of the sun's height and so of the shared world clock), each
	/// beacon at its own moment of the dusk (<see cref="KindleAt"/>, from its seed), so a line of beacons along a border is
	/// seen being lit one after another as the dusk deepens, the same for every player. It burns all night and goes out as
	/// the dawn comes back up through the same moment.
	/// </para>
	/// </remarks>
	public static class ScenePyre
	{
		/// <summary>The seed bit of a beacon's fire: dark by day, kindled at dusk (bit 16).</summary>
		public const uint DuskBit = 1u << 16;

		/// <summary>The seed bit of a brimstone flame: sulphur in the oil burns low and blue (bit 17).</summary>
		public const uint BlueBit = 1u << 17;

		/// <summary>
		/// The dusk's span the beacons are kindled over (the lamps' _FishNightLights, 0 by day .. 1 in the night): the first at
		/// <see cref="FirstKindled"/>, the last at <see cref="LastKindled"/>; each takes <see cref="CatchSpan"/> of it to catch.
		/// </summary>
		public const float FirstKindled = 0.06f, LastKindled = 0.56f, CatchSpan = 0.12f;

		/// <summary>A beacon's fire, a brimstone flame and any other's: their brightness at the top of the flicker.</summary>
		public const float BeaconIntensity = 4.2f, BlueIntensity = 1.5f, FireIntensity = 3.2f;

		/// <summary>How far each reaches when its emitter names no reach, metres.</summary>
		public const float BeaconRange = 18f, BlueRange = 6f, FireRange = 11f;

		/// <summary>True for a beacon's fire (kindled at dusk).</summary>
		public static bool AtDusk(uint seed) => (seed & DuskBit) != 0u;

		/// <summary>True for a brimstone flame (blue).</summary>
		public static bool Blue(uint seed) => (seed & BlueBit) != 0u;

		/// <summary>A seed carrying the fire's look in bits 16–17, the rest kept (both bits always written).</summary>
		public static uint WithLook(uint seed, bool atDusk, bool blue)
		{
			seed &= ~(DuskBit | BlueBit);
			return seed | (atDusk ? DuskBit : 0u) | (blue ? BlueBit : 0u);
		}

		/// <summary>Where in the dusk (of _FishNightLights) a beacon is kindled: from its seed's bits 20–27, between the first and the last.</summary>
		public static float KindleAt(uint seed) => FirstKindled + (LastKindled - FirstKindled) * (((seed >> 20) & 0xFFu) / 255f);

		/// <summary>
		/// How far a fire is alight, 0 out .. 1 burning, for the dusk's <paramref name="night"/> (_FishNightLights): a beacon
		/// catches over <see cref="CatchSpan"/> from its own <see cref="KindleAt"/>; any other burns day and night (1).
		/// </summary>
		public static float Kindled(float night, uint seed)
		{
			if (!AtDusk(seed))
			{
				return 1f;
			}
			float at = KindleAt(seed);
			return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(at, at + CatchSpan, night));
		}

		/// <summary>The fire's colour: a beacon's and an open fire's orange, a brimstone flame's blue.</summary>
		public static Color Colour(uint seed) => Blue(seed) ? new Color(0.36f, 0.5f, 1f) : new Color(1f, 0.58f, 0.26f);

		/// <summary>Its reach when the emitter names none, metres.</summary>
		public static float Range(uint seed) => AtDusk(seed) ? BeaconRange : Blue(seed) ? BlueRange : FireRange;

		/// <summary>
		/// The fire's light now: its flicker (three unrelated beats on the shared clock <paramref name="seconds"/>, its phase from
		/// the seed's low bits), times a beacon's kindling, a brimstone flame's low steady burn or an open fire's day-and-night
		/// curve (faint in daylight, as a campfire's).
		/// </summary>
		public static float Light(float seconds, float night, uint seed)
		{
			float phase = (seed & 0xFFFF) * 0.0137f;
			float flicker = 0.82f + 0.09f * Mathf.Sin(seconds * 9.1f + phase) + 0.06f * Mathf.Sin(seconds * 15.7f + phase * 2.3f) + 0.03f * Mathf.Sin(seconds * 4.3f + phase * 3.1f);
			if (AtDusk(seed))
			{
				return BeaconIntensity * Kindled(night, seed) * flicker;
			}
			if (Blue(seed))
			{
				return BlueIntensity * Mathf.Lerp(0.35f, 1f, night) * flicker;
			}
			return FireIntensity * Mathf.Lerp(0.3f, 1f, night) * flicker;
		}
	}
}
