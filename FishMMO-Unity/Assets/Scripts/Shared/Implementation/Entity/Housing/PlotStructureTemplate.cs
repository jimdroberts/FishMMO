using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A thing a player can build on a plot.
	/// </summary>
	/// <remarks>
	/// A template rather than a free-form prefab reference, so what may be built is a decision the
	/// server makes from content it ships. A placement request names a template by ID and nothing
	/// else — the client never says which prefab to spawn, only which of the offered pieces it
	/// wants, so a forged request can only ever ask for something that already exists.
	/// </remarks>
	[CreateAssetMenu(fileName = "New Plot Structure", menuName = "FishMMO/Housing/Plot Structure", order = 1)]
	public class PlotStructureTemplate : CachedScriptableObject<PlotStructureTemplate>, ICachedObject
	{
		/// <summary>
		/// The prefab spawned for this structure.
		/// </summary>
		[Tooltip("Prefab spawned when this structure is placed.")]
		public GameObject Prefab;

		/// <summary>
		/// The structure's footprint in metres, width (X) by depth (Z).
		/// </summary>
		/// <remarks>
		/// Authored rather than measured from the prefab's renderers. A mesh's bounds include
		/// decoration that overhangs — eaves, a porch rail, a chimney — and a piece that cannot be
		/// placed because its guttering crosses the plot line is a piece nobody can use. This is the
		/// space the structure is meant to occupy, which is a design decision, not a geometric one.
		/// </remarks>
		[Tooltip("Footprint in metres: width (X) by depth (Z). 1 unit = 1 metre.")]
		public Vector2 Footprint = new Vector2(4f, 4f);

		/// <summary>
		/// The structure's height in metres.
		/// </summary>
		[Tooltip("Height in metres, measured upward from the structure's base.")]
		public float Height = 3f;

		/// <summary>
		/// What it costs to place one, in the server's currency attribute.
		/// </summary>
		[Tooltip("Cost to place this structure. Zero is free.")]
		public long Price;

		/// <summary>
		/// The structure's footprint, floored so a mis-authored template cannot occupy nothing.
		/// </summary>
		/// <remarks>
		/// A zero-sized structure would pass every bounds test, including from outside the plot,
		/// because an empty box is contained by everything.
		/// </remarks>
		public Vector2 SafeFootprint => new Vector2(
			Mathf.Max(PlotFoundation.MinimumExtent, Footprint.x),
			Mathf.Max(PlotFoundation.MinimumExtent, Footprint.y));

		/// <summary>
		/// The structure's height, floored at the same minimum.
		/// </summary>
		public float SafeHeight => Mathf.Max(PlotFoundation.MinimumExtent, Height);

		/// <summary>
		/// The volume this structure would occupy if placed at a point, facing a given way.
		/// </summary>
		/// <param name="worldPosition">Where the structure's base sits.</param>
		/// <param name="yawDegrees">Rotation about the vertical axis.</param>
		/// <remarks>
		/// Rotation is applied by swapping the footprint's axes at right angles rather than by
		/// rotating a box. An axis-aligned box that has been rotated 45° has a larger axis-aligned
		/// extent than the structure really occupies, which would refuse placements that fit; the
		/// quarter turns are the ones that matter for buildings laid out on a grid, and they are
		/// exact.
		/// </remarks>
		public Bounds GetBounds(Vector3 worldPosition, float yawDegrees)
		{
			Vector2 footprint = SafeFootprint;
			float height = SafeHeight;

			if (IsQuarterTurned(yawDegrees))
			{
				footprint = new Vector2(footprint.y, footprint.x);
			}

			return new Bounds(
				new Vector3(worldPosition.x, worldPosition.y + (height * 0.5f), worldPosition.z),
				new Vector3(footprint.x, height, footprint.y));
		}

		/// <summary>
		/// True when a yaw turns the footprint onto its other axis.
		/// </summary>
		/// <remarks>
		/// Normalised into 0-360 first, because a yaw arrives from a client and may be negative or
		/// several turns round. Anything that is not near a quarter turn is treated as unrotated,
		/// which is the conservative reading: it tests the footprint as authored.
		/// </remarks>
		private static bool IsQuarterTurned(float yawDegrees)
		{
			float yaw = Mathf.Repeat(yawDegrees, 360f);

			return (yaw > 45f && yaw < 135f) ||
				   (yaw > 225f && yaw < 315f);
		}
	}
}
