using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Where somebody standing on a plot they may no longer be on is put instead.
	/// </summary>
	/// <remarks>
	/// Access rules that only apply at the boundary are access rules a player can defeat by not
	/// crossing it. Standing still while a friend revokes their key, or while the owner starts
	/// building, would otherwise leave them inside a house they are now barred from — and logging
	/// out there would let them come back to it. Eviction is what makes the answer to "may I be
	/// here" the same as the answer to "may I come in".
	///
	/// <para>Pure geometry, kept apart from the systems that act on it: the arithmetic is where the
	/// mistakes are, and a mistake here puts a player inside a wall or under the map.</para>
	/// </remarks>
	public static class PlotEviction
	{
		/// <summary>
		/// How far past the boundary an evicted player is placed, in metres.
		/// </summary>
		/// <remarks>
		/// Not zero. Landing exactly on the edge leaves a body whose own radius still overlaps the
		/// plot, which the next check reads as still inside — so the player is evicted again, and
		/// again, and is held rattling against the boundary instead of being put outside it.
		/// </remarks>
		public const float BoundaryMargin = 1.5f;

		/// <summary>
		/// The nearest point outside a plot, at the same height.
		/// </summary>
		/// <param name="plotBounds">The plot's volume.</param>
		/// <param name="position">Where the player is now.</param>
		/// <param name="margin">How far past the edge to place them.</param>
		/// <remarks>
		/// Horizontal only. The plot is a box with a top and a bottom, and the nearest face to
		/// somebody standing on the ground floor is very often the floor — so a nearest-face
		/// eviction would drop players through the terrain. Height is left exactly as it was, which
		/// keeps them on whatever they were standing on.
		///
		/// <para>Ties go to the positive axis rather than being resolved randomly. Somebody dead in
		/// the centre of a square plot is equidistant from all four sides, and two callers reaching
		/// different answers for the same player is how one system evicts them east while another
		/// decides they are still inside.</para>
		/// </remarks>
		public static Vector3 NearestExit(Bounds plotBounds, Vector3 position, float margin = BoundaryMargin)
		{
			float safeMargin = Mathf.Max(0.01f, margin);

			// Already outside the footprint. Moving them would be the surprise, not leaving them.
			if (!IsInsideFootprint(plotBounds, position))
			{
				return position;
			}

			/* Distance to each of the four lateral faces, measured from where they stand. The
			 * smallest is the shortest way out, which is the least disruptive place to put somebody
			 * who has done nothing wrong beyond standing in the wrong room. */
			float toMinX = position.x - plotBounds.min.x;
			float toMaxX = plotBounds.max.x - position.x;
			float toMinZ = position.z - plotBounds.min.z;
			float toMaxZ = plotBounds.max.z - position.z;

			float nearest = Mathf.Min(Mathf.Min(toMaxX, toMinX), Mathf.Min(toMaxZ, toMinZ));

			// Ordered so the positive axes win ties, deliberately.
			if (Mathf.Approximately(nearest, toMaxX))
			{
				return new Vector3(plotBounds.max.x + safeMargin, position.y, position.z);
			}
			if (Mathf.Approximately(nearest, toMaxZ))
			{
				return new Vector3(position.x, position.y, plotBounds.max.z + safeMargin);
			}
			if (Mathf.Approximately(nearest, toMinX))
			{
				return new Vector3(plotBounds.min.x - safeMargin, position.y, position.z);
			}
			return new Vector3(position.x, position.y, plotBounds.min.z - safeMargin);
		}

		/// <summary>
		/// True when a point is over the plot's footprint, whatever its height.
		/// </summary>
		/// <remarks>
		/// Height is ignored on purpose, and this is why eviction does not simply reuse
		/// <c>Bounds.Contains</c>. A plot's volume stops a dozen metres up; a player who has climbed
		/// onto the roof of their own house, or is falling past it, is over the plot and out of the
		/// box at the same time. Treating them as outside would leave the one position from which
		/// somebody can watch a house they are barred from.
		/// </remarks>
		public static bool IsInsideFootprint(Bounds plotBounds, Vector3 position)
		{
			return position.x > plotBounds.min.x && position.x < plotBounds.max.x &&
				   position.z > plotBounds.min.z && position.z < plotBounds.max.z;
		}
	}
}
