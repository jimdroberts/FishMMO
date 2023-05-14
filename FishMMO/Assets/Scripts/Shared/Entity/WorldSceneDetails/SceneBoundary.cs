using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Server
{
	public class SceneBoundary : IBoundary
	{
		[Header("Scene Boundaries are *inclusive*, if a player is not within it, it will not apply!")]
		public Vector3 BoundarySize;

		public void OnDrawGizmos()
		{
			Gizmos.color = Color.grey;
			Gizmos.DrawWireCube(transform.position, BoundarySize);
		}

		public void OnDrawGizmosSelected()
		{
			Gizmos.color = Color.green;
			Gizmos.DrawCube(transform.position, BoundarySize);
		}

		public override Vector3 GetBoundaryOffset()
		{
			return transform.position;
		}

		public override Vector3 GetBoundarySize()
		{
			return BoundarySize;
		}
	}
}
