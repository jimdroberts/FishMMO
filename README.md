[![](https://dcbadge.vercel.app/api/server/9JQEYjkSNk?style=full)](https://discord.gg/9JQEYjkSNk)
[Join our Discord](https://discord.gg/9JQEYjkSNk)

# **FishMMO Installation Guide (2025)**

This guide provides step-by-step instructions for setting up and building the FishMMO project.

---

## **1. Clone the FishMMO Repository**

First, download or clone the project repository. It is recommended to use the **`dev`** branch if it is more up-to-date than **`main`**.

**Repository URL:**
```shell
https://github.com/jimdroberts/FishMMO.git
```

---

## **2. Build the FishMMO-Dependencies Project**

Navigate to the **`FishMMO/FishMMO-Dependencies`** directory and build the solution.

1.  Open **`FishMMO-Dependencies.sln`**.
2.  In the Solution Explorer, right-click the solution and select **Clean**.
3.  Right-click the solution again and select **Build**.

---

## **3. Build the FishMMO-Database Project**

Next, build the database project, which is essential for the server components.

1.  Go to **`FishMMO/FishMMO-Database`**.
2.  Open **`FishMMO-Database.sln`**.
3.  In the Solution Explorer, right-click the solution and select **Clean**.
4.  Right-click the solution again and select **Build**.

---

## **4. Configure Unity Hub**

Configure Unity Hub to work with the FishMMO project and its required modules.

1.  **Add the Project:**
    - Click the **ADD** button in Unity Hub.
    - Select the **`FishMMO-Unity`** directory from your cloned repository.
2.  **Install Required Modules:**
    - Go to the **Installs** tab.
    - Click the gear icon next to your preferred Unity version and select **Add Modules**.
    - Ensure the following modules are installed:
        - **Linux Build Support (IL2CPP and Mono)**
        - **Linux Dedicated Server Build Support**
        - **Mac Build Support**
        - **WebGL Build Support**
        - **Windows Build Support (IL2CPP)**
        - **Windows Dedicated Server Build Support**

---

## **5. Open the FishMMO-Unity Project**

Launch the **`FishMMO-Unity`** project from the Unity Hub.

---

## **6. Build the FishMMO Database Installer**

The Database Installer automates the setup of the backend infrastructure.

1.  In the Unity Editor, navigate to the menu:  
    **`FishMMO > Build > Operating System > Database Installer`**
2.  Build the Database Installer executable.

---

## **7. Run the Database Installer**

Execute the installer to set up the necessary tools and the database.

1.  Run the **`Database Installer.exe`** you just built.
2.  The installer will guide you through the process, automatically checking for and installing missing dependencies such as:
    - **.NET 8 SDK**
    - **Visual Studio Build Tools** (with .NET Desktop, C++ workloads, MSVC compiler, and Windows 10 SDK)
    - **NGINX** (optional)
    - **PostgreSQL**
    - **The FishMMO Database** (including EFCore migrations)
3.  Follow the on-screen prompts to complete the installation.

---

## **Miscellaneous**

### **Build World Scene Details**
This process caches important game world details for clients and servers. It should be run whenever you add a new scene to your project.

**Unity Menu:**  
`FishMMO/Build/Rebuild World Scene Details`

---

### **FishMMO Builds**
Use the custom build menu in Unity to create clients, servers, Addressables, and the database installer.

**Environments:**
- **Development:** Uses `127.0.0.1` for loopback addresses in configuration files.
- **Release:** Uses `0.0.0.0` to bind to all available network interfaces.

---

### **Database Setup**
The Database Installer provides options for managing your database, including creating new migrations, adding user permissions, and deleting the database. FishMMO uses EFCore for migrations. After the initial migration is created during installation, you are ready to begin development.

---

### **Versioning**
This menu helps you manage the project's versioning system.

**Access:**  
`FishMMO/Version/`

**Options:**
- `Increment Major`: Increases the major version number.
- `Increment Minor`: Increases the minor version number.
- `Increment Patch`: Increases the patch version number.
- `Reset Version`: Resets all version fields to zero.

Each action updates **`VersionConfig.asset`** and Unity's bundle version. The final version is written to **`version.txt`** in the build output directory.

**Optional:**  
You can enable automatic patch version increments by uncommenting the `UpdateBuildVersion()` call in `OnPostprocessBuild`.

---

### **DotNetBuilder**
A utility class that automates .NET build, publish, and migration tasks for FishMMO servers and tools. It integrates with the installer to streamline backend setup and handles process execution and error logging for the .NET CLI.

---

### **DotNetBuilder Profiles**
Profiles define build and publish settings for different environments (e.g., Development, Production). They ensure consistent and repeatable builds by specifying configurations like target framework, output directory, and runtime identifier.

---

### **PatchGenerator**
A custom Unity Editor window for creating and managing game patches.

**Access:**  
`FishMMO/Patch/Patch Generator`

**Usage:**
1.  Select the new and old build directories.
2.  Configure options, exclusions, and version details.
3.  Click **Generate Patch** to create delta files and manifests.

This tool is useful for distributing incremental updates to players. You can then build an ASP.NET Patch server and point it to the directory containing your generated patch files.

---

### **Server Setup**

Configure the following files in the builds directory:
- `LoginServer.cfg`
- `WorldServer.cfg`
- `SceneServer.cfg`
- `logging.json`
- `appsettings.json`

---

### **Launching the Servers**

Build the **FishMMO-AppHealthMonitor** application and configure the `appsetting.json`:

```json
{
  "Applications": [
    {
      "Name": "LoginServer",
      "ApplicationExePath": "path\\to\\your\\FishMMO GameServer Windows\\GameServer.exe",
      "MonitoredPort": 7770, // This Port should match your LoginServer.cfg port
      "PortTypes": [ "TCP", "UDP" ],
      "LaunchArguments": "LOGIN",
      "CheckIntervalSeconds": 30,
      "LaunchDelaySeconds": 2,
      "CpuThresholdPercent": 0,
      "MemoryThresholdMB": 0,
      "GracefulShutdownTimeoutSeconds": 10,
      "InitialRestartDelaySeconds": 5,
      "MaxRestartDelaySeconds": 60,
      "MaxRestartAttempts": 5,
      "CircuitBreakerFailureThreshold": 3,
      "CircuitBreakerResetTimeoutMinutes": 5
    },
    {
      "Name": "WorldServer",
      "ApplicationExePath": "path\\to\\your\\FishMMO GameServer Windows\\GameServer.exe",
      "MonitoredPort": 7780, // This Port should match your WorldServer.cfg port
      "PortTypes": [ "TCP", "UDP" ],
      "LaunchArguments": "WORLD",
      "CheckIntervalSeconds": 30,
      "LaunchDelaySeconds": 2,
      "CpuThresholdPercent": 0,
      "MemoryThresholdMB": 0,
      "GracefulShutdownTimeoutSeconds": 10,
      "InitialRestartDelaySeconds": 5,
      "MaxRestartDelaySeconds": 60,
      "MaxRestartAttempts": 5,
      "CircuitBreakerFailureThreshold": 3,
      "CircuitBreakerResetTimeoutMinutes": 5
    },
    {
      "Name": "SceneServer",
      "ApplicationExePath": "path\\to\\your\\FishMMO GameServer Windows\\GameServer.exe",
      "MonitoredPort": 7781, // This Port should match your SceneServer.cfg port
      "PortTypes": [ "TCP", "UDP" ],
      "LaunchArguments": "SCENE",
      "CheckIntervalSeconds": 30,
      "LaunchDelaySeconds": 2,
      "CpuThresholdPercent": 0,
      "MemoryThresholdMB": 0,
      "GracefulShutdownTimeoutSeconds": 10,
      "InitialRestartDelaySeconds": 5,
      "MaxRestartDelaySeconds": 60,
      "MaxRestartAttempts": 5,
      "CircuitBreakerFailureThreshold": 3,
      "CircuitBreakerResetTimeoutMinutes": 5
    },
    {
      "Name": "IPFetch Server",
      "ApplicationExePath": "path\\to\\your\\IPFetch Server\\IpFetchServer.exe",
      "MonitoredPort": 0,
      "PortTypes": [ "None" ],
      "LaunchArguments": "",
      "CheckIntervalSeconds": 30,
      "LaunchDelaySeconds": 2,
      "CpuThresholdPercent": 0,
      "MemoryThresholdMB": 0,
      "GracefulShutdownTimeoutSeconds": 10,
      "InitialRestartDelaySeconds": 5,
      "MaxRestartDelaySeconds": 60,
      "MaxRestartAttempts": 5,
      "CircuitBreakerFailureThreshold": 3,
      "CircuitBreakerResetTimeoutMinutes": 5
    }
  ]
}
```

Launch the **AppHealthMonitor.exe**.

---

**Enjoy building with FishMMO!**
