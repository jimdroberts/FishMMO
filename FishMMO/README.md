# FishMMO
FishNetworking MMO Template

Discord: https://discord.gg/9JQEYjkSNk

INITIAL SETUP INSTRUCTIONS

-Unity3D-
1) Create a NEW Unity3D URP project named FishMMO.
2) Copy your new FishMMO Unity3D project to the root repository directory.


-Windows-
1) Launch WindowsSetup.bat as Administrator.
2) Ensure WSL, DOTNET 7, DOTNET-EF, and Docker are all installed.
3) Configure the Database.cfg file in \FishMMO\
4) Create a docker container and an initial migration.
5) Build the FishMMO-DB project. The FishMMO-DB.dll should be automatically copied to your FishMMO/Assets/Plugins/Database directory.

-Note- If you want to use a different directory name than FishMMO please adjust FishMMO-DB.csproj.
               Modify <TargetDir>....\FishMMO\Assets\Plugins\Database</TargetDir> to fit your directory structure


-FishMMO Project-
FishMMO will build your project for you.
Click any of the Server or Client build types and select the output folder.
All configuration files will be copied over automatically from the root project directory.

![buildgame](https://user-images.githubusercontent.com/19621936/233815094-711358a3-ca4b-44c4-84ea-b2c56b771c56.png)


-Configuration-
Configure LoginServer.cfg, WorldServer.cfg, and SceneServer.cfg in the builds Data folder.
Ensure your SceneServer.cfg RelayAddress and RelayPort are pointing to the WorldServer.cfg address and port.
If you would like to Add new WorldScenes to your project simply place them in the /Scenes/WorldScenes/ directory.

To Rebuild your new scenes InitialSpawnPositions, RespawnPositions, and/or Teleporters open the
WorldSceneDetails asset which is located in /Resources/Prefabs/Shared/ and press the Rebuild button.

![worldscenedetails](https://user-images.githubusercontent.com/19621936/233815140-ce430187-a1cf-4ca1-8c9c-e4ff579af223.png)

To start the server RUN Windows:START.bat or Linux:START.SH in the server build root directory to automatically launch the servers.