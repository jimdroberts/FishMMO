using System;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The staff and support tools of a scene server: the <c>/gm</c> and <c>/admin</c> command sets (with every module's
	/// commands, see <see cref="OperatorCommandProvider"/>), the staff console's requests, game master presence (fly,
	/// invisibility), and the player support commands and report panel (<c>/report</c>, <c>/bug</c>, <c>/helpme</c>,
	/// <c>/tickets</c>).
	/// </summary>
	/// <remarks>
	/// The Staff and Support module. These were partials of <c>SceneServerSystem</c> (the command
	/// sets, the console, presence) and <c>ChatSystem</c> (support and reports); they are one system now, so a game
	/// without staff tools leaves the module out and the scene and chat systems run without them.
	/// </remarks>
	[CreateAssetMenu(fileName = "StaffSystem", menuName = "FishMMO/Server/SceneServer/Staff System", order = 1)]
	[RequiresDataContainer(typeof(StaffSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class StaffSystem : ServerBehaviour
	{
		/// <summary>The scene server whose world, clock and server control the tools report and change.</summary>
		private SceneServerSystem sceneServer;

		/// <summary>The chat system, whose system channel the support commands answer on.</summary>
		private ChatSystem chatSystem;

		[Header("Main Thread Dispatch")]
		[Tooltip("Most staff-system replies drained from the main-thread queue per frame.")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("StaffSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.DataContainerRegistry.TryGet<IStaffSystemMainThreadQueueData>(out _))
			{
				Log.Error("StaffSystem", "InitializeOnce: IStaffSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}
			Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem);
			Server.BehaviourRegistry.TryGet(out sceneServer);
			Server.BehaviourRegistry.TryGet(out chatSystem);
			if (sceneServer == null)
			{
				Log.Error("StaffSystem", "InitializeOnce: SceneServerSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);

			RegisterAdminCommands();
			RegisterGameMasterCommands();
			InitializeGameMasterPresence(characterSystem);
			RegisterSupportCommands();

			Log.Debug("StaffSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void OnDeinitialize()
		{
			DrainMainThreadQueue(drainAll: true);
			UnregisterSupportCommands();
			UnregisterAdminCommands();
			UnregisterGameMasterCommands();
			DeinitializeGameMasterPresence();
			DespawnSimulatedPlayers();
		}

		/// <inheritdoc />
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
			UpdateSimulationRun(deltaTime);
		}

		/// <summary>Sends one system-channel line (the chat system's, when it runs).</summary>
		private void OnSendSystemMessage(NetworkConnection conn, string message)
		{
			if (chatSystem != null)
			{
				chatSystem.OnSendSystemMessage(conn, message);
			}
		}

		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IStaffSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>Runs <paramref name="action"/> on the main thread; false when the queue refused it.</summary>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IStaffSystemMainThreadQueueData>(action);
		}
	}
}
