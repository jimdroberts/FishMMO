using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The window a map panel is showing: where it is centred in the world, how much of the world
	/// it covers, and which way is up.
	/// </summary>
	/// <remarks>
	/// <para>One struct shared by the minimap and the world map, and by the camera and the UI
	/// inside each. That is the point of it: the overhead camera, the fog overlay and every marker
	/// have to agree on the mapping from world space to the rectangle on screen, and three
	/// hand-rolled copies of the same trigonometry is how a marker ends up a few pixels off its
	/// terrain — or, once the map can rotate, drifting further out the further it gets from the
	/// centre.</para>
	///
	/// <para>The mapping is deliberately expressed in <b>normalised view coordinates</b> — 0..1
	/// across the visible rectangle, Y up — rather than in pixels. The two panels are different
	/// sizes and the world map is resizable, so pixels are the caller's business; and normalised
	/// coordinates are what both the camera projection and UI Toolkit's mesh UVs want anyway.</para>
	/// </remarks>
	public readonly struct MapViewTransform
	{
		/// <summary>World-space centre of the view. Y is ignored.</summary>
		public readonly Vector3 Center;

		/// <summary>
		/// Half the width and half the height of the view, in world metres.
		/// </summary>
		/// <remarks>
		/// Square rather than a full rectangle. The minimap frame is square, the world map keeps
		/// its aspect by letterboxing rather than by stretching the world, and a single number is
		/// what the camera's orthographic size wants.
		/// </remarks>
		public readonly float Range;

		/// <summary>
		/// Rotation of the view, in degrees clockwise. Zero puts world +Z at the top.
		/// </summary>
		public readonly float RotationDegrees;

		/// <summary>Cosine of the rotation, precomputed.</summary>
		private readonly float cos;

		/// <summary>Sine of the rotation, precomputed.</summary>
		private readonly float sin;

		/// <summary>
		/// Builds a view window.
		/// </summary>
		/// <param name="center">World-space centre. Y is ignored.</param>
		/// <param name="range">Half-extent in world metres. Clamped to a positive value.</param>
		/// <param name="rotationDegrees">Rotation in degrees clockwise. Zero puts +Z up.</param>
		public MapViewTransform(Vector3 center, float range, float rotationDegrees)
		{
			Center = center;
			Range = Mathf.Max(0.0001f, range);
			RotationDegrees = rotationDegrees;

			float radians = rotationDegrees * Mathf.Deg2Rad;
			cos = Mathf.Cos(radians);
			sin = Mathf.Sin(radians);
		}

		/// <summary>The diameter of the view in world metres.</summary>
		public float Diameter => Range * 2.0f;

		/// <summary>
		/// Converts a world position into normalised view coordinates.
		/// </summary>
		/// <param name="worldPosition">The world position. Y is ignored.</param>
		/// <returns>
		/// X and Y in 0..1 across the view, Y increasing towards the top of the map. Values
		/// outside 0..1 are returned as they are: a caller drawing an off-view marker needs the
		/// unclamped vector to work out which edge to pin it to.
		/// </returns>
		public Vector2 WorldToView(Vector3 worldPosition)
		{
			float dx = worldPosition.x - Center.x;
			float dz = worldPosition.z - Center.z;

			/* Projection onto the overhead camera's own axes, and the signs are load-bearing.
			 *
			 * The camera is Quaternion.Euler(90, RotationDegrees, 0), which puts its screen-up along
			 * world (sin, 0, cos) and its screen-right along (cos, 0, -sin) — so the component of an
			 * offset along each is what these two lines compute. Rotating by the opposite sign
			 * produces a map that turns exactly the right amount in exactly the wrong direction: it
			 * is stationary and correct while the player faces north, looks plausible in a
			 * screenshot, and is unusable the moment they turn round. */
			float rx = (dx * cos) - (dz * sin);
			float rz = (dx * sin) + (dz * cos);

			return new Vector2(0.5f + (rx / Diameter), 0.5f + (rz / Diameter));
		}

		/// <summary>
		/// Converts normalised view coordinates back into a world position.
		/// </summary>
		/// <param name="view">Normalised view coordinates, X and Y in 0..1.</param>
		/// <returns>The world position, with Y taken from <see cref="Center"/>.</returns>
		public Vector3 ViewToWorld(Vector2 view)
		{
			float rx = (view.x - 0.5f) * Diameter;
			float rz = (view.y - 0.5f) * Diameter;

			// The inverse of WorldToView's projection: the same rotation with the sine negated.
			float dx = (rx * cos) + (rz * sin);
			float dz = (rz * cos) - (rx * sin);

			return new Vector3(Center.x + dx, Center.y, Center.z + dz);
		}

		/// <summary>
		/// Converts a world-space heading into the angle it should be drawn at on this view.
		/// </summary>
		/// <param name="worldDegrees">Heading in degrees clockwise from world north.</param>
		/// <returns>Heading in degrees clockwise from the top of the map.</returns>
		public float WorldToViewAngle(float worldDegrees)
		{
			return worldDegrees - RotationDegrees;
		}

		/// <summary>
		/// The world-space rectangle this view covers when it is not rotated.
		/// </summary>
		/// <remarks>
		/// Only meaningful at zero rotation — a rotated view covers a rotated square, which is not
		/// a <c>Rect</c>. Used for the axis-aligned work that only the world map does, where the
		/// view is always north-up.
		/// </remarks>
		public Rect WorldRect => new Rect(Center.x - Range, Center.z - Range, Diameter, Diameter);
	}
}
