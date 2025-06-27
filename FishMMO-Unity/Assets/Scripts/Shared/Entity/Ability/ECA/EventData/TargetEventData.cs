using UnityEngine;

namespace FishMMO.Shared
{
	public class TargetEventData : EventData
	{
		public bool Immediate { get; }
		public GameObject Target { get; }

		public TargetEventData(ICharacter initiator, GameObject target, bool immediate = true)
			: base(initiator)
		{
			Immediate = immediate;
			Target = target;
		}
	}
}