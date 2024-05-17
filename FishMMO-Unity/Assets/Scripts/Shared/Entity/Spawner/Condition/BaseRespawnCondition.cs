using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BaseRespawnCondition : MonoBehaviour
	{
		public abstract bool OnCheckCondition(ObjectSpawner spawner);
	}
}