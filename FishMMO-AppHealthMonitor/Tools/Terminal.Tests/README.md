# Desktop terminal integration checks

Run `dotnet run --project Tools/Terminal.Tests` from FishMMO-AppHealthMonitor in a
graphical Linux session. Konsole or another supported terminal must be installed.
The test opens short-lived verification windows, launches harmless shell/sleep
fixtures, and terminates only its own fixture processes. No FishMMO application
services or database operations are started.

Checks actual application PID ownership through the terminal handshake, literal
arguments, working directory, child-only environment, output/log forwarding,
headless bypass/file logging, monitor-owner death, timeout and cancellation cleanup.
