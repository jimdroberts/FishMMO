using UnityEngine;

namespace FishMMO.Shared
{
	public interface ISpawnable
	{
		[Tooltip("The spawnable prefab object.")]
		GameObject Prefab { get; }
	}
}