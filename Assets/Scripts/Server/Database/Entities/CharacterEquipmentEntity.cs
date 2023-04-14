using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Entities
{
    [Table("character_equipment", Schema = "fishMMO")]
    [Index(nameof(CharacterId))]
    [Index(nameof(CharacterId), nameof(Slot), IsUnique = true)]
    public class CharacterEquipmentEntity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public long CharacterId { get; set; }
        public CharacterEntity Character { get; set; }
        public long InstanceID { get; set; }
        public int TemplateID { get; set; }
        public int Seed { get; set; }
        public int Slot { get; set; }
        public string Name { get; set; }
        public int Amount { get; set; }
    }
}