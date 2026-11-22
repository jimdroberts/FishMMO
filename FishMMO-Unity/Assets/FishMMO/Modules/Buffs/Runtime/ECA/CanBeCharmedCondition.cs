using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// True when the event's target may be charmed by the initiator (<see cref="CharmBuffTemplate"/>): a living NPC authored
	/// charmable (<see cref="NPC.IsCharmable"/>) that is not somebody else's companion — no player, no pet, no wildlife
	/// (wildlife is never authored charmable).
	/// </summary>
	/// <remarks>
	/// Put on a charm's target selector, so a cast at anything else lands nothing rather than a buff that does nothing. Reads
	/// only authored and replicated state (the NPC's prefab flag, its health), so the caster's client predicts the same answer
	/// the server reaches.
	/// </remarks>
	[Serializable]
	public class CanBeCharmedCondition : BaseCondition
	{
		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			return MayCharm(eventData?.TargetCharacter, initiator);
		}

		/// <summary>The rule, over the two characters.</summary>
		public static bool MayCharm(ICharacter target, ICharacter caster)
		{
			if (target == null || caster == null || ReferenceEquals(target, caster))
			{
				return false;
			}
			if (!(target is NPC npc) || !npc.IsCharmable)
			{
				return false;
			}
			ICharacter owner = npc.CompanionOwner;
			if (owner != null && !ReferenceEquals(owner, caster))
			{
				return false;
			}
			return target.TryGet(out ICharacterDamageController damage) && damage.IsAlive;
		}
	}
}
