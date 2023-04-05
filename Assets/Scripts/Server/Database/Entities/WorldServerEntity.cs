using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Entities
{
    [Table("world_servers", Schema = "fishMMO")]
    public class WorldServerEntity
    {
        [Key]
        public string Name { get; set; }
        public DateTime LastPulse { get; set; }
        public string Address { get; set; }
        public ushort Port { get; set; }
        public int CharacterCount { get; set; }
        public bool Locked { get; set; }
    }
}