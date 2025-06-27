using UnityEngine;

namespace FishMMO.Shared
{
	public class CollisionEventData : EventData
	{
		public Collision Collision { get; }

		public CollisionEventData(ICharacter initiator, Collision collision)
			: base(initiator)
		{
			Collision = collision;
		}
	}
}
