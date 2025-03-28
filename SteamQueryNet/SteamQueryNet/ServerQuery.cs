﻿using SteamQueryNet.Interfaces;
using SteamQueryNet.Models;
using SteamQueryNet.Services;
using SteamQueryNet.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SteamQueryNet.Tests")]
namespace SteamQueryNet
{
    public class ServerQuery : IServerQuery, IDisposable
    {
        private IPEndPoint _remoteIpEndpoint;

        private ushort _port;
        private int _currentChallenge;

        protected virtual IUdpClient UdpClient { get; private set; }

        /// <summary>
        /// Reflects the udp client connection state.
        /// </summary>
        public bool IsConnected => UdpClient.IsConnected;

        /// <summary>
        /// Amount of time in milliseconds to terminate send operation if the server won't respond. Defaults to 5000 (5s).
        /// </summary>
        public int SendTimeout { get; set; } = 5000;

        /// <summary>
        /// Amount of time in milliseconds to terminate receive operation if the server won't respond. Defaults to 5000 (5s).
        /// </summary>
        public int ReceiveTimeout { get; set; } = 5000;

        /// <summary>
        /// Creates a new instance of ServerQuery with given UDPClient and remote endpoint.
        /// </summary>
        /// <param name="udpClient">UdpClient to communicate.</param>
        /// <param name="remoteEndpoint">Remote server endpoint.</param>
        public ServerQuery(IUdpClient udpClient, IPEndPoint remoteEndpoint)
        {
            UdpClient = udpClient;
            _remoteIpEndpoint = remoteEndpoint;
        }

        /// <summary>
        /// Creates a new instance of ServerQuery without UDP socket connection.
        /// </summary>
        public ServerQuery() { }

        /// <summary>
        /// Creates a new ServerQuery instance for Steam Server Query Operations.
        /// </summary>
        /// <param name="serverAddress">IPAddress or HostName of the server that queries will be sent.</param>
        /// <param name="port">Port of the server that queries will be sent.</param>
        public ServerQuery(string serverAddress, ushort port)
        {
            PrepareAndConnect(serverAddress, port);
        }

        /// <summary>
        /// Creates a new ServerQuery instance for Steam Server Query Operations.
        /// </summary>
        /// <param name="serverAddressAndPort">IPAddress or HostName of the server and port separated by a colon(:) or a comma(,).</param>
        public ServerQuery(string serverAddressAndPort)
        {
            var (serverAddress, port) = Helpers.ResolveIpAndPortFromString(serverAddressAndPort);
            PrepareAndConnect(serverAddress, port);
        }

        /// <summary>
        /// Creates a new instance of ServerQuery with the given Local IPEndpoint.
        /// </summary>
        /// <param name="customLocalIpEndpoint">Desired local IPEndpoint to bound.</param>
        /// <param name="serverAddressAndPort">IPAddress or HostName of the server and port separated by a colon(:) or a comma(,).</param>
        public ServerQuery(IPEndPoint customLocalIpEndpoint, string serverAddressAndPort)
        {
            UdpClient = new UdpWrapper(customLocalIpEndpoint, SendTimeout, ReceiveTimeout);
            var (serverAddress, port) = Helpers.ResolveIpAndPortFromString(serverAddressAndPort);
            PrepareAndConnect(serverAddress, port);
        }

        /// <summary>
        /// Creates a new instance of ServerQuery with the given Local IPEndpoint.
        /// </summary>
        /// <param name="customLocalIpEndpoint">Desired local IPEndpoint to bound.</param>
        /// <param name="serverAddress">IPAddress or HostName of the server that queries will be sent.</param>
        /// <param name="port">Port of the server that queries will be sent.</param>
        public ServerQuery(IPEndPoint customLocalIpEndpoint, string serverAddress, ushort port)
        {
            UdpClient = new UdpWrapper(customLocalIpEndpoint, SendTimeout, ReceiveTimeout);
            PrepareAndConnect(serverAddress, port);
        }

        /// <inheritdoc/>
        public IServerQuery Connect(string serverAddress, ushort port)
        {
            PrepareAndConnect(serverAddress, port);
            return this;
        }

        /// <inheritdoc/>
        public IServerQuery Connect(string serverAddressAndPort)
        {
            var (serverAddress, port) = Helpers.ResolveIpAndPortFromString(serverAddressAndPort);
            PrepareAndConnect(serverAddress, port);
            return this;
        }

        /// <inheritdoc/>
        public IServerQuery Connect(IPEndPoint customLocalIpEndpoint, string serverAddressAndPort)
        {
            UdpClient = new UdpWrapper(customLocalIpEndpoint, SendTimeout, ReceiveTimeout);
            var (serverAddress, port) = Helpers.ResolveIpAndPortFromString(serverAddressAndPort);
            PrepareAndConnect(serverAddress, port);
            return this;
        }

