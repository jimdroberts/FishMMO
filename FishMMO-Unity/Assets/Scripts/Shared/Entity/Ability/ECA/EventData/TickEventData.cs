using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for a tick (update) event, containing information about the target and time delta.
	/// </summary>
	public class TickEventData : EventData
	{
		/// <summary>
		/// The transform being targeted by the tick event.
		/// </summary>
		public Transform Target { get; }

		/// <summary>
		/// The time delta for the tick (update interval).
		/// </summary>
		public float DeltaTime { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="TickEventData"/> class.
		/// </summary>
		/// <param name="initiator">The character initiating the tick event.</param>
		/// <param name="target">The transform being targeted by the tick event.</param>
		/// <param name="deltaTime">The time delta for the tick.</param>
		public TickEventData(ICharacter initiator, Transform target, float deltaTime)
			: base(initiator)
		{
			Target = target;
			DeltaTime = deltaTime;
		}
	}
}