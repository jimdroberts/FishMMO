using UnityEngine;

namespace FishMMO.Shared
{
    public class MoveTickEventData : TickEventData
    {
		public float Speed { get; }

		public MoveTickEventData(ICharacter initiator, float speed, Transform target, float deltaTime)
			: base(initiator, target, deltaTime)
		{
			Speed = speed;
		}
    }
}