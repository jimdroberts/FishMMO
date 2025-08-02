using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for a target event, containing information about the targeted game object.
	/// </summary>
	public class TargetEventData : EventData
	{
		/// <summary>
		/// The game object that is the target of the event.
		/// </summary>
		public GameObject Target { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="TargetEventData"/> class.
		/// </summary>
		/// <param name="initiator">The character initiating the event.</param>
		/// <param name="target">The game object that is the target of the event.</param>
		public TargetEventData(ICharacter initiator, GameObject target)
			: base(initiator)
		{
			Target = target;
		}
	}
}