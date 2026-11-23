using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Puts its caster on the mount its saddle names (issue #109): the Mount ability's effect. The mount is a buff
	/// (<see cref="MountBuffTemplate"/>), applied exactly as <see cref="ApplyBuffAction"/> applies one, so it is predicted on
	/// the caster's own client and decided by the server.
	/// </summary>
	/// <remarks>
	/// Always the CASTER, whatever the event's target. Refused, quietly, when the rules refuse (no saddle, in combat, swimming,
	/// indoors: <see cref="MountRules.RefusalFor"/>) at the moment it lands, which can be a cast's length after it began. A
	/// different mount already ridden is got off first (a new saddle).
	/// </remarks>
	[Serializable]
	public class MountAction : BaseAction
	{
		// One apply-buff action per mount, made on first use: the shared path for predicted and authoritative applies.
		private static readonly Dictionary<MountBuffTemplate, ApplyBuffAction> appliers = new Dictionary<MountBuffTemplate, ApplyBuffAction>();

		/// <inheritdoc />
		public override bool ReappliesOnReplay => true;

		/// <inheritdoc />
		public override string GetTooltipContribution() => "Rides the mount your saddle names";

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
			{
				return;
			}
			if (eventData != null && eventData.TargetCharacter != null && !ReferenceEquals(eventData.TargetCharacter, initiator))
			{
				return; // a mount is the caster's own
			}
			MountBuffTemplate mount = MountRules.SaddleMount(initiator);
			if (mount == null || MountRules.RefusalFor(initiator) != null)
			{
				return;
			}
			MountBuffTemplate riding = MountRules.ActiveMount(initiator);
			if (riding == mount)
			{
				return;
			}
			if (riding != null && initiator.TryGet(out ICharacterMount current))
			{
				current.Dismount();
			}
			Applier(mount).Execute(initiator, eventData);
		}

		private static ApplyBuffAction Applier(MountBuffTemplate mount)
		{
			if (!appliers.TryGetValue(mount, out ApplyBuffAction applier))
			{
				applier = new ApplyBuffAction { StacksValue = new ConstantValue { Amount = 1 }, BuffTemplate = mount };
				appliers[mount] = applier;
			}
			return applier;
		}
	}
}
