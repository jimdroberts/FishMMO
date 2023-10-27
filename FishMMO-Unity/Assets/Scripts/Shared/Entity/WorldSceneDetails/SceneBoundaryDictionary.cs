using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[Serializable]
	public class SceneBoundaryDictionary : SerializableDictionary<string, SceneBoundaryDetails>
	{
		public bool PointContainedInBoundaries(Vector3 point)
		{
			// In the event we don't have any boundaries, best not to try and enforce them
			if (Count == 0) return true;

			foreach (SceneBoundaryDetails details in Values)
			{
				if (details.ContainsPoint(point)) return true;
			}

			return false;
		}
	}
}