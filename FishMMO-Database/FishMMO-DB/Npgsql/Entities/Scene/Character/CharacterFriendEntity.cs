using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_friends", Schema = "fish_mmo_postgresql")]
	[Index(nameof(CharacterID))]
	[Index(nameof(FriendCharacterID))]
	public class CharacterFriendEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public long FriendCharacterID { get; set; }
	}
}