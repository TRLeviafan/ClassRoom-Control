namespace ClassRoom_Control.Protocol;

public static class NetworkConstants
{
    // Ports
    public const int DiscoveryPort = 5555;
    public const int CommandPort = 9100;
    public const int VideoPort = 9200;
    public const int AudioPort = 9201;

    // Multicast Group IP
    public const string MulticastAddress = "239.0.0.1";
    public const string MulticastAudioAddress = "239.0.0.2";

    // Timing
    public const int HeartbeatIntervalSeconds = 3;
    public const int DisconnectTimeoutSeconds = 10;
    public const int DiscoveryIntervalSeconds = 2;
}