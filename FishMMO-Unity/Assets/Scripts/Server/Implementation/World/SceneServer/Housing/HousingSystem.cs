using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using UnityEngine;

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
			UnsubscribeFromPlots();
		}
	}
}
