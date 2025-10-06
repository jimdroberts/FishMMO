using System;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI component for displaying NPC dialogue and choices to the player.
	/// </summary>
	public class UINPCDialogue : UICharacterControl
	{
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
		/// Sets the dialogue controller and refreshes the UI.
		/// </summary>
		/// <param name="controller">The NPC dialogue controller to use.</param>
		public void SetDialogueController(NPCDialogueController controller)
		{
			dialogueController = controller;
			RefreshUI();
		}

		/// <summary>
		/// Refreshes the dialogue UI, updating the text and available choices.
		/// </summary>
		public void RefreshUI()
		{
			if (dialogueController == null || DialogueText == null || ChoicesContainer == null || ChoiceButtonPrefab == null)
				return;
			var node = dialogueController.CurrentNode;
			if (node == null)
				return;
			DialogueText.text = node.Text;
			// Clear old choices
			foreach (Transform child in ChoicesContainer)
				Destroy(child.gameObject);
			// Add new choices (only those whose conditions are met)
			for (int i = 0; i < node.Choices.Count; i++)
			{
				var choice = node.Choices[i];
				bool available = true;
				// Evaluate all conditions for this choice; if any fail, the choice is not available
				if (choice.Conditions != null && choice.Conditions.Count > 0)
				{
					foreach (var cond in choice.Conditions)
					{
						if (cond != null && !cond.Evaluate(Character, dialogueController?.CurrentEventData))
						{
							available = false;
							break;
						}
					}
				}
				if (!available)
					continue;
				// Instantiate a new button for the available choice
				var btn = Instantiate(ChoiceButtonPrefab, ChoicesContainer);
				btn.GetComponentInChildren<TextMeshProUGUI>().text = choice.Text;
				int idx = i;
				btn.onClick.AddListener(() => OnChoiceSelected(idx));
				btn.gameObject.SetActive(true);
			}
		}

		/// <summary>
		/// Called when a choice is selected by the player.
		/// </summary>
		/// <param name="index">The index of the selected choice.</param>
		private void OnChoiceSelected(int index)
		{
			dialogueController.Choose(index);
			RefreshUI();
		}
	}
}