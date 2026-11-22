using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Buff template that turns its target to fight for whoever cast it, for as long as it lasts: a charm (the Mesmerize
	/// ability). Only a charmable NPC answers (<see cref="NPC.IsCharmable"/>, not already somebody's companion); on anyone
	/// else it is a buff that does nothing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The brain does the work.</b> Applying hands the target's server brain to the caster through the core seam
	/// <see cref="INPCCharm"/>, which the NPCs module's brain implements: it follows the caster, fights what the caster
	/// fights, and is judged as the caster's by the harm rule (<c>NPC.CharmedBy</c>). Removing the buff — expiry, a dispel,
	/// death — hands it back. Only the server has a brain, so on a client this is the buff's strip entry and its effect.
	/// </para>
	/// <para>
	/// <b>Not a state flag.</b> <see cref="CharacterFlags.IsMesmerized"/> means "cannot act" (<see cref="CharacterIncapacitation"/>),
	/// the opposite of what a charmed NPC does, so this template sets no flag at all.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Charm Buff Template", menuName = "FishMMO/Character/Buff/Charm Buff", order = 5)]
	public class CharmBuffTemplate : BaseBuffTemplate
	{
		/// <inheritdoc />
		public override void SecondaryTooltip(TooltipContent content)
		{
			content.AddEffect("Fights for its charmer", TooltipPriority.Effects);
		}

		/// <summary>Hands the target's brain to the buff's caster.</summary>
		public override void OnApply(Buff buff, ICharacter target)
		{
			if (TryGetCharm(target, out INPCCharm charm) && buff != null && buff.Caster != null)
			{
				charm.BeginCharm(buff.Caster);
			}
		}

		/// <summary>Hands it back.</summary>
		public override void OnRemove(Buff buff, ICharacter target)
		{
			if (TryGetCharm(target, out INPCCharm charm))
			{
				charm.EndCharm(buff?.Caster);
			}
		}

		private static bool TryGetCharm(ICharacter target, out INPCCharm charm)
		{
			charm = null;
			if (target == null || !target.TryGet(out INPCBrain brain))
			{
				return false;
			}
			charm = brain as INPCCharm;
			return charm != null;
		}
	}
}
