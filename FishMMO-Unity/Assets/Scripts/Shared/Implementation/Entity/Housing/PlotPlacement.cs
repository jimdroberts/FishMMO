using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Why a structure could not be placed on a plot.
	/// </summary>
	/// <remarks>
	/// Distinct reasons rather than a bool, because the server refuses placements the client
	/// already thought were fine — clock skew, a plot sold out from under someone, a template that
	/// was removed — and "no" with nothing after it is indistinguishable from the request having
	/// been dropped.
	/// </remarks>
	public enum PlotPlacementResult
	{
		/// <summary>The structure may be placed.</summary>
		Allowed = 0,

		/// <summary>The plot could not be identified, or has no database row yet.</summary>
		UnknownPlot = 1,

		/// <summary>The structure template could not be identified.</summary>
		UnknownStructure = 2,

		/// <summary>The character does not own the plot.</summary>
		NotTheOwner = 3,

		/// <summary>The plot is not in building mode.</summary>
		NotBuilding = 4,

		/// <summary>Some part of the structure would fall outside the plot.</summary>
		OutOfBounds = 5,

		/// <summary>The structure would intersect something already built here.</summary>
		Occupied = 6,
	}

	/// <summary>
	/// Decides whether a structure fits on a plot.
	/// </summary>
	/// <remarks>
	/// Shared so a client can grey out a placement it knows will fail, while the server reaches the
	/// same verdict from the same code. The client's answer is a courtesy and the server's is the
	/// one that counts — but they must agree, or a player is shown a valid placement that is then
	/// refused, which reads as the game ignoring them.
	/// </remarks>
	public static class PlotPlacement
	{
		/// <summary>
		/// True when one volume lies entirely inside another.
		/// </summary>
		/// <remarks>
		/// <c>Bounds.Contains</c> tests a point, not a box, so a structure whose centre sits inside
		/// a plot would pass it while three of its corners hung over the street.
		/// </remarks>
		public static bool IsFullyInside(Bounds inner, Bounds outer)
		{
			return inner.min.x >= outer.min.x && inner.max.x <= outer.max.x &&
				   inner.min.y >= outer.min.y && inner.max.y <= outer.max.y &&
				   inner.min.z >= outer.min.z && inner.max.z <= outer.max.z;
		}

		/// <summary>
		/// True when two volumes share space.
		/// </summary>
		/// <remarks>
		/// Touching faces are not an intersection, so pieces snapped flush against one another —
		/// a wall meeting a wall, which is how anything gets built — are not read as colliding.
		/// </remarks>
		public static bool Intersects(Bounds a, Bounds b)
		{
			return a.min.x < b.max.x && b.min.x < a.max.x &&
				   a.min.y < b.max.y && b.min.y < a.max.y &&
				   a.min.z < b.max.z && b.min.z < a.max.z;
		}

		/// <summary>
		/// Converts a plot-relative offset into a world position.
		/// </summary>
		/// <remarks>
		/// Structures are stored relative to their plot rather than in world space, so that moving
		/// a foundation in the editor carries the house with it instead of leaving it standing in a
		/// field. The plot's own origin is its transform.
		/// </remarks>
		public static Vector3 ToWorld(Vector3 plotOrigin, Vector3 localOffset)
		{
			return plotOrigin + localOffset;
		}

		/// <summary>
		/// Converts a world position into a plot-relative offset.
		/// </summary>
		public static Vector3 ToLocal(Vector3 plotOrigin, Vector3 worldPosition)
		{
			return worldPosition - plotOrigin;
		}
	}
}
