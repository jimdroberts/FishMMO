using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for a collision event, containing Unity collision information.
	/// </summary>
	public class CollisionEventData : EventData
	{
		/// <summary>
		/// The Unity collision data associated with the event.
		/// </summary>
		public Collision Collision { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="CollisionEventData"/> class.
		/// </summary>
		/// <param name="initiator">The character initiating the event.</param>
		/// <param name="collision">The Unity collision data.</param>
		public CollisionEventData(ICharacter initiator, Collision collision)
			: base(initiator)
		{
			Collision = collision;
		}
	}
}
