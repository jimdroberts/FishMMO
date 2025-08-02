# FishMMO-Logger

FishMMO-Logger is a flexible, extensible logging library designed for use with FishMMO projects, but suitable for any .NET application. It supports multiple logging backends (such as file and email), configurable log levels, and easy integration with Unity or other .NET-based projects. The logger is built on .NET Standard 2.1 for broad compatibility.

## Features
- Multiple logger types: File, Email, Console, and more
- Configurable log levels (Info, Warning, Error, etc.)
- JSON-based configuration for easy setup
- Designed for integration with Unity projects
- Extensible with custom logger types

## Installation
1. Build the project using your preferred .NET build tool.
2. Reference the generated `FishMMO-Logger.dll` in your project (e.g., copy to your Unity project's `Assets/Dependencies` folder).


## Initialization Example
Below is an example of how to initialize the logging system at application startup, including custom logger factory registration:

```csharp
using FishMMO.Logging;
using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // (Optional) Register a custom logger factory before initialization
        Log.RegisterLoggerFactory("MyCustomLoggerConfig", (cfg, logCallback) => new MyCustomLogger((MyCustomLoggerConfig)cfg, logCallback));

        // Initialize logging from config file
        await Log.Initialize("logging.json");

        await Log.Info("Main", "Application started.");
        // ... rest of your application code ...
    }
}
```

## JSON Configuration Example
Below is an example `logging.json` configuration file:

```json
{
  "LogLevel": "Info", // Minimum log level: Trace, Debug, Info, Warning, Error, Critical
  "Loggers": [
    {
      "Type": "File",
      "Config": {
        "FilePath": "logs/app.log",
        "Append": true,
        "MaxFileSizeMB": 10
      }
    },
    {
      "Type": "Email",
      "Config": {
        "SmtpServer": "smtp.example.com",
        "Port": 587,
        "Username": "user@example.com",
        "Password": "yourpassword",
        "From": "logger@example.com",
        "To": "admin@example.com",
        "Subject": "FishMMO Log Alert"
      }
    }
  ]
}
```

### Configuration Options
- `LogLevel`: Sets the minimum level of messages to log.
- `Loggers`: Array of logger definitions. Each logger has a `Type` and a `Config` object.

#### File Logger Config
- `FilePath`: Path to the log file.
- `Append`: Whether to append to the file (true/false).
- `MaxFileSizeMB`: Maximum file size before rotation (optional).

#### Email Logger Config
- `SmtpServer`: SMTP server address.
- `Port`: SMTP server port.
- `Username`: SMTP username.
- `Password`: SMTP password.
- `From`: Sender email address.
- `To`: Recipient email address.
- `Subject`: Email subject line.

## Extending
You can add custom logger types by implementing the `ILogger` and `ILoggerConfig` interfaces.