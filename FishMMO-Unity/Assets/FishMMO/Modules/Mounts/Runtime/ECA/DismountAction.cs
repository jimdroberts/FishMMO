using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Knocks the struck character off its mount (issue #109: "a new ability effect that dismounts a target
	/// that is hit", for PvP). Crafted onto any ability as the Dismount effect.
	/// </summary>
	/// <remarks>
	/// Done where the target's buffs are simulated: the server (and the target's own client, were it the caster). The
	/// target's owner hears of it through its buff reconcile; everyone else through the buff list. A caster's client does
	/// nothing (another character's buffs are not its to change).
	/// </remarks>
	[Serializable]
	public class DismountAction : BaseAction
	{
		/// <inheritdoc />
		public override string GetTooltipContribution() => "Knocks the target off its mount";

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!TryResolveTarget(eventData, out ICharacter target) || target == null)
			{
				return;
			}
			if (target.TryGet(out ICharacterMount mount) && mount.IsMounted)
			{
				mount.Dismount();
			}
		}
	}
}
