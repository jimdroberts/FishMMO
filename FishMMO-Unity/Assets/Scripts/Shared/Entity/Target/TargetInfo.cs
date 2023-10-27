using UnityEngine;

namespace FishMMO.Shared
{
	public struct TargetInfo
	{
		public Transform Target;
		public Vector3 HitPosition;

		public TargetInfo(Transform target, Vector3 hitPosition)
		{
			Target = target;
			HitPosition = hitPosition;
		}
	}
}