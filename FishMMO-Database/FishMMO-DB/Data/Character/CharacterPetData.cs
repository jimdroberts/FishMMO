using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet data transfer object.
	/// </summary>
	public struct CharacterPetData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public int TemplateID { get; set; }
		public List<int> Abilities { get; set; }
		public bool Spawned { get; set; }
	}
}