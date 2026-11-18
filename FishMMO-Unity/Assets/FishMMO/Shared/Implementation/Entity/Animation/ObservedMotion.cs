using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a client reads off a character it only watches: whether it swims or climbs, from where it is. Another player's
	/// movement never runs on this client (state forwarding is off; its NetworkTransform places it) and an NPC's runs only
	/// on the server, so nothing tells this client what they are doing. Their body is animated from what can be seen
	/// instead, with the movement's own thresholds, so a watched swimmer swims where the swimmer itself does.
	/// </summary>
	/// <remarks>
	/// Nothing goes on the wire for it. Crouching cannot be seen this way (the capsule's height is not synced), so a
	/// watched character never crouches.
	/// </remarks>
	public static class ObservedMotion
	{
		/// <summary>How far over the feet the ground below is looked for from, metres.</summary>
		public const float GroundProbeHeight = 0.3f;

		/// <summary>How far under the feet ground still counts as stood on, metres (a capsule's skin and a step's lip).</summary>
		public const float GroundTolerance = 0.15f;

		/// <summary>How far past where a climber hangs a watched character may be and still be on the climbable, metres.</summary>
		public const float ClimbSlack = 0.25f;

		/// <summary>
		/// Whether a watched character swims, from the still water over it (<paramref name="stillWater"/>, NaN for none) and
		/// its feet: in at <see cref="Constants.Character.SwimEnterSubmersion"/> of its height under the surface, out under
		/// <see cref="Constants.Character.SwimExitSubmersion"/>, as KCCController decides it, so wading at the depth between
		/// keeps whichever it was.
		/// </summary>
		public static bool Swimming(bool wasSwimming, float stillWater, float feetY, float capsuleHeight)
		{
			if (float.IsNaN(stillWater) || float.IsInfinity(stillWater))
			{
				return false;
			}
			float submersion = (stillWater - feetY) / Mathf.Max(0.1f, capsuleHeight);
			return wasSwimming
				? submersion >= Constants.Character.SwimExitSubmersion
				: submersion >= Constants.Character.SwimEnterSubmersion;
		}

		/// <summary>
		/// Whether feet at <paramref name="feet"/> are on <paramref name="climbable"/>: across the ground within a climber's
		/// hang of its line (its half width, a body's radius of <paramref name="radius"/>, the hang clearance and
		/// <see cref="ClimbSlack"/>), and between its foot and its top.
		/// </summary>
		public static bool OnClimbable(in SceneClimbable climbable, Vector3 feet, float radius)
		{
			if (!ClimbingRules.IsValid(in climbable))
			{
				return false;
			}
			Vector3 bottom = climbable.Bottom;
			Vector3 top = climbable.Top;
			if (feet.y < Mathf.Min(bottom.y, top.y) - 0.2f || feet.y > Mathf.Max(bottom.y, top.y) + 0.2f)
			{
				return false;
			}
			Vector3 axis = top - bottom;
			float length = axis.sqrMagnitude;
			float t = length > 1e-6f ? Mathf.Clamp01(Vector3.Dot(feet - bottom, axis) / length) : 0f;
			Vector3 on = bottom + axis * t;
			float across = new Vector2(feet.x - on.x, feet.z - on.z).magnitude;
			return across <= Mathf.Max(0f, climbable.HalfWidth) + radius + Constants.Character.ClimbHangClearance + ClimbSlack;
		}
	}
}
