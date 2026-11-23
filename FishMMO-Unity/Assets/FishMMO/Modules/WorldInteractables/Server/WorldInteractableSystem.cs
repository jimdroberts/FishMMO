using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// The World Interactables module's server system: dialogue sessions and their persisted choices
	/// (<c>WorldInteractableSystem.Dialogue.cs</c>) and taking items out of containers (<c>WorldInteractableSystem.Container.cs</c>).
	/// Moved out of the core <see cref="InteractableSystem"/>, which keeps the interaction request itself.
	/// </summary>
	/// <remarks>
	/// <para>The other kinds (shrines, lore, gathering, capture points, switches, bindstones) are driven by their ECA actions
	/// from the core interaction path and need no system.</para>
	/// <para>Every request is held to the core's one interaction guard (<see cref="IInteractableSystem.TryBeginInteraction"/>),
	/// so a dialogue choice, a container take and an interaction cannot be interleaved faster than the shared debounce;
	/// object IDs are validated the same way (<see cref="IInteractableSystem.TryResolveSceneObject"/>). Work handed back to
	/// the main thread goes on the core system's queue, which the core system drains.</para>
	/// </remarks>
	[CreateAssetMenu(fileName = "WorldInteractableSystem", menuName = "FishMMO/Server/SceneServer/World Interactable System", order = 1)]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class WorldInteractableSystem : ServerBehaviour
	{
		private IInteractableSystem interactableSystem;

		/// <summary>The scene details the interaction system validates against.</summary>
		private WorldSceneDetailsCache worldSceneDetailsCache => interactableSystem?.WorldSceneDetails;

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("WorldInteractableSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.BehaviourRegistry.TryGet(out interactableSystem) || interactableSystem == null)
			{
				// Every request is validated and guarded by the core interaction system; without it nothing here can run.
				Log.Error("WorldInteractableSystem", "InitializeOnce: IInteractableSystem not found; dialogue and containers are disabled.");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			Server.NetworkWrapper.RegisterBroadcast<DialogueChoiceBroadcast>(OnServerDialogueChoiceBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ContainerTakeItemBroadcast>(OnServerContainerTakeItemBroadcastReceived, true);

			IDialogueInteractable.OnServerDialogueRequested += OnDisplayDialogueActionRequested;

			/* The dialogue choice cache is memory-only and keyed by character ID. Without these
			 * two hooks it has no idea who is still playing, which is what made its capacity
			 * sweep evict a connected player's one-time choices; and nothing ever removed an
			 * entry, so it filled up with characters who had long since logged out. */
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> dialogueCharacterSystem) &&
				dialogueCharacterSystem != null)
			{
				dialogueCharacterSystem.OnAfterLoadCharacter += CharacterSystem_OnDialogueCharacterLoaded;
				dialogueCharacterSystem.OnDisconnect += CharacterSystem_OnDialogueCharacterDisconnected;
			}
			else
			{
				Log.Warning("WorldInteractableSystem", "InitializeOnce: ICharacterSystem not found; cached dialogue choices will not be released on disconnect.");
			}

			Log.Debug("WorldInteractableSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("WorldInteractableSystem", "OnDeinitialize: Server is null");
				return;
			}

			Server.NetworkWrapper.UnregisterBroadcast<DialogueChoiceBroadcast>(OnServerDialogueChoiceBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ContainerTakeItemBroadcast>(OnServerContainerTakeItemBroadcastReceived);

			IDialogueInteractable.OnServerDialogueRequested -= OnDisplayDialogueActionRequested;

			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> dialogueCharacterSystem) &&
				dialogueCharacterSystem != null)
			{
				dialogueCharacterSystem.OnAfterLoadCharacter -= CharacterSystem_OnDialogueCharacterLoaded;
				dialogueCharacterSystem.OnDisconnect -= CharacterSystem_OnDialogueCharacterDisconnected;
			}

			// Dialogue session cleanup
			activeDialogueSessions.Clear();
			characterDialogueChoices.Clear();
			connectedDialogueCharacters.Clear();
			if (unconfirmedDialogueChoices.Count > 0)
			{
				// Nothing is left to retry them; logged (character, template:mask) so they can be applied by hand.
				var lines = new List<string>(unconfirmedDialogueChoices.Count);
				foreach (KeyValuePair<long, Dictionary<int, short>> character in unconfirmedDialogueChoices)
				{
					foreach (KeyValuePair<int, short> template in character.Value)
					{
						lines.Add($"{character.Key}/{template.Key}:{template.Value}");
					}
				}
				Log.Error("WorldInteractableSystem", $"Shutting down with unconfirmed dialogue choices (CharID/TemplateID:mask): {string.Join(", ", lines)}");
				unconfirmedDialogueChoices.Clear();
			}
			interactableSystem = null;
		}

		/// <summary>The core interaction guard, shared by every interaction kind.</summary>
		private bool TryBeginIngressGuard(int connectionId, out long guardKey)
		{
			if (interactableSystem == null)
			{
				guardKey = 0;
				return false;
			}
			return interactableSystem.TryBeginInteraction(connectionId, out guardKey);
		}

		/// <summary>Releases a guard taken by <see cref="TryBeginIngressGuard"/>.</summary>
		private void EndIngressGuard(long guardKey)
		{
			interactableSystem?.EndInteraction(guardKey);
		}

		/// <summary>Resolves a client's scene object ID the way the core interaction path does: present, live, in the character's scene.</summary>
		private bool ValidateSceneObject(long sceneObjectID, SceneHandle characterSceneHandle, out ISceneObject sceneObject)
		{
			if (interactableSystem == null)
			{
				sceneObject = null;
				return false;
			}
			return interactableSystem.TryResolveSceneObject(sceneObjectID, characterSceneHandle, out sceneObject);
		}

		/// <summary>Hands work to the main thread on the core interaction system's queue.</summary>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IInteractableSystemMainThreadQueueData>(action);
		}
	}
}
