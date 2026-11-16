using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.TravelModule))]
// Every player character carries its discovered waypoints (added at wake, not authored on the prefab).
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.WaypointController), typeof(FishMMO.Shared.Core.IPlayerCharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Travel module (Phase 2): discovering waypoints and fast travel between them.
	/// </summary>
	/// <remarks>
	/// <para>Here: the per-character record (<see cref="WaypointController"/>, a character part), the travel rule
	/// (<see cref="WaypointTravel"/>), the broadcasts, the "Waypoint Interact" trigger and its unlock action, the teleport
	/// action, the scene server's <c>WaypointTravelSystem</c> and the save participant over <c>character_waypoints</c>.</para>
	/// <para>Not here, because the world keeps them without fast travel: the <c>Waypoint</c> scene objects and their
	/// baked details (rescue points and cloud landings use them), <c>WaypointUnlockMask</c> (portals use it),
	/// <c>WaypointRegistry</c>, <c>WaypointTravelPolicy</c> and the <c>IWaypointController</c> contract the map reads.
	/// Without this module a waypoint is still a landmark; nothing unlocks and the map offers no travel.</para>
	/// </remarks>
	public sealed class TravelModule : GameModule
	{
		public const string ModuleId = "travel";

		public override string Id => ModuleId;

		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
