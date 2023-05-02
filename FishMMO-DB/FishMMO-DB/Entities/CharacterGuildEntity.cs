using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO_DB.Entities
{
    [Table("character_guild", Schema = "fish_mmo_postgresql")]
    // add index on guild to avoid full scans when loading guild members
    [Index(nameof(CharacterId))]
    [Index(nameof(GuildId))]
    [Index(nameof(CharacterId), nameof(GuildId), IsUnique = true)]
    public class CharacterGuildEntity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public long CharacterId { get; set; }
        public CharacterEntity Character { get; set; }
        public long GuildId { get; set; }
        public GuildInfoEntity Guild { get; set; }
        public int Rank { get; set; }
    }
}