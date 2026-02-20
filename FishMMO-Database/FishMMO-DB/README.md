# FishMMO-DB Environment Configuration

This library supports layered configuration in this order:

1. `appsettings.json` (required)
2. `appsettings.{Environment}.json` (optional)
3. Environment variables (highest priority)

Environment is resolved by your host/.NET configuration pipeline (for example via `DOTNET_ENVIRONMENT` or `ASPNETCORE_ENVIRONMENT`).

---

## Recommended appsettings files

Keep shared defaults in `appsettings.json`, and only override differences in environment files.

- `appsettings.Development.json`
- `appsettings.Production.json`

Example override file:

```json
{
  "Npgsql": {
    "Host": "127.0.0.1",
    "Database": "fish_mmo_postgresql_dev",
    "Username": "postgres",
    "Password": "dev_password"
  },
  "QueryPerformanceTracking": {
    "Enabled": true,
    "Level": "Basic"
  }
}
```

---

## Environment variables by OS

## Windows

### PowerShell (current session)

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
```

### CMD (current session)

```cmd
set DOTNET_ENVIRONMENT=Development
```

### Persist for future sessions

```powershell
setx DOTNET_ENVIRONMENT "Production"
```

Restart terminal (or app) after `setx`.

---

## CachyOS (Arch Linux, fish shell)

### Current shell session

```fish
set -x DOTNET_ENVIRONMENT Development
```

### Persist for your user (all future fish sessions)

```fish
set -Ux DOTNET_ENVIRONMENT Production
```

### Remove universal variable

```fish
set -eU DOTNET_ENVIRONMENT
```

---

## Ubuntu (Debian family)

### Current shell session (bash/zsh)

```bash
export DOTNET_ENVIRONMENT=Development
```

### Persist per-user (bash)

Add to `~/.bashrc`:

```bash
export DOTNET_ENVIRONMENT=Production
```

Then reload:

```bash
source ~/.bashrc
```

### Systemd service example

Use in service unit:

```ini
[Service]
Environment=DOTNET_ENVIRONMENT=Production
```

or use an env file:

```ini
[Service]
EnvironmentFile=/etc/fishmmo-db.env
```

with `/etc/fishmmo-db.env`:

```bash
DOTNET_ENVIRONMENT=Production
```

---

## Overriding individual settings via environment variables

Use double underscores (`__`) for nested keys:

- `Npgsql__Host`
- `Npgsql__Port`
- `Npgsql__Database`
- `Npgsql__Username`
- `Npgsql__Password`
- `Npgsql__CommandTimeout`

Example (fish):

```fish
set -x Npgsql__Host 10.0.0.25
set -x Npgsql__Database fish_mmo_postgresql
set -x Npgsql__Username postgres
set -x Npgsql__Password super_secret
```

---

## Database.cs setup examples

## 1) Build IConfiguration and pass it to Database

```csharp
using FishMMO.Database;
using Microsoft.Extensions.Configuration;

IConfiguration configuration = new ConfigurationBuilder()
	.SetBasePath("/opt/fishmmo/config")
	.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
	.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}.json", optional: true, reloadOnChange: false)
	.AddEnvironmentVariables()
	.Build();

IDatabase database = new Database(
	configuration,
	enableLogging: false,
	commandTimeout: 15,
	healthCheckWarningMs: 100,
	healthCheckCriticalMs: 500);
```

## 2) Normalize custom environment variable before building configuration

```csharp
string? fishEnv = Environment.GetEnvironmentVariable("FISHMMO_ENVIRONMENT");
if (!string.IsNullOrWhiteSpace(fishEnv))
{
	Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", fishEnv);
	Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", fishEnv);
}
```

---

## Npgsql service usage examples

Services are resolved from `database.ServiceRegistry` by interface.

## Account service

```csharp
using FishMMO.Database.Npgsql.Services.Interfaces;

if (!database.ServiceRegistry.TryGet<IAccountService>(out var accountService))
	throw new InvalidOperationException("IAccountService not registered.");

var loginResult = await accountService.FetchForLoginAsync("myAccount", cancellationToken);
if (!loginResult.IsSuccess)
{
	Console.WriteLine($"Login lookup failed: {loginResult.ErrorCode} - {loginResult.ErrorMessage}");
	return;
}

var account = loginResult.Data;
```

## Character service

```csharp
using FishMMO.Database.Npgsql.Services.Interfaces;

if (!database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
	throw new InvalidOperationException("ICharacterService not registered.");

var characterResult = await characterService.FetchByAccountAsync("myAccount", cancellationToken);
if (!characterResult.IsSuccess)
{
	Console.WriteLine($"Character fetch failed: {characterResult.ErrorCode} - {characterResult.ErrorMessage}");
	return;
}

var character = characterResult.Data;
```

## Chat service

```csharp
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Database.Data.Enums;

if (!database.ServiceRegistry.TryGet<IChatService>(out var chatService))
	throw new InvalidOperationException("IChatService not registered.");

var persist = await chatService.PersistAsync(
	characterId: 123,
	characterName: "Ari",
	accountName: "myAccount",
	worldServerId: 1,
	sceneServerId: 10,
	channel: ChatChannel.World,
	message: "Hello world",
	serverReceivedTime: DateTime.UtcNow,
	cancellationToken: cancellationToken);

if (!persist.IsSuccess)
	Console.WriteLine($"Chat persist failed: {persist.ErrorCode} - {persist.ErrorMessage}");
```

---

## Optional: explicit environment selection in code

If you need direct factory creation, pass `IConfiguration` into `NpgsqlDbConfiguration`:

```csharp
using FishMMO.Database.Npgsql;
using Microsoft.Extensions.Configuration;

var rootConfiguration = new ConfigurationBuilder()
	.SetBasePath("/opt/fishmmo/config")
	.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
	.AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: false)
	.AddEnvironmentVariables()
	.Build();

var config = new NpgsqlDbConfiguration(
	rootConfiguration,
	enableLogging: false,
	commandTimeoutOverride: null);

var factory = new NpgsqlDbContextFactory(config);
```

---

## Notes

- Prefer `FISHMMO_ENVIRONMENT` for environment configuration.
- Keep secrets out of source control; use environment variables or secret stores.
- In production, set `enableLogging: false` to avoid sensitive data logging.