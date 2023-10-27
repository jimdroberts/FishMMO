using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[Serializable]
	public class SceneBoundaryDetails
	{
		public Vector3 BoundaryOrigin;
		public Vector3 BoundarySize;

		public bool ContainsPoint(Vector3 point)
		{
			// Checking X, Z, Y order, since X, Z out of bounds is probably more common than Y
			return
				(BoundaryOrigin.x + BoundarySize.x / 2) >= point.x &&
				(BoundaryOrigin.x - BoundarySize.x / 2) <= point.x &&

				(BoundaryOrigin.z + BoundarySize.z / 2) >= point.z &&
				(BoundaryOrigin.z - BoundarySize.z / 2) <= point.z &&

				(BoundaryOrigin.y + BoundarySize.y / 2) >= point.y &&
				(BoundaryOrigin.y - BoundarySize.y / 2) <= point.y;
		}
	}
}