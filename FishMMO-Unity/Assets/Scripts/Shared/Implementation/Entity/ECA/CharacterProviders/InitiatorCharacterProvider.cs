using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character provider that always returns the initiator (self).
	/// </summary>
	[Serializable]
	public sealed class InitiatorCharacterProvider : ICharacterProvider
	{
		/// <inheritdoc/>
		public ICharacter GetCharacter(ICharacter initiator, EventData eventData)
		{
			return initiator;
		}
	}
}