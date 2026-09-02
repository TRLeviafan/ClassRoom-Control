using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ClassRoom_Control.Services.Common;

public static class FileTransferService
{
    public static async Task SendFileAsync(string filePath, int port, CancellationToken token = default)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        try
        {
            listener.Start();
            using var client = await listener.AcceptTcpClientAsync(token);
            using var networkStream = client.GetStream();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            
            await fileStream.CopyToAsync(networkStream, 81920, token);
            await networkStream.FlushAsync(token);
        }
        finally
        {
            listener.Stop();
        }
    }

    public static async Task ReceiveFileAsync(string savePath, string hostIp, int port, CancellationToken token = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(hostIp, port, token);
        
        using var networkStream = client.GetStream();
        using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);
        
        await networkStream.CopyToAsync(fileStream, 81920, token);
        await fileStream.FlushAsync(token);
    }
}
