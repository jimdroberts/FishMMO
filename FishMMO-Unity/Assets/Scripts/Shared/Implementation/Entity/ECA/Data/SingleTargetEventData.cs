using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Marks an event whose actions act on its own target only: no trigger or action selector fans it out.
	/// </summary>
	/// <remarks>
	/// For a hit claimed after the object ended (AbilityObject.TryApplyEndedClaim). Its OnHit chain already ran where the
	/// object ended, an area in it included; the claim grants the hit on the victim, and a second fan-out would apply
	/// the area a second time.
	/// </remarks>
	public sealed class SingleTargetEventData : EventData
	{
		public SingleTargetEventData(ICharacter initiator) : base(initiator)
		{
		}
	}
}
