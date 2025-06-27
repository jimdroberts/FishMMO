using UnityEngine;

namespace FishMMO.Shared
{
	public class TickEventData : EventData
	{
		public Transform Target { get; }
		public float DeltaTime { get; }

		public TickEventData(ICharacter initiator, Transform target, float deltaTime)
			: base(initiator)
		{
			Target = target;
			DeltaTime = deltaTime;
		}
	}
}