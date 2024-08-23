using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("patch_servers", Schema = "fish_mmo_postgresql")]
	public class PatchServerEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public string Address { get; set; }
		public ushort Port { get; set; }
	}
}