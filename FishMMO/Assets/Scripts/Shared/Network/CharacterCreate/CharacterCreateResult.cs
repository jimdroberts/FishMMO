public enum CharacterCreateResult : byte
{
	Success = 0,
	TooMany = 1,
	InvalidCharacterName,
	CharacterNameTaken,
	InvalidSpawn,
}