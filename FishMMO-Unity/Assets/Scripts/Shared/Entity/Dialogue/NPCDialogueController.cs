using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls NPC dialogue interactions, managing dialogue nodes and player choices.
	/// </summary>
	public class NPCDialogueController : CharacterBehaviour
	{
		/// <summary>
		/// Dictionary of all dialogue nodes for this NPC, keyed by node ID.
		/// Used to manage dialogue flow and transitions.
		/// </summary>
		private Dictionary<int, DialogueNode> nodes = new Dictionary<int, DialogueNode>();

		/// <summary>
		/// The ID of the current dialogue node being displayed or interacted with.
		/// </summary>
		private int currentNodeId = -1;

		/// <summary>
		/// Gets the current dialogue node based on the currentNodeId.
		/// Returns null if the node does not exist.
		/// </summary>
		public DialogueNode CurrentNode => nodes.ContainsKey(currentNodeId) ? nodes[currentNodeId] : null;

		/// <summary>
		/// The event data associated with the current dialogue session (e.g., quest, context).
		/// Used for evaluating conditions and executing actions/triggers.
		/// </summary>
		public EventData CurrentEventData { get; set; }

		/// <summary>
		/// Starts a dialogue at the specified node, optionally providing event data for context.
		/// Sets the current node and enters it, triggering any node entry logic.
		/// </summary>
		/// <param name="startNodeId">The node ID to start the dialogue at.</param>
		/// <param name="eventData">Optional event data for the dialogue session.</param>
		public void StartDialogue(int startNodeId, EventData eventData = null)
		{
			CurrentEventData = eventData;
			currentNodeId = startNodeId;
			EnterNode(currentNodeId);
		}

		/// <summary>
		/// Chooses a dialogue option by index, evaluating conditions and executing actions/triggers.
		/// If conditions are not met, the choice is not executed.
		/// </summary>
		/// <param name="choiceIndex">Index of the choice to select.</param>
		public void Choose(int choiceIndex)
		{
			var node = CurrentNode;
			if (node == null || choiceIndex < 0 || choiceIndex >= node.Choices.Count)
				return;
			var choice = node.Choices[choiceIndex];
			// Check conditions for the selected choice
			foreach (var cond in choice.Conditions)
			{
				if (!cond.Evaluate(Character, CurrentEventData))
					return;
			}
			// Execute all actions for the selected choice
			foreach (var act in choice.Actions)
			{
				act.Execute(Character, CurrentEventData);
			}
			// Execute all triggers for the selected choice
			foreach (var trig in choice.Triggers)
			{
				trig.Execute(CurrentEventData);
			}
			// Move to the next node as specified by the choice
			EnterNode(choice.NextNodeId);
		}

		/// <summary>
		/// Enters the specified dialogue node, evaluating node conditions and executing entry actions/triggers.
		/// If conditions are not met, the node is not entered.
		/// </summary>
		/// <param name="nodeId">The node ID to enter.</param>
		private void EnterNode(int nodeId)
		{
			if (!nodes.ContainsKey(nodeId))
				return;
			var node = nodes[nodeId];
			// Check node conditions before entering
			foreach (var cond in node.Conditions)
			{
				if (!cond.Evaluate(Character, CurrentEventData))
					return;
			}
			// Execute all triggers for node entry
			foreach (var trig in node.OnEnterTriggers)
			{
				trig.Execute(CurrentEventData);
			}
			// Execute all actions for node entry
			foreach (var act in node.OnEnterActions)
			{
				act.Execute(Character, CurrentEventData);
			}
			currentNodeId = nodeId;
		}

		/// <summary>
		/// Adds a dialogue node to the controller, keyed by node ID.
		/// If a node with the same ID exists, it is replaced.
		/// </summary>
		/// <param name="nodeId">The node ID to add or replace.</param>
		/// <param name="node">The dialogue node to add.</param>
		public void AddNode(int nodeId, DialogueNode node)
		{
			nodes[nodeId] = node;
		}
	}
}