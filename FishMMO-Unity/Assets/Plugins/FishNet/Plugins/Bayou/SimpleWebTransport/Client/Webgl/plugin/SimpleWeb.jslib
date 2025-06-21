// this will create a global object
const SimpleWeb = {
    webSockets: [],
    next: 1,
    GetWebSocket: function (index) {
        return SimpleWeb.webSockets[index]
    },
    AddNextSocket: function (webSocket) {
        var index = SimpleWeb.next;
        SimpleWeb.next++;
        SimpleWeb.webSockets[index] = webSocket;
        return index;
    },
    RemoveSocket: function (index) {
        SimpleWeb.webSockets[index] = undefined;
		SimpleWeb.stopPinging(index);
    },
    setupPinging: function(index) {
        const sendPing = function() {
            var webSocket = SimpleWeb.GetWebSocket(index);
            if (webSocket && webSocket.readyState === WebSocket.OPEN) {
                // Create a binary ping message
                var buffer = new ArrayBuffer(5); // 1 byte for PacketId + 4 bytes for timestamp
                var view = new DataView(buffer);
                view.setUint8(0, 14); // 14 is the PacketId for PingPong
                view.setUint32(1, Math.floor(Date.now() / 1000), true);
                webSocket.send(buffer);
            } else {
                SimpleWeb.stopPinging(index);
            }
        };

        document.addEventListener("visibilitychange", function() {
            if (document.hidden) {
                if (!SimpleWeb.pingIntervals[index]) {
                    SimpleWeb.pingIntervals[index] = setInterval(sendPing, 10000); // Every 10 seconds when hidden
                }
            } else {
                SimpleWeb.stopPinging(index);
            }
        });

        // Start pinging immediately if the tab is already hidden
        if (document.hidden) {
            SimpleWeb.pingIntervals[index] = setInterval(sendPing, 10000);
        }
    },
    stopPinging: function(index) {
        if (SimpleWeb.pingIntervals[index]) {
            clearInterval(SimpleWeb.pingIntervals[index]);
            delete SimpleWeb.pingIntervals[index];
        }
    }
};

function IsConnected(index) {
    var webSocket = SimpleWeb.GetWebSocket(index);
    if (webSocket) {
        return webSocket.readyState === webSocket.OPEN;
    }
    else {
        return false;
    }
}


function Connect(addressPtr, openCallbackPtr, closeCallBackPtr, messageCallbackPtr, errorCallbackPtr) {
    // fix for unity 2021 because unity bug in .jslib
    if (typeof Runtime === "undefined") {
        // if unity doesn't create Runtime, then make it here
        // dont ask why this works, just be happy that it does
        Runtime = {
            dynCall: dynCall
        }
    }

    const address = UTF8ToString(addressPtr);
    console.log("Connecting to " + address);
    // Create webSocket connection.
    webSocket = new WebSocket(address);
    webSocket.binaryType = 'arraybuffer';
    const index = SimpleWeb.AddNextSocket(webSocket);
	SimpleWeb.setupPinging(index);

    // Connection opened
    webSocket.addEventListener('open', function (event) {
        console.log("Connected to " + address);
        Runtime.dynCall('vi', openCallbackPtr, [index]);
    });
    webSocket.addEventListener('close', function (event) {
        console.log("Disconnected from " + address);
		SimpleWeb.stopPinging(index);
        Runtime.dynCall('vi', closeCallBackPtr, [index]);
    });

    // Listen for messages
    webSocket.addEventListener('message', function (event) {
        if (event.data instanceof ArrayBuffer) {
            // TODO dont alloc each time
            var array = new Uint8Array(event.data);
            var arrayLength = array.length;

            var bufferPtr = _malloc(arrayLength);
            var dataBuffer = new Uint8Array(HEAPU8.buffer, bufferPtr, arrayLength);
            dataBuffer.set(array);

            Runtime.dynCall('viii', messageCallbackPtr, [index, bufferPtr, arrayLength]);
            _free(bufferPtr);
        }
        else {
            console.error("message type not supported")
        }
    });

    webSocket.addEventListener('error', function (event) {
        console.error('Socket Error', event);

        Runtime.dynCall('vi', errorCallbackPtr, [index]);
    });

    return index;
}

function Disconnect(index) {
    var webSocket = SimpleWeb.GetWebSocket(index);
    if (webSocket) {
        webSocket.close(1000, "Disconnect");
    }

    SimpleWeb.RemoveSocket(index);
}

function Send(index, arrayPtr, offset, length) {
    var webSocket = SimpleWeb.GetWebSocket(index);
    if (webSocket) {
        const start = arrayPtr + offset;
        const end = start + length;
        const data = HEAPU8.buffer.slice(start, end);
        webSocket.send(data);
        return true;
    }
    return false;
}


const SimpleWebLib = {
    $SimpleWeb: SimpleWeb,
    IsConnected,
    Connect,
    Disconnect,
    Send
};
autoAddDeps(SimpleWebLib, '$SimpleWeb');
mergeInto(LibraryManager.library, SimpleWebLib);