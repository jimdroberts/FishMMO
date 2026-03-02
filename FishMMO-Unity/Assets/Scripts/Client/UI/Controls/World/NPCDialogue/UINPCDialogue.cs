using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI component for displaying server-authoritative NPC dialogue.
	/// Receives dialogue state from server broadcasts and sends player choices back for validation.
	/// </summary>
	public class UINPCDialogue : UICharacterControl
	{
		/// <summary>
		/// The text field for displaying the speaker name.
		/// </summary>
		public TextMeshProUGUI SpeakerNameText;

		/// <summary>
		/// The text field for displaying the NPC's dialogue.
		/// </summary>
		public TextMeshProUGUI DialogueText;

		/// <summary>
		/// The container for choice buttons.
		/// </summary>
		public Transform ChoicesContainer;

		/// <summary>
		/// The prefab used to instantiate choice buttons.
		/// </summary>
		public Button ChoiceButtonPrefab;

		/// <summary>
		/// The interactable ID for the current dialogue session.
		/// </summary>
		private long currentInteractableID;

		/// <summary>
		/// The active dialogue template resolved from the server broadcast.
		/// </summary>
		private DialogueTemplate currentTemplate;

		/// <summary>
		/// The node map built from the active template for fast lookups.
		/// </summary>
		private Dictionary<int, DialogueNode> nodeMap;

		/// <summary>
		/// The node currently being displayed.
		/// </summary>
		private DialogueNode currentNode;

		/// <summary>
		/// Bitmask of choices the character has made, received from the server.
		/// </summary>
		private short cachedChoices;

		/// <summary>
		/// Registers dialogue broadcast handlers when the client is set.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<DialogueStartBroadcast>(OnClientDialogueStartReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<DialogueChoiceResultBroadcast>(OnClientDialogueChoiceResultReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<DialogueEndBroadcast>(OnClientDialogueEndReceived);
		}

		/// <summary>
		/// Unregisters dialogue broadcast handlers when the client is unset.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DialogueStartBroadcast>(OnClientDialogueStartReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DialogueChoiceResultBroadcast>(OnClientDialogueChoiceResultReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DialogueEndBroadcast>(OnClientDialogueEndReceived);
		}

		/// <summary>
		/// Cleans up dialogue state when the UI is being destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ClearDialogue();
		}

		/// <summary>
		/// Handles the server starting a dialogue session. Resolves the template and displays the start node.
		/// </summary>
		private void OnClientDialogueStartReceived(DialogueStartBroadcast msg, Channel channel)
		{
			DialogueTemplate template = DialogueTemplate.Get<DialogueTemplate>(msg.TemplateID);
			if (template == null)
			{
				return;
			}

			currentInteractableID = msg.InteractableID;
			currentTemplate = template;
			nodeMap = template.GetNodeMap();
			cachedChoices = msg.CachedChoices;

			if (!nodeMap.TryGetValue(msg.StartNodeId, out DialogueNode startNode))
			{
				return;
			}

			currentNode = startNode;
			RefreshUI();
			Show();
		}

		/// <summary>
		/// Handles the server accepting a dialogue choice. Advances to the next node or closes the dialogue.
		/// </summary>
		private void OnClientDialogueChoiceResultReceived(DialogueChoiceResultBroadcast msg, Channel channel)
		{
			cachedChoices = msg.UpdatedChoices;

			if (msg.NextNodeId < 0 || currentTemplate == null || nodeMap == null)
			{
				CloseDialogue();
				return;
			}

			if (!nodeMap.TryGetValue(msg.NextNodeId, out DialogueNode nextNode))
			{
				CloseDialogue();
				return;
			}

			currentNode = nextNode;
			RefreshUI();
		}

		/// <summary>
		/// Handles the server forcibly ending the dialogue (e.g., out of range).
		/// </summary>
		private void OnClientDialogueEndReceived(DialogueEndBroadcast msg, Channel channel)
		{
			CloseDialogue();
		}

		/// <summary>
		/// Refreshes the dialogue UI with the current node's speaker, text, and available choices.
		/// </summary>
		private void RefreshUI()
		{
			if (currentNode == null || currentTemplate == null || DialogueText == null || ChoicesContainer == null || ChoiceButtonPrefab == null)
			{
				return;
			}

			// Update speaker name
			if (SpeakerNameText != null)
			{
				SpeakerNameText.text = !string.IsNullOrWhiteSpace(currentNode.SpeakerName)
					? currentNode.SpeakerName
					: currentTemplate.Name;
			}

			// Update dialogue text
			DialogueText.text = currentNode.Text;

			// Clear old choice buttons
			ClearChoiceButtons();

			// Add available choices
			if (currentNode.Choices != null)
			{
				for (int i = 0; i < currentNode.Choices.Count; i++)
				{
					DialogueChoice choice = currentNode.Choices[i];
					if (choice == null)
					{
						continue;
					}

					// Check if this choice was already made (via bitmask)
					int bitIndex = currentTemplate.GetChoiceBitIndex(currentNode.NodeId, i);
					if (bitIndex >= 0 && bitIndex < DialogueTemplate.MaxTrackedChoices &&
						currentTemplate.CacheDialogueChoices &&
						(cachedChoices & (1 << bitIndex)) != 0)
					{
						continue;
					}

					// Evaluate conditions client-side for display filtering
					bool conditionsMet = true;
					if (choice.Conditions != null)
					{
						for (int c = 0; c < choice.Conditions.Count; c++)
						{
							if (choice.Conditions[c] != null && !choice.Conditions[c].Evaluate(Character, null))
							{
								conditionsMet = false;
								break;
							}
						}
					}

					if (!conditionsMet)
					{
						continue;
					}

					Button btn = Instantiate(ChoiceButtonPrefab, ChoicesContainer);
					TextMeshProUGUI btnText = btn.GetComponentInChildren<TextMeshProUGUI>();
					if (btnText != null)
					{
						btnText.text = choice.Text;
					}

					int capturedIndex = i;
					btn.onClick.AddListener(() => OnChoiceSelected(capturedIndex));
					btn.gameObject.SetActive(true);
				}
			}
		}

		/// <summary>
		/// Called when the player selects a choice. Sends the choice to the server for validation.
		/// </summary>
		/// <param name="choiceIndex">The index of the selected choice in the node's Choices list.</param>
		private void OnChoiceSelected(int choiceIndex)
		{
			if (Character == null || currentNode == null)
			{
				return;
			}

			Client.NetworkManager.ClientManager.Broadcast(new DialogueChoiceBroadcast()
			{
				InteractableID = currentInteractableID,
				NodeId = currentNode.NodeId,
				ChoiceIndex = choiceIndex,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Closes the dialogue UI and clears all state.
		/// </summary>
		private void CloseDialogue()
		{
			ClearDialogue();
			Hide();
		}

		/// <summary>
		/// Clears dialogue state and choice buttons without hiding the UI.
		/// </summary>
		private void ClearDialogue()
		{
			currentInteractableID = 0;
			currentTemplate = null;
			nodeMap = null;
			currentNode = null;
			cachedChoices = 0;
			ClearChoiceButtons();
		}

		/// <summary>
		/// Destroys all instantiated choice buttons in the container.
		/// </summary>
		private void ClearChoiceButtons()
		{
			if (ChoicesContainer == null)
			{
				return;
			}

			foreach (Transform child in ChoicesContainer)
			{
				Destroy(child.gameObject);
			}
		}
	}
}