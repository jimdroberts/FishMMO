using FishNet.Transporting;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Applies server-authoritative world state for interactables that change the scene rather than
	/// opening a window.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A switch throwing and a capture point flipping are world state: the server decides, and every
	/// client that can see the object has to be shown the result. Both already had a broadcast
	/// defined and sent — <see cref="SwitchStateBroadcast"/> to the observers of the switch,
	/// <see cref="CapturePointUpdateBroadcast"/> to the observers of the point — and neither had a
	/// single handler on the client, so a door opened on the server and stayed shut on every screen.
	/// </para>
	/// <para>
	/// This lives outside the UI because neither message belongs to a panel. It resolves the object
	/// the message names through <see cref="SceneObject.Objects"/>, which the client populates from
	/// each interactable's spawn payload, and writes the state onto the component. Anything that
	/// wants to draw it — a target frame, a world label, an objective HUD — reads it from there.
	/// </para>
	/// </remarks>
	public sealed class ClientInteractableStateSystem : IClientService
	{
		/// <summary>The system's id among the client's services.</summary>
		public const string ServiceId = "world-interactables.state";

		/// <summary>Registers the system as a client service (<see cref="ClientServices"/>), at load. Moved out of
		/// <c>Client</c>'s own start with the World Interactables module.</summary>
		[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			ClientServices.Register(ServiceId, () => new ClientInteractableStateSystem());
		}

		/// <summary>The network manager the handlers are registered with, or null when not running.</summary>
		private FishNet.Managing.NetworkManager manager;

		/// <summary>
		/// Registers the world-state broadcast handlers.
		/// </summary>
		/// <param name="manager">The client's network manager.</param>
		public void Initialize(FishNet.Managing.NetworkManager manager)
		{
			if (manager == null || manager.ClientManager == null || this.manager != null)
			{
				return;
			}
			this.manager = manager;
			manager.ClientManager.RegisterBroadcast<SwitchStateBroadcast>(OnClientSwitchStateBroadcastReceived);
			manager.ClientManager.RegisterBroadcast<CapturePointUpdateBroadcast>(OnClientCapturePointUpdateBroadcastReceived);
		}

		/// <inheritdoc />
		public void Tick(float deltaTime) { }

		/// <inheritdoc />
		public void Clear() { }

		/// <summary>
		/// Unregisters the world-state broadcast handlers.
		/// </summary>
		public void Shutdown()
		{
			if (manager == null)
			{
				return;
			}
			if (manager.ClientManager != null)
			{
				manager.ClientManager.UnregisterBroadcast<SwitchStateBroadcast>(OnClientSwitchStateBroadcastReceived);
				manager.ClientManager.UnregisterBroadcast<CapturePointUpdateBroadcast>(OnClientCapturePointUpdateBroadcastReceived);
			}
			manager = null;
		}

		/// <summary>
		/// Drives a switch's target to the state the server reports.
		/// </summary>
		/// <remarks>
		/// The switch itself carries no visual — the thing that moves is its
		/// <see cref="ISwitchTarget"/>, which is why this drives the target rather than the switch.
		/// Activate and Deactivate are safe to call for a state the target is already in: both
		/// implementations shipped with the project are idempotent, and a mover already at the
		/// requested pose simply has nothing to travel.
		/// </remarks>
		private static void OnClientSwitchStateBroadcastReceived(SwitchStateBroadcast msg, Channel channel)
		{
			if (!SceneObject.Objects.TryGetValue(msg.InteractableID, out ISceneObject sceneObject))
			{
				// Not observed yet. The switch's own payload carries no state, so nothing can be
				// done here; the next toggle will find it.
				return;
			}

			if (sceneObject is not Switch switchInteractable ||
				switchInteractable.SwitchTarget == null)
			{
				return;
			}

			// Played out from the server's tick, so the door stands where it does for everyone else.
			if (switchInteractable.SwitchTarget is ITimedSwitchTarget timed)
			{
				timed.SetState(msg.Activated, msg.ChangeTick, msg.ChangeTravel, msg.SolidTick);
				return;
			}

			if (msg.Activated)
			{
				switchInteractable.SwitchTarget.Activate(null);
			}
			else
			{
				switchInteractable.SwitchTarget.Deactivate(null);
			}
		}

		/// <summary>
		/// Writes a capture point's reported state onto the client's copy of it.
		/// </summary>
		/// <remarks>
		/// Ownership, progress and objective state all land on the component so that anything
		/// drawing the point — the target frame today, an objective HUD later — reads one place.
		/// The point's own spawn payload seeds the same three fields for a client that starts
		/// observing mid-capture, so a missed message is corrected rather than compounded.
		/// </remarks>
		private static void OnClientCapturePointUpdateBroadcastReceived(CapturePointUpdateBroadcast msg, Channel channel)
		{
			if (!SceneObject.Objects.TryGetValue(msg.InteractableID, out ISceneObject sceneObject))
			{
				return;
			}

			if (sceneObject is not CapturePoint capturePoint)
			{
				return;
			}

			capturePoint.OwnerCharacterID = msg.OwnerCharacterID;
			capturePoint.CaptureProgress = msg.CaptureProgress;
			capturePoint.State = msg.State;

			Log.Debug("ClientInteractableStateSystem",
				$"Capture point {msg.InteractableID}: {msg.State} {msg.CaptureProgress}/{msg.InteractionsToCapture}");
		}
	}
}
