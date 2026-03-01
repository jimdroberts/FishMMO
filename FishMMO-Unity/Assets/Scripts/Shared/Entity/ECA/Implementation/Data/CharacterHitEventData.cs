namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for a character hit event, containing information about the target and random number generator.
	/// </summary>
	public class CharacterHitEventData : EventData
	{
		/// <summary>
		/// The character that was hit or targeted by the event.
		/// </summary>
		public ICharacter Target { get; }

		/// <summary>
		/// The random number generator used for any randomization in the event (optional).
		/// </summary>
		public System.Random RNG { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterHitEventData"/> class.
		/// </summary>
		/// <param name="initiator">The character initiating the event.</param>
		/// <param name="target">The character that was hit or targeted.</param>
		/// <param name="rng">The random number generator for the event (optional).</param>
		public CharacterHitEventData(ICharacter initiator, ICharacter target, System.Random rng = null)
			: base(initiator)
		{
			Target = target;
			RNG = rng;
		}
	}
}