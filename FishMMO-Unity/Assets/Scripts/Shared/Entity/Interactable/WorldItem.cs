using UnityEngine;

namespace FishMMO.Shared
{
	public class WorldItem : Interactable
	{
		[SerializeField]
		private BaseItemTemplate template;

		public uint Amount;

		public BaseItemTemplate Template { get { return template; } }
		public override string Title { get { return ""; } }
	}
}