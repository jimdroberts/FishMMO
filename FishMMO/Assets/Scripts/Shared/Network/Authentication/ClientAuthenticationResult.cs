public enum ClientAuthenticationResult : byte
{
	InvalidUsernameOrPassword,
	AlreadyOnline,
	Banned,
	LoginSuccess,
	WorldLoginSuccess,
	SceneLoginSuccess,
	ServerFull,
}