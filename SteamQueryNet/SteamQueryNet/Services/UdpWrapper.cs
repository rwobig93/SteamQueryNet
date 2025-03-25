using SteamQueryNet.Interfaces;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SteamQueryNet.Services
{
    internal sealed class UdpWrapper : IUdpClient
    {
        private readonly UdpClient _udpClient;
        private readonly int _sendTimeout;
        private readonly int _receiveTimeout;

        public UdpWrapper(IPEndPoint localIpEndPoint, int sendTimeout, int receiveTimeout)
        {
            _udpClient = new UdpClient(localIpEndPoint);
            _sendTimeout = sendTimeout;
            _receiveTimeout = receiveTimeout;
        }

        public bool IsConnected => _udpClient.Client.Connected;

        public void Close()
        {
            _udpClient.Close();
        }

        public void Connect(IPEndPoint remoteIpEndpoint)
        {
            _udpClient.Connect(remoteIpEndpoint);
        }

        public void Dispose()
        {
            _udpClient.Dispose();
        }

        public Task<UdpReceiveResult> ReceiveAsync()
        {
            var asyncResult = _udpClient.BeginReceive(null, null);
            asyncResult.AsyncWaitHandle.WaitOne(_receiveTimeout);
            if (!asyncResult.IsCompleted) throw new TimeoutException();
            
            IPEndPoint remoteEndpoint = null;
            var receivedData = _udpClient.EndReceive(asyncResult, ref remoteEndpoint);
            return Task.FromResult(new UdpReceiveResult(receivedData, remoteEndpoint!));
        }

        public Task<int> SendAsync(byte[] datagram, int bytes)
        {
            var asyncResult = _udpClient.BeginSend(datagram, bytes, null, null);
            asyncResult!.AsyncWaitHandle.WaitOne(_sendTimeout);
            if (!asyncResult.IsCompleted) throw new TimeoutException();
            
            var num = _udpClient.EndSend(asyncResult);
            return Task.FromResult(num);
        }
    }
}