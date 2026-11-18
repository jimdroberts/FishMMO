using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A flag stand or a control point placed in an arena scene. See <see cref="IArenaObjective"/>.
	/// </summary>
	/// <remarks>
	/// Authored like any interactable: a prefab or scene object with this component, a collider,
	/// a <c>NetworkObject</c>, and an interaction whose action is
	/// <see cref="InteractWithArenaObjectiveAction"/>. A Capture the Flag arena needs one flag
	/// stand per team with <see cref="Team"/> set; a King of the Hill arena needs one or more
	/// control points.
	/// </remarks>
	public class ArenaObjective : Interactable, IArenaObjective
	{
		[Tooltip("Flag stand for Capture the Flag, or control point for King of the Hill.")]
		public ArenaObjectiveKind ObjectiveKind = ArenaObjectiveKind.FlagStand;

		[Tooltip("For a flag stand: the team (0-based) whose flag rests here. Ignored for a control point.")]
		[Min(0)]
		public int FlagTeam;

		[Tooltip("Optional display name. Defaults to Flag or Control Point.")]
		public string DisplayName;

		public override string Title
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(DisplayName))
				{
					return DisplayName;
				}
				return ObjectiveKind == ArenaObjectiveKind.FlagStand ? $"Team {FlagTeam + 1} Flag" : "Control Point";
			}
		}

		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.gold); } }

		ArenaObjectiveKind IArenaObjective.Kind => ObjectiveKind;

		int IArenaObjective.Team => FlagTeam;
	}
}
