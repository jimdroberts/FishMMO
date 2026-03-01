using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls NPC dialogue interactions. Manages dialogue flow, condition evaluation,
	/// action execution, and player choice handling. Drives the dialogue tree from a DialogueTemplate.
	/// </summary>
	public class NPCDialogueController : CharacterBehaviour
	{
		/// <summary>
		/// The dialogue template asset defining the conversation tree for this NPC.
		/// </summary>
		[Tooltip("The dialogue template asset defining the conversation tree.")]
		public DialogueTemplate Template;

		/// <summary>
		/// Cached node lookup built from the template.
		/// </summary>
		private Dictionary<int, DialogueNode> nodeMap;

		/// <summary>
		/// The ID of the current dialogue node.
		/// </summary>
		private int currentNodeId = -1;

		/// <summary>
		/// The player currently engaged in dialogue with this NPC.
		/// </summary>
		private IPlayerCharacter currentPlayer;

		/// <summary>
		/// Gets the current dialogue node, or null if none is active.
		/// </summary>
		public DialogueNode CurrentNode
		{
			get
			{
				if (nodeMap != null && nodeMap.TryGetValue(currentNodeId, out DialogueNode node))
				{
					return node;
				}
				return null;
			}
		}

		/// <summary>
		/// The event data for the current dialogue session.
		/// </summary>
		public DialogueEventData CurrentEventData { get; private set; }

		/// <summary>
		/// Fired whenever the dialogue state changes (node entered, dialogue ended, etc.).
		/// </summary>
		public event Action OnDialogueUpdated;

		/// <summary>
		/// Fired when the dialogue ends.
		/// </summary>
		public event Action OnDialogueEnded;

		/// <summary>
		/// Whether a dialogue session is currently active.
		/// </summary>
		public bool IsDialogueActive { get; private set; }

		/// <summary>
		/// Starts a dialogue session with the given player character.
		/// </summary>
		/// <param name="player">The player character initiating the dialogue.</param>
		/// <returns>True if the dialogue started successfully; false otherwise.</returns>
		public bool StartDialogue(IPlayerCharacter player)
		{
			if (Template == null || player == null)
			{
				Log.Warning("NPCDialogueController", $"Cannot start dialogue: Template or player is null.");
				return false;
			}

			currentPlayer = player;
			nodeMap = Template.BuildNodeMap();

			CurrentEventData = new DialogueEventData(
				player,
				Character,
				Template.StartNodeId
			);

			IsDialogueActive = true;

			if (!EnterNode(Template.StartNodeId))
			{
				EndDialogue();
				return false;
			}

			return true;
		}

		/// <summary>
		/// Processes the player selecting a choice at the current node.
		/// Evaluates choice conditions, executes choice actions, and transitions to the next node.
		/// </summary>
		/// <param name="choiceIndex">The index of the selected choice.</param>
		public void Choose(int choiceIndex)
		{
			if (!IsDialogueActive)
			{
				return;
			}

			var node = CurrentNode;
			if (node == null || choiceIndex < 0 || choiceIndex >= node.Choices.Count)
			{
				return;
			}

			var choice = node.Choices[choiceIndex];

			// Evaluate choice conditions
			if (choice.Conditions != null)
			{
				foreach (var condition in choice.Conditions)
				{
					if (condition != null && !condition.Evaluate(currentPlayer, CurrentEventData))
					{
						Log.Debug("NPCDialogueController", $"Choice '{choice.Text}' conditions not met.");
						return;
					}
				}
			}

			// Execute on-exit actions for the current node
			if (node.OnExitActions != null)
			{
				foreach (var action in node.OnExitActions)
				{
					if (action != null)
					{
						action.Execute(currentPlayer, CurrentEventData);
					}
				}
			}

			// Execute on-select actions for the choice
			if (choice.OnSelectActions != null)
			{
				foreach (var action in choice.OnSelectActions)
				{
					if (action != null)
					{
						action.Execute(currentPlayer, CurrentEventData);
					}
				}
			}

			// Transition to the next node, or end dialogue if NextNodeId is -1
			if (choice.NextNodeId < 0)
			{
				EndDialogue();
			}
			else
			{
				// Update event data with the new node and choice index
				CurrentEventData = new DialogueEventData(
					currentPlayer,
					Character,
					choice.NextNodeId,
					choiceIndex
				);

				if (!EnterNode(choice.NextNodeId))
				{
					EndDialogue();
				}
			}
		}

		/// <summary>
		/// Ends the current dialogue session and cleans up state.
		/// </summary>
		public void EndDialogue()
		{
			IsDialogueActive = false;
			currentNodeId = -1;
			currentPlayer = null;
			CurrentEventData = null;

			OnDialogueEnded?.Invoke();
			OnDialogueUpdated?.Invoke();
		}

		/// <summary>
		/// Returns the list of available choices at the current node, filtered by conditions.
		/// </summary>
		/// <returns>List of (index, choice) pairs for choices whose conditions are met.</returns>
		public List<(int Index, DialogueChoice Choice)> GetAvailableChoices()
		{
			var available = new List<(int, DialogueChoice)>();
			var node = CurrentNode;
			if (node == null || node.Choices == null)
			{
				return available;
			}

			for (int i = 0; i < node.Choices.Count; i++)
			{
				var choice = node.Choices[i];
				if (choice == null)
				{
					continue;
				}

				bool conditionsMet = true;
				if (choice.Conditions != null)
				{
					foreach (var condition in choice.Conditions)
					{
						if (condition != null && !condition.Evaluate(currentPlayer, CurrentEventData))
						{
							conditionsMet = false;
							break;
						}
					}
				}

				if (conditionsMet)
				{
					available.Add((i, choice));
				}
			}

			return available;
		}

		/// <summary>
		/// Enters the specified dialogue node. Evaluates conditions and executes on-enter actions.
		/// </summary>
		/// <param name="nodeId">The node ID to enter.</param>
		/// <returns>True if the node was entered successfully; false if conditions failed or node not found.</returns>
		private bool EnterNode(int nodeId)
		{
			if (nodeMap == null || !nodeMap.TryGetValue(nodeId, out DialogueNode node))
			{
				Log.Warning("NPCDialogueController", $"Node {nodeId} not found in dialogue '{Template?.Name}'.");
				return false;
			}

			// Evaluate node conditions
			if (node.Conditions != null)
			{
				foreach (var condition in node.Conditions)
				{
					if (condition != null && !condition.Evaluate(currentPlayer, CurrentEventData))
					{
						Log.Debug("NPCDialogueController", $"Node {nodeId} conditions not met in dialogue '{Template?.Name}'.");
						return false;
					}
				}
			}

			// Execute on-enter actions
			if (node.OnEnterActions != null)
			{
				foreach (var action in node.OnEnterActions)
				{
					if (action != null)
					{
						action.Execute(currentPlayer, CurrentEventData);
					}
				}
			}

			currentNodeId = nodeId;
			OnDialogueUpdated?.Invoke();
			return true;
		}
	}
}