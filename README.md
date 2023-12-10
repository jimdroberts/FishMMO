# FishMMO
FishNetworking MMO Template

[![](https://dcbadge.vercel.app/api/server/9JQEYjkSNk)](https://discord.gg/9JQEYjkSNk)

INITIAL SETUP INSTRUCTIONS

### Windows

1) Download / Clone Project into your choosen directory (https://github.com/jimdroberts/FishMMO.git)
2) Clone - CD into your directory, Open CMD of your choice, paste "git clone https://github.com/jimdroberts/FishMMO.git"
3) If Dev branch is ahead of main, Please checkout project to Dev Branch!
4) Open Unity Hub, Click "ADD", Enter "FishMMO" Directory, Select "FishMMO-Unity"
5) Open "\FishMMO\FishMMO-Dependencies" Directory, Open "FishMMO-Dependencies.sln" , Right click solution on right side menu, "Clean", Right click solution, Click "Build"
6) Open "\FishMMO\FishMMO-Database" Directory, Open "FishMMO-Database.sln" , Right click solution on right side menu, "Clean", Right click solution, Click "Build"
7) Go back to your open project, You should now see a "FishMMO" Menu up top. Click "FishMMO"
8) Select "Build", Select "Installer", Select "\FishMMO" Directory, Save it there.
9) Select "Build", Select "Server", Select "Windows x64-All In One", Select "\FishMMO" Directory to save.
10) Open the location of where you saved the "Installer". EX: "\FishMMO\FishMMO-Unity\Installer Windows"
11) Ensure WSL, DOTNET 7, DOTNET-EF, Docker and your Initial Migration are all installed by launching the "Installer.exe". Select "Everything Option"
12) Configure LoginServer.cfg, WorldServer.cfg, SceneServer.cfg, and appsettings.json in the builds Data folder.

### Launching the Server

1) Navigate to "\FishMMO\All-In-One Windows" Or where you have chosen to install your "Windows x64-All-In-One".
2) Launch "Start.Bat"

### Video of Installation
https://drive.google.com/file/d/11FFrECZSh_zW9JVKZk7lR0xUYHBwVZAR/view?usp=sharing

#### Note
If you want to use a different directory name than **FishMMO** please adjust FishMMO-DB.csproj and FishMMO-Utils.csproj.

     ...\FishMMO\Assets\Plugins\(FishMMO-Database or FishMMO-Dependencies)

You can rename the folder path to fit your directory structure.

### FishMMO (Unity)

FishMMO will build your project for you.
Click any of the Server or Client build types and select the output folder.
All configuration files will be copied over automatically from the root project directory.

![buildgame](https://user-images.githubusercontent.com/19621936/233815094-711358a3-ca4b-44c4-84ea-b2c56b771c56.png)


### Adding Scenes

If you would like to Add new WorldScenes to your project simply place them in the /Scenes/WorldScenes/ directory.

To Bake Initial Spawn Positions, Respawn Positions, and/or Teleporters into your new scenes open the
WorldSceneDetails asset which is located in /Resources/Prefabs/Shared/ and press the Rebuild button.

![worldscenedetails](https://user-images.githubusercontent.com/19621936/233815140-ce430187-a1cf-4ca1-8c9c-e4ff579af223.png)