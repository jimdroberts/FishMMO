using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Entities
{
    [Table("character_itemcooldowns", Schema = "fishMMO")]
    [Index(nameof(CharacterId))]
    [Index(nameof(CharacterId), nameof(Category), IsUnique = true)]
    public class CharacterItemCooldownEntity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public long CharacterId { get; set; }
        public CharacterEntity Character { get; set; }
        public string Category { get; set; }
        public float CooldownEnd { get; set; }
        
    }
}