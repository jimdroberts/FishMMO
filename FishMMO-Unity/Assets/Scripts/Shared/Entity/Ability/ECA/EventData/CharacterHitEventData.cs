namespace FishMMO.Shared
{
	public class CharacterHitEventData : EventData
	{
		public ICharacter Target { get; }
		public System.Random RNG { get; }

		public CharacterHitEventData(ICharacter initiator, ICharacter target, System.Random rng = null)
			: base(initiator)
		{
			Target = target;
			RNG = rng;
		}
	}
}