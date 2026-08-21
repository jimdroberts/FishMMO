using System.Collections.Generic;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit NPC dialogue panel. Receives server-authoritative dialogue state via broadcasts,
	/// renders the speaker, body text, and dynamic choice buttons, and sends the player's choice
	/// back to the server for validation.
	/// </summary>
	/// <remarks>
	/// <para><b>Content is written after Show, and again after a tree rebuild.</b> A
	/// <c>UIDocument</c> re-clones its UXML on every enable, so the speaker, body and choice
	/// buttons written before <see cref="UITKControl.Show"/> belonged to a tree that was discarded
	/// microseconds later — the panel opened blank. The node arrives in a broadcast, is kept as
	/// plain data, and is rendered from <see cref="OnAfterShow"/> and
	/// <see cref="OnAfterStarting"/>. Both, because on a panel's first open <c>hasStarted</c> is
	/// still false and the re-initialisation path bails out before <c>OnAfterShow</c> would help.</para>
	///
	/// <para><b>There is always a way out.</b> A node whose choices have all been filtered away —
	/// every one already taken, every one's conditions unmet — used to render no buttons at all,
	/// and this panel has no close button of its own. The player was left with a dialogue box that
	/// could only be escaped, and escaping it left the session open on the server. A Leave button
	/// is added whenever no choice survives the filter, and closing the panel by any route tells
	/// the server the conversation is over.</para>
	/// </remarks>
	public class UITKNPCDialogue : UITKCharacterControl
	{
		/// <summary>
		/// Choice index that tells the server the player has left the conversation.
		/// </summary>
		/// <remarks>
		/// Mirrors <c>InteractableSystem.DialogueLeaveChoiceIndex</c>. Declared here rather than
		/// shared because the server type lives in the server assembly, which the client does not
		/// reference; the value is part of the wire contract either way.
		/// </remarks>
		private const int LEAVE_CHOICE_INDEX = -1;
		/// <summary>Name of the speaker label element.</summary>
		private const string SPEAKER_NAME = "dialogue-speaker";

		/// <summary>Name of the dialogue body text element.</summary>
		private const string TEXT_NAME = "dialogue-text";

		/// <summary>Name of the container that holds the generated choice buttons.</summary>
		private const string CHOICES_NAME = "dialogue-choices";

		/// <summary>USS class applied to each generated choice button.</summary>
		private const string CHOICE_BUTTON_CLASS = "dialogue-choice";

		/// <summary>The interactable ID for the current dialogue session.</summary>
		private long currentInteractableID;

		/// <summary>The active dialogue template resolved from the server broadcast.</summary>
		private DialogueTemplate currentTemplate;

		/// <summary>The node map built from the active template for fast lookups.</summary>
		private Dictionary<int, DialogueNode> nodeMap;

		/// <summary>The node currently being displayed.</summary>
		private DialogueNode currentNode;

		/// <summary>Bitmask of choices the character has made, received from the server.</summary>
		private short cachedChoices;

		/// <summary>Label displaying the speaker name.</summary>
		private Label speakerLabel;
		/// <summary>Label displaying the dialogue body text.</summary>
		private Label dialogueText;
		/// <summary>The container element that holds the generated choice buttons.</summary>
		private VisualElement choicesContainer;

		/// <summary>
		/// Queries the speaker, text, and choices elements.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			speakerLabel = root.Q<Label>(SPEAKER_NAME);
			dialogueText = root.Q<Label>(TEXT_NAME);
			choicesContainer = root.Q(CHOICES_NAME);
		}

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
		/// Clears dialogue state when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ClearDialogue();
		}

		/// <summary>
		/// Handles the server starting a dialogue session. Resolves the template and displays the start node.
		/// </summary>
		/// <param name="msg">The dialogue start broadcast.</param>
		/// <param name="channel">The network channel.</param>
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

			/* Show first, then render. Enabling the document clones a fresh tree, so a RefreshUI
			 * before this line writes into elements that are thrown away — which is exactly what
			 * this did, and why the dialogue opened blank. Show() calls OnAfterShow, which
			 * renders. */
			Show();

			// Already visible: Show is a no-op and OnAfterShow never ran, so render directly.
			RefreshUI();
		}

		/// <summary>
		/// Renders the current node into the tree the player will actually see.
		/// </summary>
		protected override void OnAfterShow()
		{
			RefreshUI();
		}

		/// <summary>
		/// Renders the current node again after the visual tree has been rebuilt.
		/// </summary>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			RefreshUI();
		}

		/// <summary>
		/// Tells the server the conversation is over whenever this panel closes.
		/// </summary>
		/// <remarks>
		/// Escape, the close-on-escape registration and any other route through Hide all land
		/// here. Without it the server session outlived the panel, and every later dialogue
		/// message was validated against a conversation the player had walked away from.
		/// </remarks>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			/* Guarded on Visible: Hide is reached from several places, and telling the server the
			 * player left a conversation they were not having would end a session that a later
			 * open had legitimately started. */
			if (!overrideIsAlwaysOpen && Visible)
			{
				SendLeave();
			}
			base.Hide(overrideIsAlwaysOpen);
		}

		/// <summary>
		/// Sends the leave message for the active session, if there is one, and clears local state.
		/// </summary>
		private void SendLeave()
		{
			if (currentNode == null || Character == null || Client == null)
			{
				ClearDialogue();
				return;
			}

			Client.NetworkManager.ClientManager.Broadcast(new DialogueChoiceBroadcast()
			{
				InteractableID = currentInteractableID,
				NodeId = currentNode.NodeId,
				ChoiceIndex = LEAVE_CHOICE_INDEX,
			}, Channel.Reliable);

			ClearDialogue();
		}

		/// <summary>
		/// Handles the server accepting a dialogue choice. Advances to the next node or closes the dialogue.
		/// </summary>
		/// <param name="msg">The dialogue choice result broadcast.</param>
		/// <param name="channel">The network channel.</param>
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
		/// <param name="msg">The dialogue end broadcast.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientDialogueEndReceived(DialogueEndBroadcast msg, Channel channel)
		{
			CloseDialogue();
		}

		/// <summary>
		/// Refreshes the dialogue UI with the current node's speaker, text, and available choices.
		/// </summary>
		private void RefreshUI()
		{
			if (currentNode == null || currentTemplate == null || dialogueText == null || choicesContainer == null)
			{
				return;
			}

			if (speakerLabel != null)
			{
				speakerLabel.text = !string.IsNullOrWhiteSpace(currentNode.SpeakerName)
					? currentNode.SpeakerName
					: currentTemplate.Name;
			}

			dialogueText.text = currentNode.Text;

			ClearChoiceButtons();

			int rendered = 0;

			/* Choices is a serialized list, so an authored node can legitimately leave it null.
			 * Reading .Count off it threw straight out of a broadcast handler. */
			if (currentNode.Choices != null)
			{
				for (int i = 0; i < currentNode.Choices.Count; i++)
				{
					DialogueChoice choice = currentNode.Choices[i];
					if (choice == null)
					{
						continue;
					}

					int bitIndex = currentTemplate.GetChoiceBitIndex(currentNode.NodeId, i);
					if (bitIndex >= 0 && bitIndex < DialogueTemplate.MaxTrackedChoices &&
						currentTemplate.CacheDialogueChoices &&
						(cachedChoices & (1 << bitIndex)) != 0)
					{
						continue;
					}

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

					int capturedIndex = i;
					Button btn = new Button(() => OnChoiceSelected(capturedIndex))
					{
						text = choice.Text,
					};
					btn.AddToClassList("fish-button");
					btn.AddToClassList(CHOICE_BUTTON_CLASS);
					choicesContainer.Add(btn);
					++rendered;
				}
			}

			/* Every filter above can reject every choice — a node whose options are all one-time
			 * and all already taken does exactly that, and so does a node authored with no choices
			 * at all. Without this the panel showed text, no buttons, and no close, while the
			 * server sat holding an open session. */
			if (rendered == 0)
			{
				Button leave = new Button(OnLeaveSelected)
				{
					text = "Leave",
				};
				leave.AddToClassList("fish-button");
				leave.AddToClassList(CHOICE_BUTTON_CLASS);
				choicesContainer.Add(leave);
			}
		}

		/// <summary>
		/// Ends the conversation from the Leave button.
		/// </summary>
		/// <remarks>
		/// Goes through <see cref="UITKControl.Hide()"/> rather than sending directly, so the
		/// button and the Escape key take exactly the same path and cannot drift apart.
		/// </remarks>
		private void OnLeaveSelected()
		{
			Hide();
		}

		/// <summary>
		/// Sends the selected choice to the server for validation.
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
		/// Removes all generated choice buttons from the container.
		/// </summary>
		private void ClearChoiceButtons()
		{
			choicesContainer?.Clear();
		}
	}
}