        /// <inheritdoc/>
        public IServerQuery Connect(IPEndPoint customLocalIpEndpoint, string serverAddress, ushort port)
        {
            UdpClient = new UdpWrapper(customLocalIpEndpoint, SendTimeout, ReceiveTimeout);
            PrepareAndConnect(serverAddress, port);
            return this;
        }

        /// <inheritdoc/>
        public async Task<ServerInfo> GetServerInfoAsync()
        {
            var serverInfo = new ServerInfo
            {
                Ping = new Ping().Send(_remoteIpEndpoint.Address)?.RoundtripTime ?? 0
            };

            if (_currentChallenge == 0)
            {
                await RenewChallengeAsync();
            }
            
            var response = await SendRequestAsync(RequestHelpers.PrepareAS2_INFO_Request(_currentChallenge));
            if (response.Length > 0)
            {
                DataResolutionUtils.ExtractData(serverInfo, response, nameof(serverInfo.Edf), true);
            }

            return serverInfo;
        }

        /// <inheritdoc/>
        public ServerInfo GetServerInfo()
        {
            return Helpers.RunSync(GetServerInfoAsync);
        }

        /// <inheritdoc/>
        public async Task<int> RenewChallengeAsync()
        {
            var response = await SendRequestAsync(RequestHelpers.PrepareAS2_RENEW_CHALLENGE_Request());
            if (response.Length > 0)
            {
                _currentChallenge = BitConverter.ToInt32(response.Skip(DataResolutionUtils.ResponseCodeIndex).Take(sizeof(int)).ToArray(), 0);
            }

            return _currentChallenge;
        }

        /// <inheritdoc/>
        public int RenewChallenge()
        {
            return Helpers.RunSync(RenewChallengeAsync);
        }

        /// <inheritdoc/>
        public async Task<List<Player>> GetPlayersAsync()
        {
            if (_currentChallenge == 0)
            {
                await RenewChallengeAsync();
            }

            var response = await SendRequestAsync(RequestHelpers.PrepareAS2_GENERIC_Request(RequestHeaders.A2SPlayer,_currentChallenge));

            if (response.Length > 0)
            {
                return DataResolutionUtils.ExtractListData<Player>(response);
            }
            
            throw new InvalidOperationException("Server did not response the query");
        }

        /// <inheritdoc/>
        public List<Player> GetPlayers()
        {
            return Helpers.RunSync(GetPlayersAsync);
        }

        /// <inheritdoc/>
        public async Task<List<Rule>> GetRulesAsync()
        {
            if (_currentChallenge == 0)
            {
                await RenewChallengeAsync();
            }

            var response = await SendRequestAsync(RequestHelpers.PrepareAS2_GENERIC_Request(RequestHeaders.A2SRules, _currentChallenge));
            if (response.Length > 0)
            {
                return DataResolutionUtils.ExtractListData<Rule>(response);
            }
            
            throw new InvalidOperationException("Server did not response the query");
        }

        /// <inheritdoc/>
        public List<Rule> GetRules()
        {
            return Helpers.RunSync(GetRulesAsync);
        }

        /// <summary>
        /// Disposes the object and its disposables.
        /// </summary>
        public void Dispose()
        {
            UdpClient.Close();
            UdpClient.Dispose();
        }

        private void PrepareAndConnect(string serverAddress, ushort port)
        {
            _port = port;

            // Try to parse the serverAddress as IP first
            if (IPAddress.TryParse(serverAddress, out var parsedIpAddress))
            {
                // Yep its an IP.
                _remoteIpEndpoint = new IPEndPoint(parsedIpAddress, _port);
            }
            else
            {
                // Nope it might be a hostname.
                var addressList = Dns.GetHostAddresses(serverAddress!);
                try
                {
                    if (addressList.Length > 0)
                    {
                        // We get the first address.
                        _remoteIpEndpoint = new IPEndPoint(addressList[0], _port);
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid host address {serverAddress}");
                    }
                }
                catch (SocketException ex)
                {
                    throw new ArgumentException("Could not reach the hostname.", ex);
                }
            }

            UdpClient ??= new UdpWrapper(new IPEndPoint(IPAddress.Any, 0), SendTimeout, ReceiveTimeout);
            UdpClient.Connect(_remoteIpEndpoint);
        }

        private async Task<byte[]> SendRequestAsync(byte[] request)
        {
            await UdpClient.SendAsync(request, request.Length);
            var result = await UdpClient.ReceiveAsync();
            return result.Buffer;
        }
    }
}