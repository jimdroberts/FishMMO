using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO_DB.Entities
{
    [Table("character_skills", Schema = "fishMMO")]
    [Index(nameof(CharacterId))]
    [Index(nameof(CharacterId), nameof(Hash), IsUnique = true)]
    public class CharacterSkillEntity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public long CharacterId { get; set; }
        public CharacterEntity Character { get; set; }
        public int Hash { get; set; }
        public int Level { get; set; }
        public float CastTimeEnd { get; set; }
        public float CooldownEnd { get; set; }
    }
}