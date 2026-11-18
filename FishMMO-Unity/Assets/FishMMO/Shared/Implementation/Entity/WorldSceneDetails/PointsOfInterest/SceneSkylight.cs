using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// An opening in a cave's roof up to the sky (2026-10-20, caves livened; <see cref="SceneEmitterKind.Skylight"/>): where its
	/// emitter stands and how its seed carries the opening's height over it, and the two measures every presenter of it agrees
	/// on — how much of the sky a point under it sees (<see cref="SkyShare"/>) and whether the sun falls on it through the
	/// opening (<see cref="InBeam"/>). One definition read by the client's skylight presenter (CaveSkylights: the shaft, the
	/// motes, the sky let in round it), its bounce light (SiteLightPool) and the lit surfaces' underground darkness
	/// (FishSkylights.hlsl, which mirrors these two functions line for line).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the sky's share.</b> Underground the lit surfaces take no sky and no sun (FishUnderground: their ambient and the
	/// sun's term are taken off, since the cave's deep rock casts no shadow). Under an opening that is wrong: the throat's walls
	/// near the top are as good as outdoors, and the floor under it sees a patch of sky — the share a disc of the opening's
	/// radius fills of a point's sky (r² / (r² + h²) on its axis), boosted a little for the light the throat's sunlit walls throw
	/// down, falling off faster off the axis where the rock hides the opening.
	/// </para>
	/// <para>
	/// <b>Why the beam.</b> The sun's term is kept, not taken off, where a line toward the sun leaves through the opening:
	/// the shadow map (the terrain's, its hole clipped; the rock round the opening, which casts) then lights exactly the patch
	/// the sun reaches and no more.
	/// </para>
	/// </remarks>
	public static class SceneSkylight
	{
		/// <summary>How far over the floor under the opening its emitter stands, metres: where its light off the sunlit floor is.</summary>
		public const float LitOver = 1.2f;
		/// <summary>The opening's radius when its emitter names none, metres.</summary>
		public const float DefaultRadius = 4f;
		/// <summary>How much the sky's share (a disc's view factor) is lifted for the light the throat's sunlit walls throw down.</summary>
		public const float SkyBoost = 3f;
		/// <summary>How much faster the sky's share falls off away from under the opening (the rock round it hides it).</summary>
		public const float SkyOffAxis = 4f;
		/// <summary>Where a beam's edge fades, as shares of the opening's radius: the shadow map draws the real edge inside it.</summary>
		public const float BeamInner = 0.8f, BeamOuter = 1.15f;
		/// <summary>The least the sun must stand over the horizon for any beam (its direction's y).</summary>
		public const float LowestSun = 0.02f;
		/// <summary>Openings the shaders take at once (FishSkylights.hlsl's FISH_SKYLIGHTS).</summary>
		public const int MaxNear = 4;

		/// <summary>The bits of a seed that carry the opening's height over its emitter, decimetres (0–819.1 m).</summary>
		public const int HeightBits = 13;
		private const uint HeightMask = (1u << HeightBits) - 1u;

		/// <summary>A skylight's seed: the opening's middle <paramref name="height"/> metres over its emitter, and a hash of its own over the rest.</summary>
		public static uint Seed(float height, uint hash)
			=> (hash << HeightBits) | (uint)Mathf.Clamp(Mathf.RoundToInt(height * 10f), 0, (int)HeightMask);

		/// <summary>How far over its emitter the opening's middle is, metres.</summary>
		public static float Height(uint seed) => (seed & HeightMask) * 0.1f;

		/// <summary>The opening's middle on the ground (world).</summary>
		public static Vector3 Opening(in SceneEmitter e) => e.Position + Vector3.up * Height(e.Seed);

		/// <summary>The opening's radius, metres.</summary>
		public static float Radius(in SceneEmitter e) => e.Reach > 0f ? e.Reach : DefaultRadius;

		/// <summary>The floor under the opening (world): the emitter less <see cref="LitOver"/>.</summary>
		public static Vector3 Floor(in SceneEmitter e) => e.Position - Vector3.up * LitOver;

		/// <summary>
		/// How much of the sky a point sees through an opening, 0..1: all of it at the opening, the boosted share of a disc of its
		/// radius under it, falling off away from under it; nothing over it (the open air is not this one's to give).
		/// </summary>
		public static float SkyShare(Vector3 opening, float radius, Vector3 p)
		{
			float below = opening.y - p.y;
			if (below < 0f)
			{
				return 0f;
			}
			float dx = p.x - opening.x, dz = p.z - opening.z;
			float off = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - radius);
			float r2 = radius * radius;
			float f = r2 / (r2 + below * below + SkyOffAxis * off * off);
			return Mathf.Min(1f, SkyBoost * f);
		}

		/// <summary>
		/// Whether a point under an opening has the sun through it, 0..1: a line from it toward the sun (<paramref name="toSun"/>, unit)
		/// leaves through the opening, soft at the edge (the shadow map draws the true one). Nothing over the opening, nor with the
		/// sun at or under the horizon.
		/// </summary>
		public static float InBeam(Vector3 opening, float radius, Vector3 toSun, Vector3 p)
		{
			float below = opening.y - p.y;
			if (toSun.y <= LowestSun || below < 0f)
			{
				return 0f;
			}
			float t = below / toSun.y;
			float dx = p.x + toSun.x * t - opening.x, dz = p.z + toSun.z * t - opening.z;
			float d = Mathf.Sqrt(dx * dx + dz * dz);
			return 1f - SmoothStep(BeamInner * radius, BeamOuter * radius, d);
		}

		/// <summary>
		/// How much of the sun that comes in at an opening reaches the floor under it rather than the throat's wall, 0..1: all when
		/// the sun stands high enough over a shallow opening, none when it slants in so low it falls on the wall.
		/// </summary>
		public static float BeamOnFloor(float height, float radius, Vector3 toSun)
		{
			if (toSun.y <= LowestSun)
			{
				return 0f;
			}
			float across = Mathf.Sqrt(Mathf.Max(0f, 1f - toSun.y * toSun.y)) / toSun.y;
			return 1f - SmoothStep(0.6f * radius, 2.2f * radius, height * across);
		}

		/// <summary>The greatest sky share at a point of any opening in a list of skylights (for the camera's own ambient).</summary>
		public static float SkyShareAt(System.Collections.Generic.IReadOnlyList<SceneEmitter> skylights, Vector3 p)
		{
			float share = 0f;
			for (int i = 0; i < skylights.Count; i++)
			{
				SceneEmitter e = skylights[i];
				if (e.Kind == SceneEmitterKind.Skylight)
				{
					share = Mathf.Max(share, SkyShare(Opening(e), Radius(e), p));
				}
			}
			return share;
		}

		/// <summary>HLSL's smoothstep (edges, then x): not Mathf.SmoothStep, which interpolates between its first two by its third.</summary>
		public static float SmoothStep(float edge0, float edge1, float x)
		{
			float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-6f, edge1 - edge0));
			return t * t * (3f - 2f * t);
		}
	}
}
