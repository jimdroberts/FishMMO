using System;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI component for displaying NPC dialogue and player choices.
	/// Subscribes to NPCDialogueController events to refresh the display automatically.
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
		/// Reference to the current NPCDialogueController driving the dialogue logic.
		/// </summary>
		private NPCDialogueController dialogueController;

		/// <summary>
		/// Opens the dialogue UI with the given controller and subscribes to updates.
		/// </summary>
		/// <param name="controller">The NPC dialogue controller to use.</param>
		public void Open(NPCDialogueController controller)
		{
			if (dialogueController != null)
			{
				dialogueController.OnDialogueUpdated -= RefreshUI;
				dialogueController.OnDialogueEnded -= Close;
			}

			dialogueController = controller;

			if (dialogueController != null)
			{
				dialogueController.OnDialogueUpdated += RefreshUI;
				dialogueController.OnDialogueEnded += Close;
			}

			gameObject.SetActive(true);
			RefreshUI();
		}

		/// <summary>
		/// Closes the dialogue UI and unsubscribes from events.
		/// </summary>
		public void Close()
		{
			if (dialogueController != null)
			{
				dialogueController.OnDialogueUpdated -= RefreshUI;
				dialogueController.OnDialogueEnded -= Close;
				dialogueController = null;
			}

			gameObject.SetActive(false);
		}

		/// <summary>
		/// Refreshes the dialogue UI, updating the speaker name, dialogue text, and available choices.
		/// </summary>
		public void RefreshUI()
		{
			if (dialogueController == null || DialogueText == null || ChoicesContainer == null || ChoiceButtonPrefab == null)
			{
				return;
			}

			var node = dialogueController.CurrentNode;
			if (node == null)
			{
				return;
			}

			// Update speaker name
			if (SpeakerNameText != null)
			{
				SpeakerNameText.text = !string.IsNullOrWhiteSpace(node.SpeakerName)
					? node.SpeakerName
					: dialogueController.Character?.Name ?? "???";
			}

			// Update dialogue text
			DialogueText.text = node.Text;

			// Clear old choices
			foreach (Transform child in ChoicesContainer)
			{
				Destroy(child.gameObject);
			}

			// Add available choices (filtered by conditions)
			var availableChoices = dialogueController.GetAvailableChoices();
			for (int i = 0; i < availableChoices.Count; i++)
			{
				var (originalIndex, choice) = availableChoices[i];
				var btn = Instantiate(ChoiceButtonPrefab, ChoicesContainer);
				btn.GetComponentInChildren<TextMeshProUGUI>().text = choice.Text;
				int capturedIndex = originalIndex;
				btn.onClick.AddListener(() => OnChoiceSelected(capturedIndex));
				btn.gameObject.SetActive(true);
			}
		}

		/// <summary>
		/// Called when the player selects a choice.
		/// </summary>
		/// <param name="index">The original index of the selected choice in the node's choice list.</param>
		private void OnChoiceSelected(int index)
		{
			if (dialogueController != null)
			{
				dialogueController.Choose(index);
			}
		}

		private void OnDestroy()
		{
			if (dialogueController != null)
			{
				dialogueController.OnDialogueUpdated -= RefreshUI;
				dialogueController.OnDialogueEnded -= Close;
			}
		}
	}
}