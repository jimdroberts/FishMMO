WebRTC transport plugin for [FishNetworking](https://github.com/FirstGearGames/FishNet)

You can now use UDP in the browser with FishNetworking!

Both `Reliable` and `Unreliable` channels are supported by this transport

Based on [Bayou](https://github.com/FirstGearGames/Bayou) and [SimpleWebTransport](https://github.com/James-Frowen/SimpleWebTransport)

Limitations:
- Clients cannot be tested in Unity Editor, **it only works in WebGL builds**
    - This is because the WebRTC client is built with JavaScript and is not implemented for other platforms
- Only WebGL is supported for clients
  - You can use UDP on other platforms with a Multipass transport
- There's no P2P support and host mode doesn't work
- HTTPS support for the initial connection (signaling) to the server is not implemented
  - You can still use HTTPS with a reverse proxy
- The server can be built for most platforms but only Linux Dedicated Server build has been tested

Installation: 
- Add `https://github.com/Unity-Technologies/com.unity.webrtc.git#3.0.0-pre.5` using the Package Manager (Add from git URL)
  - For now only `3.0.0-pre.5` is supported
- Add `ENABLE_WEBRTC` to define symbols in Player settings
- Copy this folder to `Assets/FishNet/Runtime/Transporting/Transports/`
- Modify the assembly definition `Assets/FishNet/Runtime/FishNet.Runtime.asmdef` to include the `SimpleWebRTC` assembly:
<img width="340" alt="example_assembly" src="https://user-images.githubusercontent.com/7516142/233031099-2ab4053f-e0c2-4191-8ceb-73f9fbaa1811.png">

- Add the `FishyWebRTC` transport component and use it in your `TransportManager`:
<img width="327" alt="example_component" src="https://user-images.githubusercontent.com/7516142/233031818-7ad0c782-9b98-4f0a-896a-8690de758f9b.png">

