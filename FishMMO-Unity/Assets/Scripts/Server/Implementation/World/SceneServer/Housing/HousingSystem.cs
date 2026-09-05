using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Owns the server's housing configuration and answers who may own land.
	/// </summary>
	/// <remarks>
	/// This is the first slice of the housing work (issues #121 and #132) and deliberately does
	/// nothing but hold and expose the ownership mode. Purchase, building, tax and reclamation
	/// each land separately and all resolve their permission questions through here, so the rule
	/// lives in one place rather than being restated by every system that needs it.
	///
	/// <para>Disabled by default: <see cref="HousingOwnershipMode.Neither"/> means a server that
	/// has not asked for housing carries none of its persistent world state, recurring tax, or
	/// destruction of unpaid plots.</para>
	/// </remarks>
	[CreateAssetMenu(fileName = "HousingSystem", menuName = "FishMMO/Server/SceneServer/Housing System", order = 1)]
	[RequiresDataContainer(typeof(HousingSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class HousingSystem : ServerBehaviour, IHousingSystem
	{
		/// <summary>
		/// Who may own land and housing on this server.
		/// </summary>
		[Header("Ownership")]
		[Tooltip("Who may own land and housing. Neither disables housing entirely, including purchase, building, tax and reclamation.")]
		[SerializeField]
		private HousingOwnershipMode ownershipMode = HousingOwnershipMode.Neither;

		/// <inheritdoc />
		public HousingOwnershipMode OwnershipMode => this.ownershipMode;

		/// <inheritdoc />
		public bool IsHousingEnabled => this.ownershipMode.IsHousingEnabled();

		/// <inheritdoc />
		public bool AllowsPlayerOwnership => this.ownershipMode.AllowsPlayerOwnership();

		/// <inheritdoc />
		public bool AllowsGuildOwnership => this.ownershipMode.AllowsGuildOwnership();

		/// <summary>
		/// Reports the configured ownership mode once at startup.
		/// </summary>
		/// <remarks>
		/// Logged because housing being off is indistinguishable from housing being broken from
		/// the outside — a player who cannot claim a plot sees the same nothing either way. One
		/// line at startup makes the configured state answerable without reading the asset.
		/// </remarks>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("HousingSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindServer;
			}

			if (!this.IsHousingEnabled)
			{
				Log.Debug("HousingSystem", "Housing is disabled (ownership mode: Neither).");
				return ServerComponentInitializationStatus.Initialized;
			}

			Log.Debug("HousingSystem",
				$"Housing enabled. Ownership mode: {this.ownershipMode} " +
				$"(player: {this.AllowsPlayerOwnership}, guild: {this.AllowsGuildOwnership}).");

			SubscribeToPlots();
			RegisterHousingBroadcasts();
			SubscribeToCharacterLifecycle();

			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Releases the foundation subscriptions taken during initialization.
		/// </summary>
		/// <remarks>
		/// Unconditional, unlike the subscribe. The mode is read once at startup, but a static event
		/// outlives this object either way, and unsubscribing something that was never subscribed is
		/// free — where leaving a dead handler attached is a scene server that keeps answering claim
		/// requests after it has been torn down.
		/// </remarks>
		public override void OnDeinitialize()
		{
			UnregisterHousingBroadcasts();
			UnsubscribeFromCharacterLifecycle();
			UnsubscribeFromPlots();
		}

		/// <summary>
		/// Watches for characters leaving, so their build sessions do not outlive them.
		/// </summary>
		/// <remarks>
		/// A build session is closed by the owner saying they are done, and a player who
		/// disconnects, crashes or is kicked says nothing at all. Without this the plot stays shut
		/// until the sweep times it out — and the owner, logging back in, finds land they cannot
		/// enter because of a session they still hold and cannot reach.
		///
		/// <para>Both events are needed. A disconnect is a player leaving; a despawn is a character
		/// being taken out of the world without one, which is what a hand-off to another scene
		/// server looks like from here.</para>
		/// </remarks>
		private void SubscribeToCharacterLifecycle()
		{
			if (Server?.BehaviourRegistry == null ||
				!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				Log.Warning("HousingSystem", "No character system found; build sessions will only close on their timeout.");
				return;
			}

			characterSystem.OnDisconnect += CharacterSystem_OnCharacterLeft;
			characterSystem.OnDespawnCharacter += CharacterSystem_OnCharacterLeft;
		}

		/// <summary>
		/// Stops watching for characters leaving.
		/// </summary>
		private void UnsubscribeFromCharacterLifecycle()
		{
			if (Server?.BehaviourRegistry == null ||
				!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				return;
			}

			characterSystem.OnDisconnect -= CharacterSystem_OnCharacterLeft;
			characterSystem.OnDespawnCharacter -= CharacterSystem_OnCharacterLeft;
		}

		/// <summary>
		/// Closes whatever a departing character was holding open.
		/// </summary>
		private void CharacterSystem_OnCharacterLeft(NetworkConnection conn, IPlayerCharacter player)
		{
			if (player == null)
			{
				return;
			}

			EndBuildingFor(player.ID);
		}
	}
}
