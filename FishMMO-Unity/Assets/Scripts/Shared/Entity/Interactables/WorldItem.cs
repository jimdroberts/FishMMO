namespace FishMMO.Shared
{
	public class WorldItem : Interactable
	{
		public BaseItemTemplate Template { get; private set; }
		public uint Amount { get; private set; }
		public override string Title { get { return Template == null ? "Item" : Template.Name; } }

		public void Initialize(BaseItemTemplate template, uint amount)
		{
			Template = template;
			Amount = amount;
		}
	}
}