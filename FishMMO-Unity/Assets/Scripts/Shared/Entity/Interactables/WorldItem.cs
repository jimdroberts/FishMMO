using UnityEngine;

namespace FishMMO.Shared
{
	public class WorldItem : Interactable
	{
		[SerializeField]
		private BaseItemTemplate template;

		public BaseItemTemplate Template { get { return template; } }
		public uint Amount { get; private set; }
		public override string Title { get { return Template == null ? "Item" : Template.Name; } }

		public void Initialize(BaseItemTemplate template, uint amount)
		{
			this.template = template;
			Amount = amount;
		}
	}
}