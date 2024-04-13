using System;

namespace FishMMO.Shared
{
	public interface ICharacter
	{
		static Action<Character> OnReadPayload;
		static Action<Character> OnStartLocalClient;
		static Action<Character> OnStopLocalClient;
	}
}