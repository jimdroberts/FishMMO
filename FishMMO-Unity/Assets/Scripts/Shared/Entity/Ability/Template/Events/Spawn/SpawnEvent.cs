using System.Collections.Generic;

namespace FishMMO.Shared
{
	public abstract class SpawnEvent : AbilityEvent
	{
		public SpawnEventType SpawnEventType = SpawnEventType.OnSpawn;

		public abstract void Invoke(ICharacter self, TargetInfo targetInfo, AbilityObject initialObject, ref int nextID, Dictionary<int, AbilityObject> abilityObjects);
	}
}