using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA condition: the character has discovered a particular waypoint.
	/// </summary>
	/// <remarks>
	/// Answers the same on every peer that holds the record — the server, and the owner client
	/// — and false on an observer, whose copy of a stranger's controller is empty by design.
	/// </remarks>
	[Serializable]
	public class WaypointUnlockedCondition : BaseCondition
	{
		[Tooltip("The scene the waypoint stands in. Leave empty for the character's current scene.")]
		public string SceneName;

		[Tooltip("The waypoint's authored index within that scene.")]
		public int WaypointIndex;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData = null)
		{
			ICharacter characterToCheck = eventData?.TargetCharacter ?? initiator;
			if (characterToCheck == null || !characterToCheck.TryGet(out IWaypointController controller))
			{
				return false;
			}

			string sceneName = SceneName;
			if (string.IsNullOrEmpty(sceneName))
			{
				sceneName = characterToCheck is IPlayerCharacter player
					? player.CurrentSceneName()
					: characterToCheck.GameObject != null ? characterToCheck.GameObject.scene.name : null;
			}

			return controller.IsUnlocked(sceneName, WaypointIndex);
		}

		/// <inheritdoc />
		public override string GetTooltipContribution()
		{
			return "Requires a discovered waypoint";
		}
	}
}
