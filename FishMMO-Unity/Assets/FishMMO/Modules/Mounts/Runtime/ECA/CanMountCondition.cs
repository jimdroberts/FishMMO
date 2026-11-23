using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Mount ability's activation rule (issue #109): its caster wears a saddle, is alive, not in combat, not swimming or
	/// climbing, and not indoors or underground (<see cref="MountRules.RefusalFor"/>).
	/// </summary>
	/// <remarks>
	/// Pressing Mount while mounted never reaches this: it gets off instead (<see cref="MountPressOverride"/>), so a rider
	/// in combat can still get down.
	/// </remarks>
	[Serializable]
	public class CanMountCondition : BaseCondition
	{
		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData = null)
		{
			return MountRules.RefusalFor(initiator) == null;
		}

		/// <inheritdoc />
		public override string GetTooltipContribution() => "Needs a saddle; not in combat, in water, indoors or underground";
	}
}
