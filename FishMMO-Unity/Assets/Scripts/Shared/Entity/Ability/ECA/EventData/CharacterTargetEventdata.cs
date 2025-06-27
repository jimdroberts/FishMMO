namespace FishMMO.Shared
{
	public class CharacterTargetEventData : EventData
	{
		public ICharacter Target { get; }
		public System.Random RNG { get; }

		public CharacterTargetEventData(ICharacter initiator, ICharacter target, System.Random rng = null)
			: base(initiator)
		{
			Target = target;
			RNG = rng;
		}
	}
}