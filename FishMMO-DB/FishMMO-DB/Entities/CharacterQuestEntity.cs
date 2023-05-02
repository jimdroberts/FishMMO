using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO_DB.Entities
{
    [Table("character_quests", Schema = "fish_mmo_postgresql")]
    [Index(nameof(CharacterId))]
    [Index(nameof(CharacterId), nameof(Name), IsUnique = true)]
    public class CharacterQuestEntity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public long CharacterId { get; set; }
        public CharacterEntity Character { get; set; }
        public string Name { get; set; }
        public int Progress { get; set; }
        public bool Completed { get; set; }
        // PRIMARY KEY (character, name) is created manually.
    }
}