# FishMMO
FishNetworking MMO Template

INITIAL SETUP INSTRUCTIONS

-IMPORTANT-
If you're on a Windows Operating System please install WSL2.

1) Launch Command Prompt as Administrator

2) RUN wsl.exe --install

Install Docker. This will make Postgresql database setup significantly easier!

Docker: https://docs.docker.com/engine/install/

If you don't want to install Docker you can manually install Postgresql Server.

Postgresql: https://www.postgresql.org/download/

Download the source files from the Github Repository.

Github: https://github.com/jimdroberts/FishMMO/

Create a NEW Unity3D URP Project with any name.

Copy the root repository directory to the root project directory and replace all files if any.

FishMMO will build your project for you.
Click any of the Server or Client build types and select the output folder.
All configuration files will be copied over automatically from the root project directory.

You can also automatically set up the Postgresql Docker Database if you did not manually install Postgresql Server.
Remember to configure PostgresqlSetup.cfg beforehand.

![buildgame](https://user-images.githubusercontent.com/19621936/233815094-711358a3-ca4b-44c4-84ea-b2c56b771c56.png)

Configure LoginServer.cfg, WorldServer.cfg, and SceneServer.cfg in the builds Data folder.
Ensure your SceneServer.cfg RelayAddress and RelayPort are pointing to the WorldServer.cfg address and port.
If you would like to Add new WorldScenes to your project simply place them in the /Scenes/WorldScenes/ directory.

To Rebuild your new scenes InitialSpawnPositions, RespawnPositions, and/or Teleporters open the
WorldSceneDetails asset which is located in /Resources/Prefabs/Shared/ and press the Rebuild button.

![worldscenedetails](https://user-images.githubusercontent.com/19621936/233815140-ce430187-a1cf-4ca1-8c9c-e4ff579af223.png)

To start the server RUN Windows:START.bat or Linux:START.SH in the server build root directory to automatically launch the servers.
