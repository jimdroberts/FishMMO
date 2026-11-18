using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The scene server's side of the Factions module: the owner's request to turn aggression on or off. Was part of
	/// <c>AbilitySystem</c>; adopted through the <c>SceneServer_Systems</c> label so nothing outside the module names it.
	/// </summary>
	[CreateAssetMenu(fileName = "FactionSystem", menuName = "FishMMO/Server/SceneServer/Faction System", order = 1)]
	public class FactionSystem : ServerBehaviour
	{
		/// <inheritdoc/>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("FactionSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			Server.NetworkWrapper.RegisterBroadcast<FactionAggressionBroadcast>(OnServerAggressionBroadcastReceived, true);
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		public override void OnDeinitialize()
		{
			Server?.NetworkWrapper?.UnregisterBroadcast<FactionAggressionBroadcast>(OnServerAggressionBroadcastReceived);
		}

		/// <summary>
		/// The owner asks to turn aggression on or off (HarmRule: on, they may harm neutrals and allies, and be harmed by
		/// anyone). Turning it off is refused for <see cref="FactionController.AggressionLockSeconds"/> after any hit or kill
		/// made or taken with it on, so a player cannot strike a neutral and then step out of its reach. The answer is the state
		/// itself: a refused request simply leaves it as it was, which the client's toggle shows.
		/// </summary>
		public void OnServerAggressionBroadcastReceived(NetworkConnection conn, FactionAggressionBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			if (!request.Character.TryGet(out IFactionController faction))
			{
				return;
			}
			if (!faction.TrySetAggression(msg.Enabled, out float lockedSeconds) && lockedSeconds > 0f)
			{
				Log.Debug("FactionSystem", $"{request.Character.CharacterName}: aggression stays on for {lockedSeconds:0.#} s more (fought recently).");
			}
		}
	}
}
