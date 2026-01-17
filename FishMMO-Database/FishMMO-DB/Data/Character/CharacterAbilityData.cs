using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character ability data transfer object.
	/// </summary>
	public struct CharacterAbilityData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
		public List<int> AbilityEvents { get; set; }
		public float Cooldown { get; set; }
	}
}