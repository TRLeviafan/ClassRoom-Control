namespace ClassRoom_Control.Protocol;

public enum CommandType
{
    // Discovery
    Discover = 1,
    DiscoverAck = 2,

    // Registration
    Register = 10,
    RegisterAck = 11,

    // Heartbeat
    Heartbeat = 20,
    HeartbeatAck = 21,

    // Screen Demonstration
    StartDemo = 30,
    StopDemo = 31,

    // Input & Screen Locking
    LockInput = 40,
    UnlockInput = 41,
    LockScreen = 42,
    UnlockScreen = 43,

    // Remote System Controls
    Shutdown = 50,
    Restart = 51,

    // Messages & Alerts
    SendMessage = 60,
    Identify = 61,

    // Monitoring & Thumbnails
    RequestThumbnail = 70,
    ResponseThumbnail = 71,

    // File Transfer
    FileTransferOffer = 80
}