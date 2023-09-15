using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO_DB.Entities
{
    [Table("character_buffs", Schema = "fish_mmo_postgresql")]
    [Index(nameof(CharacterId))]
    public class CharacterBuffEntity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public long CharacterId { get; set; }
        public CharacterEntity Character { get; set; }
        public int TemplateID { get; set; }
        public float RemainingTime { get; set; }
        public List<CharacterBuffEntity> Stacks { get; set; }
	}
}