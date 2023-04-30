using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO_DB.Entities
{
    [Table("character_buffs", Schema = "fishMMO")]
    [Index(nameof(CharacterId))]
    [Index(nameof(CharacterId), nameof(Name), IsUnique = true)]
    public class CharacterBuffEntity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        // PRIMARY KEY (character, name) is created via db context
        public long CharacterId { get; set; }
        public CharacterEntity Character { get; set; }
        public string Name { get; set; }
        public int Level { get; set; }
        public float BuffTimeEnd { get; set; }
    }
}