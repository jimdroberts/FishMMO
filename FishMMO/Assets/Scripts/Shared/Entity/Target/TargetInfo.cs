using UnityEngine;

public struct TargetInfo
{
	public Transform target;
	public Vector3 hitPosition;

	public TargetInfo(Transform target, Vector3 hitPosition)
	{
		this.target = target;
		this.hitPosition = hitPosition;
	}
}