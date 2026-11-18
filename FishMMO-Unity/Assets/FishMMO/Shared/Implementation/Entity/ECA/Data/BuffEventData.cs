using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data carrying the buff involved in an apply or remove event.
	/// </summary>
	public class BuffEventData : EventData
	{
		/// <summary>
		/// The buff that was applied or removed.
		/// </summary>
		public Buff Buff { get; }

		/// <summary>
		/// Creates a new BuffEventData.
		/// </summary>
		/// <param name="initiator">The character whose buff state changed.</param>
		/// <param name="buff">The buff that was applied or removed.</param>
		public BuffEventData(ICharacter initiator, Buff buff)
			: base(initiator)
		{
			Buff = buff;
		}
	}
}