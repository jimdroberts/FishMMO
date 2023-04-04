using SQLite;

namespace Server.Entities
{
    [Table("character_buffs")]
    public class CharacterBuffEntity
    {
        public string character { get; set; }
        public string name { get; set; }
        public int level { get; set; }
        public float buffTimeEnd { get; set; }
        // PRIMARY KEY (character, name) is created manually.
    }
}