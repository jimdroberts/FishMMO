using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_friends")]
	[Index(nameof(CharacterID))]
	[Index(nameof(FriendCharacterID))]
	[Index(nameof(CharacterID), nameof(FriendCharacterID), IsUnique = true)]
	public class CharacterFriendEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public long FriendCharacterID { get; set; }
	}
}