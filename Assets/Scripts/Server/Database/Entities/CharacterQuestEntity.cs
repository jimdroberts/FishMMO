using System;
using SQLite;

namespace Server.Entities
{
    
    //[Table("account", Schema = "mmo")]
    [Table("character_quests")]
    public class CharacterQuestEntity
    {
        public string character { get; set; }
        public string name { get; set; }
        public int progress { get; set; }
        public bool completed { get; set; }
        // PRIMARY KEY (character, name) is created manually.
    }
}