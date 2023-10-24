public enum ClientAuthenticationResult : byte
{
	AccountCreated,
	SrpVerify,
	SrpProof,
	InvalidUsernameOrPassword,
	AlreadyOnline,
	Banned,
	LoginSuccess,
	WorldLoginSuccess,
	SceneLoginSuccess,
	ServerFull,
}