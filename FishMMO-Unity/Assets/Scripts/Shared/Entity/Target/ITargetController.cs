using System;
using UnityEngine;

namespace FishMMO.Shared
{
	public interface ITargetController : ICharacterBehaviour
	{
		event Action<GameObject> OnChangeTarget;
		event Action<GameObject> OnUpdateTarget;

		event Action<Transform> OnClearTarget;
		event Action<Transform> OnNewTarget;

		TargetInfo Current { get; }
		TargetInfo UpdateTarget(Vector3 origin, Vector3 direction, float maxDistance);
	}
}