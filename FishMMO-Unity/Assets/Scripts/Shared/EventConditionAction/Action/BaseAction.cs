using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BaseAction : ScriptableObject, IAction
	{
		public abstract void Execute(ICharacter initiator, EventData eventData);
	}
}