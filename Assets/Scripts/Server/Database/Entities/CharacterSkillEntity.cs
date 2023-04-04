using SQLite;

namespace Server.Entities
{
    [Table("character_skills")]
    public class CharacterSkillEntity
    {
        public string character { get; set; }
        public int hash { get; set; }
        public int level { get; set; }
        public float castTimeEnd { get; set; }
        public float cooldownEnd { get; set; }
        // PRIMARY KEY (character, name) is created manually.
    }
}