namespace FishMMO.Shared
{
	public class Faction
	{
		public int Value;

		public FactionTemplate Template { get; private set; }

		public Faction(int templateID, int value)
		{
			Template = FactionTemplate.Get<FactionTemplate>(templateID);
			Value = value.Clamp(Template.Minimum, Template.Maximum);
		}
	}
}