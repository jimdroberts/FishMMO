using System;
using UnityEngine;

namespace FishMMO.Shared
{
	public interface ITargetController : ICharacterBehaviour
	{
		event Action<Transform> OnChangeTarget;
		event Action<Transform> OnUpdateTarget;
		event Action<Transform> OnClearTarget;

		TargetInfo Current { get; }
		TargetInfo UpdateTarget(Vector3 origin, Vector3 direction, float maxDistance);
	}
}