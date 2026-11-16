using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The ECA payload for anything that happens at a waypoint: the traveller is the initiator,
	/// the waypoint's GameObject is the target, and the waypoint itself rides along typed.
	/// </summary>
	/// <remarks>
	/// Used for the waypoint's travel conditions and its on-travel triggers. The unlock path uses
	/// the ordinary <see cref="PlayerInteractionEventData"/> because it <i>is</i> an interaction.
	/// </remarks>
	public class WaypointEventData : EventData
	{
		/// <summary>The waypoint involved.</summary>
		public IWaypoint Waypoint { get; }

		public WaypointEventData(ICharacter initiator, IWaypoint waypoint)
			: base(initiator, waypoint?.GameObject)
		{
			Waypoint = waypoint;
		}
	}
}
