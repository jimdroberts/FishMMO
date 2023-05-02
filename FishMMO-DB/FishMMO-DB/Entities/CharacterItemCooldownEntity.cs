using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO_DB.Entities
{
    [Table("character_itemcooldowns", Schema = "fish_mmo_postgresql")]
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