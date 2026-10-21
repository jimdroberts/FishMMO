using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The run-time cave cover (<see cref="CaveCoverSystem"/>, 2026-10-20): how far it reaches, how it thins and steps down with
	/// distance, how thick it grows and how its glowing part glows. On the Weather Render Profile ("Cave cover"); read every
	/// frame, so changes apply while playing.
	/// </summary>
	/// <remarks>
	/// The types themselves (what grows where, how big, how it stands) are the shared table's (FishMMO.Shared CaveCoverTypes),
	/// and which a cave grows is decided at its cut (CaveCoverSite): these are how the cover is drawn, the same everywhere.
	/// Every distance here is for a type of reach 1; a type's own reach scales it (a candle or a root further, grit nearer).
	/// </remarks>
	[Serializable]
	public class CaveCoverSettings
	{
		/// <summary>The most rings the compute reads (FishCaveCover.compute COVER_MAX_RINGS).</summary>
		public const int MaxRings = 8;

		[Tooltip("Off: no cave cover is drawn (the dashboard's A/B button overrides it per play session).")]
		public bool Enabled = true;

		[Header("Distance")]
		[Tooltip("The share of the cover kept with distance: (metres, keep) rising in metres, linear between, nothing past the last. A thinned instance is one of the nearer ones (the same ones every frame), dissolving as it goes. The last ring is the reach.")]
		public Vector2[] Rings = { new Vector2(5f, 1f), new Vector2(12f, 0.55f), new Vector2(24f, 0.22f), new Vector2(40f, 0.06f), new Vector2(48f, 0f) };
		[Tooltip("Where a type steps from its full clump to its reduced one, metres.")]
		[Min(0.5f)] public float Level1Metres = 7f;
		[Tooltip("Where it steps to its farthest, a few faces, metres.")]
		[Min(1f)] public float Level2Metres = 18f;

		[Header("Density")]
		[Tooltip("Every type's density against its own (the table's instances per m² at full habitat).")]
		[Range(0f, 4f)] public float Density = 1f;

		[Header("Glow")]
		[Tooltip("How bright the glowing cover is (the cave's glow colours are HDR; the glow adds after the lighting, so the cave's darkness never dims it).")]
		[Range(0f, 8f)] public float GlowStrength = 2.2f;
		[Tooltip("How much a glowing instance's light swells and fades (a share of it), on the shared motion clock.")]
		[Range(0f, 1f)] public float ShimmerAmount = 0.35f;
		[Tooltip("How fast it does, cycles a second.")]
		[Range(0f, 2f)] public float ShimmerSpeed = 0.12f;

		[Header("Look")]
		[Tooltip("The light at an item's foot against its top (its own shade where it meets the rock).")]
		[Range(0f, 1f)] public float FootShade = 0.55f;
		[Tooltip("How much of the sun and sky underground takes from it (FishUnderground, as the cave rock's own darkness).")]
		[Range(0f, 1f)] public float Underground = 0.9f;
		[Tooltip("Debug: draws every instance magenta, so where the cover lands is plain.")]
		public bool Highlight;

		[Header("Budget")]
		[Tooltip("The instances a (type, level) slot starts with; a slot that overflows grows to what it needed (read back a few times a second), up to the cap.")]
		[Min(256)] public int InitialSlotCapacity = 4096;
		[Tooltip("The most instances one type at one level can draw for one camera; past it, appends are dropped and the editor warns once.")]
		[Min(1024)] public int MaxInstancesPerSlot = 262144;
		[Tooltip("The most cave pieces (32 m cubes of a cave's surface) walked for one camera, nearest first.")]
		[Range(1, 512)] public int MaxPieces = 96;

		/// <summary>The farthest any type reaches, metres: the last ring at the longest type reach.</summary>
		public float Reach(float longestTypeReach) => Rings != null && Rings.Length > 0 ? Rings[Mathf.Min(Rings.Length, MaxRings) - 1].x * Mathf.Max(0.05f, longestTypeReach) : 0f;
	}
}
