using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that discovers a named waypoint for a character without them visiting it — a
	/// quest reward, a purchased map, a dialogue outcome. Server only.
	/// </summary>
	/// <remarks>
	/// The scene is named explicitly, so a waypoint in another zone can be granted; nothing here
	/// checks that the scene or index exist, because the scene may not be loaded on this server.
	/// A grant for a waypoint that does not exist is a persisted bit nothing ever reads — cheap,
	/// and the world scene details cache rebuild is where authoring mistakes are caught.
	/// </remarks>
	[Serializable]
	public class GrantWaypointAction : BaseAction, IAbortableAction
	{
		[Tooltip("The scene the waypoint stands in.")]
		public string SceneName;

		[Tooltip("The waypoint's authored index within that scene.")]
		public int WaypointIndex;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			TryExecute(initiator, eventData);
		}

		/// <inheritdoc />
		public bool TryExecute(ICharacter initiator, EventData eventData)
		{
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return false;
			}

			if (!TryResolveTargetOrInitiator(initiator, eventData, out ICharacter target) ||
				!target.TryGet(out IWaypointController controller))
			{
				return false;
			}

			return controller.Unlock(SceneName, WaypointIndex);
		}

		/// <inheritdoc />
		public override string GetTooltipContribution()
		{
			return string.IsNullOrEmpty(SceneName) ? null : $"Reveals a waypoint in {SceneName}";
		}
	}
}
