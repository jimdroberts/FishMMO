using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable dictionary mapping string keys to scene boundary details. Provides logic to check if a point is contained in any boundary.
	/// </summary>
	[Serializable]
	public class SceneBoundaryDictionary : SerializableDictionary<string, SceneBoundaryDetails>
	{
		/// <summary>
		/// Checks if the given point is contained within any of the boundaries in the dictionary.
		/// Returns true if no boundaries are defined.
		/// </summary>
		/// <param name="point">The point to check.</param>
		/// <returns>True if the point is inside any boundary, or if no boundaries exist; false otherwise.</returns>
		public bool PointContainedInBoundaries(Vector3 point)
		{
			// If there are no boundaries, do not enforce containment.
			if (Count == 0) return true;

			foreach (SceneBoundaryDetails details in Values)
			{
				if (details.ContainsPoint(point)) return true;
			}

			return false;
		}
	}
}