const SimpleWebRTC = {
   peers: [],
   next: 1,
   GetPeer: function (index) {
      return SimpleWebRTC.peers[index];
   },
   AddNextPeer: function (peer) {
      let index = SimpleWebRTC.next;
      SimpleWebRTC.next++;
      SimpleWebRTC.peers[index] = peer;
      return index;
   },
   RemovePeer: function (index) {
      SimpleWebRTC.peers[index] = undefined;
   },
};

function IsConnectedRTC(index) {
   let peer = SimpleWebRTC.GetPeer(index);
   if (peer) {
      return peer.readyState === peer.OPEN;
   } else {
      return false;
   }
}

function ConnectRTC(
   addressPtr,
   iceServersPtr,
   openCallbackPtr,
   closeCallBackPtr,
   messageCallbackPtr,
   errorCallbackPtr
) {
   // Fix for unity 2021 because unity bug in .jslib
   if (typeof Runtime === "undefined") {
      // If unity doesn't create Runtime, then make it here
      // don't ask why this works, just be happy that it does
      Runtime = {
         dynCall: dynCall,
      };
   }

   const fetchTimeout = 5000;

   let offerAddress = UTF8ToString(addressPtr);
   console.log("Connecting to " + offerAddress);
   offerAddress += "offer/";
   let answerAddress = UTF8ToString(addressPtr);
   answerAddress += "answer/";

	window.candidates = []

   // Create the connection
   const iceServers = UTF8ToString(iceServersPtr)
      .split(";;")
      .map((s) => {
         const props = s.split("__");

         if (props.length > 1) {
            return {
               urls: props[0],
               username: props[1],
               credential: props[2],
            };
         } else {
            return {
               urls: props[0],
            };
         }
      });
	console.log("Using the following ICE servers: " + JSON.stringify(iceServers, null, 3))
   peerConnection = new RTCPeerConnection({
      iceServers: iceServers
   });

   peerConnection.addEventListener("connectionstatechange", (event) => {
      if (peerConnection.connectionState === "connected") {
         console.log("Connected to " + addressPtr);
         // We don't trigger the connected callback here because we still need the data channels to be ready
      } else if (peerConnection.connectionState === "closed") {
         console.log("Disconnected from " + addressPtr);
         Runtime.dynCall("vi", closeCallBackPtr, [index]);
      } else if (peerConnection.connectionState === "failed") {
         console.error("WebRTC PeerConnection error");
         Runtime.dynCall("vi", errorCallbackPtr, [index]);
      }
   });

   peerConnection.addEventListener("icecandidate", (e) => {
      window.candidates.push(e.candidate);
      console.log("icecandidate " + JSON.stringify(e.candidate));
   });
   peerConnection.addEventListener("negotiationneeded", (e) => {
      console.log("negotiationneeded! create offer...");
   });
   peerConnection.addEventListener("icegatheringstatechange", (e) => {
      console.log(
         "icegatheringstatechange " + peerConnection.iceGatheringState
      );
   });
   peerConnection.addEventListener("iceconnectionstatechange", (e) => {
      console.log(
         "iceconnectionstatechange " + peerConnection.iceConnectionState
      );
   });
   peerConnection.addEventListener("icecandidateerror", (e) => {
      console.log(
         "icecandidateerror " + e.url + " " + e.errorCode + " " + e.errorText
      );
   });
   peerConnection.addEventListener("signalingstatechange", (e) => {
      console.log("signalingstatechange " + peerConnection.signalingState);
   });

   //

   const index = SimpleWebRTC.AddNextPeer(peerConnection);

   const fetchError = (e) => {
      console.error("Fetch error: " + e);
      setTimeout(() => {
         Runtime.dynCall("vi", errorCallbackPtr, [index]);
      }, 100);
   };

   try {
      const offerTimeout = setTimeout(() => {
         Runtime.dynCall("vi", errorCallbackPtr, [index]);
      }, fetchTimeout);
      fetch(offerAddress, {
         method: "GET",
         headers: {
            "Content-Type": "application/json",
         },
      })
         .then(function (response) {
            clearTimeout(offerTimeout);
            return response.json();
         })
         .then(function (offer) {
            // Answer after offer

            const connId = offer.connId;
            peerConnection
               .setRemoteDescription({ type: "offer", sdp: offer.sdp })
               .then(function () {
                  peerConnection.createAnswer().then(function (answer) {
                     peerConnection
                        .setLocalDescription(answer)
                        .then(function () {
                           setTimeout(() => {
                              const answerTimeout = setTimeout(() => {
                                 Runtime.dynCall("vi", errorCallbackPtr, [
                                    index,
                                 ]);
                              }, fetchTimeout);

                              fetch(answerAddress, {
                                 method: "POST",
                                 headers: {
                                    "Content-Type": "application/json",
                                 },
                                 body: JSON.stringify({
                                    connId: connId,
                                    sdp: answer.sdp,
                                    candidates: window.candidates.filter(c => c).map(
                                       (c) => c.candidate
                                    ),
                                 }),
                              })
                                 .then(function (response) {
                                    clearTimeout(answerTimeout);
                                    return response.json();
                                 })
                                 .then((obj) => {
                                    if (obj.candidates.length === 0) {
                                       Runtime.dynCall("vi", errorCallbackPtr, [
                                          index,
                                       ]);
                                       return console.error(
                                          "No ICE candidates found in the server"
                                       );
                                    }

                                    obj.candidates.forEach((c) => {
                                       peerConnection.addIceCandidate({
                                          candidate: c,
                                          sdpMid: "0",
                                          sdpMLineIndex: 0,
                                       });
                                    });
                                    // End of candidates
                                    peerConnection.addIceCandidate();
                                    console.log("Got remote candidates");
                                 })
                                 .catch((e) => {
                                    fetchError(e);
                                 });
                           }, 1000); // Wait 1 second to gather candidates
                        });
                  });
               });

            // Setup data channel

            let channels = [];
            peerConnection.ondatachannel = function (event) {
               const dataChannel = event.channel;

               if (dataChannel.label === "Reliable") {
                  peerConnection.reliableChannel = dataChannel;
                  channels.push(dataChannel.label);
               } else if (dataChannel.label === "Unreliable") {
                  peerConnection.unreliableChannel = dataChannel;
                  channels.push(dataChannel.label);
               }

               if (channels.length == 2) {
                  // All channels open and ready, trigger connected callback
                  Runtime.dynCall("vi", openCallbackPtr, [index]);
               }

               dataChannel.addEventListener("error", (ev) => {
                  const err = ev.error;
                  console.error(
                     "WebRTC " + dataChannel.label + " DataChannel error: ",
                     err.message
                  );
                  Runtime.dynCall("vi", errorCallbackPtr, [index]);
               });

               dataChannel.addEventListener("message", function (event) {
                  if (event.data instanceof ArrayBuffer) {
                     // TODO: Don't allocate each time
                     let array = new Uint8Array(event.data);
                     let arrayLength = array.length;

                     let bufferPtr = _malloc(arrayLength);
                     let dataBuffer = new Uint8Array(
                        HEAPU8.buffer,
                        bufferPtr,
                        arrayLength
                     );
                     dataBuffer.set(array);

                     Runtime.dynCall("viii", messageCallbackPtr, [
                        index,
                        bufferPtr,
                        arrayLength,
                     ]);
                     _free(bufferPtr);
                  } else {
                     console.error("Message type not supported");
                  }
               });
            };
         })
         .catch((e) => {
            fetchError(e);
         });
   } catch (e) {
      console.error(e);
      setTimeout(() => {
         Runtime.dynCall("vi", errorCallbackPtr, [index]);
      }, 100);
   }

   return index;
}

function DisconnectRTC(index) {
   let peer = SimpleWebRTC.GetPeer(index);
   if (peer) peer.close();

   SimpleWebRTC.RemovePeer(index);
}

function SendRTC(index, arrayPtr, offset, length, deliveryMethod) {
   let peer = SimpleWebRTC.GetPeer(index);
   if (peer) {
      const start = arrayPtr + offset;
      const end = start + length;
      const data = HEAPU8.buffer.slice(start, end);

      if (deliveryMethod === 4) peer.unreliableChannel.send(data);
      else peer.reliableChannel.send(data);
      return true;
   }
   return false;
}

const SimpleWebRTCLib = {
   $SimpleWebRTC: SimpleWebRTC,
   IsConnectedRTC,
   ConnectRTC,
   DisconnectRTC,
   SendRTC,
};
autoAddDeps(SimpleWebRTCLib, "$SimpleWebRTC");
mergeInto(LibraryManager.library, SimpleWebRTCLib);
