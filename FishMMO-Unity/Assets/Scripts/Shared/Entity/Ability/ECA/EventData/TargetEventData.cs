using UnityEngine;

namespace FishMMO.Shared
{
	public class TargetEventData : EventData
	{
		public GameObject Target { get; }

		public TargetEventData(ICharacter initiator, GameObject target)
			: base(initiator)
		{
			Target = target;
		}
	}
}