using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Entities
{
    [Table("guild_info", Schema = "fishMMO")]
    [Index(nameof(Name))]
    public class GuildInfoEntity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public string Name { get; set; }
        public string Notice { get; set; }
        
        public List<CharacterGuildEntity> Characters { get; set; }
    }
}