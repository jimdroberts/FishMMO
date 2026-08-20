using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using System.Collections.Generic;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Server-authoritative dialogue session management.
	/// Tracks active dialogue sessions per character, validates client choices,
	/// evaluates ECA conditions/actions, and maintains per-character choice bitmasks.
	/// All dictionaries are bounded to prevent unbounded memory growth.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Maximum concurrent dialogue sessions. Prevents unbounded dictionary growth if sessions are never ended.
		/// </summary>
		private const int MaxActiveDialogueSessions = 2048;

		/// <summary>
		/// Maximum number of characters with cached dialogue choices. Prevents unbounded memory growth.
		/// </summary>
		private const int MaxCachedChoiceCharacters = 4096;
		/// <summary>
		/// Represents an active dialogue session for a single character.
		/// </summary>
		private class DialogueSession
		{
			/// <summary>The scene object ID of the dialogue interactable (0 for ECA-triggered).</summary>
			public long InteractableID;
			/// <summary>The CachedScriptableObject ID of the DialogueTemplate.</summary>
			public int TemplateID;
			/// <summary>The node the character is currently viewing.</summary>
			public int CurrentNodeId;
			/// <summary>Bitmask of choices made during this session.</summary>
			public short ChoicesMade;
		}

		/// <summary>
		/// Active dialogue sessions keyed by character ID.
		/// </summary>
		private readonly Dictionary<long, DialogueSession> activeDialogueSessions = new Dictionary<long, DialogueSession>();

		/// <summary>
		/// Persistent per-character choice bitmasks keyed by character ID → template ID → bitmask.
		/// Only populated for templates with <see cref="DialogueTemplate.CacheDialogueChoices"/> enabled.
		/// </summary>
		private readonly Dictionary<long, Dictionary<int, short>> characterDialogueChoices = new Dictionary<long, Dictionary<int, short>>();

		/// <summary>
		/// Starts a new dialogue session for the character from an interactable interaction.
		/// Evaluates the start node's conditions and sends <see cref="DialogueStartBroadcast"/> to the client.
		/// </summary>
		/// <param name="character">The player character starting the dialogue.</param>
		/// <param name="sceneObject">The scene object hosting the interactable.</param>
		/// <param name="dialogue">The dialogue interactable.</param>
		public void StartDialogueSession(IPlayerCharacter character, ISceneObject sceneObject, IDialogueInteractable dialogue)
		{
			StartDialogueSessionInternal(character, sceneObject != null ? sceneObject.ID : 0, dialogue.Template, dialogue);
		}

		/// <summary>
		/// Starts a new dialogue session triggered by an ECA action (no interactable required).
		/// </summary>
		/// <param name="character">The player character starting the dialogue.</param>
		/// <param name="template">The dialogue template to use.</param>
		public void StartECADialogueSession(IPlayerCharacter character, DialogueTemplate template)
		{
			StartDialogueSessionInternal(character, 0, template, null);
		}

		/// <summary>
		/// Core dialogue session startup. Evaluates start node conditions, creates the session,
		/// and broadcasts <see cref="DialogueStartBroadcast"/> to the client.
		/// </summary>
		private void StartDialogueSessionInternal(IPlayerCharacter character, long interactableID, DialogueTemplate template, IDialogueInteractable dialogue)
		{
			if (character == null || template == null)
			{
				return;
			}

			// End any existing session for this character
			EndDialogueSession(character);

			// Bounded capacity check
			if (activeDialogueSessions.Count >= MaxActiveDialogueSessions)
			{
				Log.Warning("InteractableSystem", $"Active dialogue sessions at capacity ({MaxActiveDialogueSessions}). Rejecting new session.");
				return;
			}

			DialogueNode startNode = template.GetNode(template.StartNodeId);
			if (startNode == null)
			{
				Log.Warning("InteractableSystem", $"Dialogue template '{template.Name}' has no valid start node ({template.StartNodeId}).");
				return;
			}

			// Evaluate start node conditions
			ICharacter speaker = dialogue != null ? dialogue.Transform.GetComponent<ICharacter>() : null;
			DialogueEventData eventData = new DialogueEventData(
				character,
				speaker,
				template.StartNodeId
			);

			if (!EvaluateConditions(startNode.Conditions, character, eventData))
			{
				Log.Debug("InteractableSystem", $"Start node conditions not met for dialogue '{template.Name}'.");
				return;
			}

			// Execute on-enter actions for the start node
			ExecuteActions(startNode.OnEnterActions, character, eventData);

			// Get cached choices for this character + template
			short cachedChoices = GetCachedChoices(character.ID, template.ID);

			// Create the session
			DialogueSession session = new DialogueSession
			{
				InteractableID = interactableID,
				TemplateID = template.ID,
				CurrentNodeId = template.StartNodeId,
				ChoicesMade = cachedChoices,
			};
			activeDialogueSessions[character.ID] = session;

			// Broadcast to client
			Server.NetworkWrapper.Broadcast(character.Owner, new DialogueStartBroadcast()
			{
				InteractableID = interactableID,
				TemplateID = template.ID,
				StartNodeId = template.StartNodeId,
				CachedChoices = cachedChoices,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Handles a <see cref="DialogueChoiceBroadcast"/> from the client.
		/// Validates the session, range, node, choice conditions, executes actions, and advances the dialogue.
		/// </summary>
		private void OnServerDialogueChoiceBroadcastReceived(NetworkConnection conn, DialogueChoiceBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();if (character == null)
			{
				return;
			}
			
			if (!CharacterStateValidation.CanAct(character))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			try
			{
				// Validate session exists
				if (!activeDialogueSessions.TryGetValue(character.ID, out DialogueSession session))
				{
					return;
				}

				// Validate node matches
				if (session.CurrentNodeId != msg.NodeId)
				{
					return;
				}

				// Resolve template
				DialogueTemplate template = DialogueTemplate.Get<DialogueTemplate>(session.TemplateID);
				if (template == null)
				{
					EndDialogueSession(character);
					return;
				}

				// Validate the scene the character is actually in — see CurrentSceneName.
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.CurrentSceneName(), out _))
				{
					EndDialogueSessionWithBroadcast(character);
					return;
				}

				// Range check (skip for ECA-triggered sessions with no interactable)
				if (session.InteractableID != 0)
				{
					if (!ValidateSceneObject(session.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
					{
						EndDialogueSessionWithBroadcast(character);
						return;
					}

					IInteractable interactable = sceneObject.GameObject.GetComponent<IInteractable>();
					if (interactable == null || !interactable.InRange(character.Transform))
					{
						EndDialogueSessionWithBroadcast(character);
						return;
					}
				}

				// Resolve current node
				DialogueNode currentNode = template.GetNode(session.CurrentNodeId);
				if (currentNode == null || msg.ChoiceIndex < 0 || msg.ChoiceIndex >= currentNode.Choices.Count)
				{
					return;
				}

				DialogueChoice choice = currentNode.Choices[msg.ChoiceIndex];
				if (choice == null)
				{
					return;
				}

				// Build event data for condition/action evaluation
				DialogueEventData eventData = new DialogueEventData(
					character,
					null,
					session.CurrentNodeId,
					msg.ChoiceIndex
				);

				// Evaluate choice conditions
				if (!EvaluateConditions(choice.Conditions, character, eventData))
				{
					Log.Debug("InteractableSystem", $"Choice {msg.ChoiceIndex} conditions not met at node {session.CurrentNodeId}.");
					return;
				}

				// Execute on-exit actions for the current node
				ExecuteActions(currentNode.OnExitActions, character, eventData);

				// Execute on-select actions for the chosen choice
				ExecuteActions(choice.OnSelectActions, character, eventData);

				// Update choice bitmask
				int bitIndex = template.GetChoiceBitIndex(session.CurrentNodeId, msg.ChoiceIndex);
				if (bitIndex >= 0 && bitIndex < DialogueTemplate.MaxTrackedChoices)
				{
					// Cast to ushort to avoid sign-extension warnings
					session.ChoicesMade = (short)((ushort)session.ChoicesMade | (ushort)(1 << bitIndex));
				}

				// Transition to next node or end dialogue
				if (choice.NextNodeId < 0)
				{
					// Persist choices if caching is enabled
					if (template.CacheDialogueChoices)
					{
						SetCachedChoices(character.ID, template.ID, session.ChoicesMade);
					}

					EndDialogueSessionWithBroadcast(character);
				}
				else
				{
					// Evaluate the next node
					DialogueNode nextNode = template.GetNode(choice.NextNodeId);
					if (nextNode == null)
					{
						if (template.CacheDialogueChoices)
						{
							SetCachedChoices(character.ID, template.ID, session.ChoicesMade);
						}
						EndDialogueSessionWithBroadcast(character);
						return;
					}

					DialogueEventData nextEventData = new DialogueEventData(
						character,
						null,
						choice.NextNodeId
					);

					if (!EvaluateConditions(nextNode.Conditions, character, nextEventData))
					{
						if (template.CacheDialogueChoices)
						{
							SetCachedChoices(character.ID, template.ID, session.ChoicesMade);
						}
						EndDialogueSessionWithBroadcast(character);
						return;
					}

					// Execute on-enter actions for the next node
					ExecuteActions(nextNode.OnEnterActions, character, nextEventData);

					// Advance session
					session.CurrentNodeId = choice.NextNodeId;

					// Send result to client
					Server.NetworkWrapper.Broadcast(character.Owner, new DialogueChoiceResultBroadcast()
					{
						NextNodeId = choice.NextNodeId,
						UpdatedChoices = session.ChoicesMade,
					}, true, Channel.Reliable);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Ends the dialogue session for the specified character without sending a broadcast.
		/// </summary>
		/// <param name="character">The player character whose session to end.</param>
		public void EndDialogueSession(IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}
			activeDialogueSessions.Remove(character.ID);
		}

		/// <summary>
		/// Ends the dialogue session and sends a <see cref="DialogueEndBroadcast"/> to the client.
		/// </summary>
		private void EndDialogueSessionWithBroadcast(IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}
			activeDialogueSessions.Remove(character.ID);
			Server.NetworkWrapper.Broadcast(character.Owner, new DialogueEndBroadcast(), true, Channel.Reliable);
		}

		/// <summary>
		/// Gets the cached choice bitmask for a character and template combination.
		/// </summary>
		/// <param name="characterId">The character's ID.</param>
		/// <param name="templateId">The dialogue template's CachedScriptableObject ID.</param>
		/// <returns>The bitmask of previously made choices, or 0 if none.</returns>
		private short GetCachedChoices(long characterId, int templateId)
		{
			if (characterDialogueChoices.TryGetValue(characterId, out Dictionary<int, short> templateChoices) &&
				templateChoices.TryGetValue(templateId, out short choices))
			{
				return choices;
			}
			return 0;
		}

		/// <summary>
		/// Stores the cached choice bitmask for a character and template combination.
		/// </summary>
		/// <param name="characterId">The character's ID.</param>
		/// <param name="templateId">The dialogue template's CachedScriptableObject ID.</param>
		/// <param name="choices">The updated choice bitmask.</param>
		private void SetCachedChoices(long characterId, int templateId, short choices)
		{
			if (!characterDialogueChoices.TryGetValue(characterId, out Dictionary<int, short> templateChoices))
			{
				// Bounded capacity check
				if (characterDialogueChoices.Count >= MaxCachedChoiceCharacters)
				{
					Log.Warning("InteractableSystem", $"Cached dialogue choices at capacity ({MaxCachedChoiceCharacters}). Dropping oldest entry.");
					// Remove one arbitrary entry to make room
					using (var enumerator = characterDialogueChoices.GetEnumerator())
					{
						if (enumerator.MoveNext())
						{
							characterDialogueChoices.Remove(enumerator.Current.Key);
						}
					}
				}
				templateChoices = new Dictionary<int, short>();
				characterDialogueChoices[characterId] = templateChoices;
			}
			templateChoices[templateId] = choices;
		}

		/// <summary>
		/// Evaluates a list of ECA conditions. Returns true if all conditions pass or the list is null/empty.
		/// </summary>
		private bool EvaluateConditions(List<BaseCondition> conditions, ICharacter character, EventData eventData)
		{
			if (conditions == null)
			{
				return true;
			}

			for (int i = 0; i < conditions.Count; i++)
			{
				if (conditions[i] != null && !conditions[i].Evaluate(character, eventData))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Executes a list of ECA actions.
		/// </summary>
		private void ExecuteActions(List<BaseAction> actions, ICharacter character, EventData eventData)
		{
			if (actions == null)
			{
				return;
			}

			for (int i = 0; i < actions.Count; i++)
			{
				if (actions[i] != null)
				{
					actions[i].Execute(character, eventData);
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="DisplayDialogueAction.OnServerDialogueRequested"/> static event.
		/// Resolves the initiator as a player character and starts an ECA-triggered dialogue session.
		/// </summary>
		private void OnDisplayDialogueActionRequested(ICharacter initiator, DialogueTemplate template)
		{
			if (initiator == null || template == null)
			{
				return;
			}

			IPlayerCharacter player = initiator as IPlayerCharacter;
			if (player == null)
			{
				return;
			}

			StartECADialogueSession(player, template);
		}
	}
}