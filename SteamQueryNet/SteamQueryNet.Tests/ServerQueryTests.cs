using Moq;

using Newtonsoft.Json;

using SteamQueryNet.Interfaces;
using SteamQueryNet.Models;
using SteamQueryNet.Tests.Responses;
using SteamQueryNet.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace SteamQueryNet.Tests
{
    public class ServerQueryTests
    {
        private const string IpAddress = "127.0.0.1";
        private const string HostName = "localhost";
        private const ushort Port = 27015;
        private byte _packetCount;
        private readonly IPEndPoint _localIpEndpoint = new(IPAddress.Parse("127.0.0.1"), 0);

        [Theory]
        [InlineData(IpAddress)]
        [InlineData(HostName)]
        public void ShouldInitializeWithProperHost(string host)
        {
            using (var sq = new ServerQuery(new Mock<IUdpClient>().Object, It.IsAny<IPEndPoint>()))
            {
                sq.Connect(host, Port);
            }
        }

        [Theory]
        [InlineData("127.0.0.1:27015")]
        [InlineData("127.0.0.1,27015")]
        [InlineData("localhost:27015")]
        [InlineData("localhost,27015")]
        [InlineData("steam://connect/localhost:27015")]
        [InlineData("steam://connect/127.0.0.1:27015")]
        public void ShouldInitializeWithProperHostAndPort(string ipAndHost)
        {
            using (var sq = new ServerQuery(new Mock<IUdpClient>().Object, It.IsAny<IPEndPoint>()))
            {
                sq.Connect(ipAndHost);
            }
        }

        [Theory]
        [InlineData("invalidHost:-1")]
        [InlineData("invalidHost,-1")]
        [InlineData("invalidHost:65536")]
        [InlineData("invalidHost,65536")]
        [InlineData("256.256.256.256:-1")]
        [InlineData("256.256.256.256,-1")]
        [InlineData("256.256.256.256:65536")]
        [InlineData("256.256.256.256,65536")]
        public void ShouldNotInitializeWithAnInvalidHostAndPort(string invalidHost)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                using (var sq = new ServerQuery(new Mock<IUdpClient>().Object, It.IsAny<IPEndPoint>()))
                {
                    sq.Connect(invalidHost);
                }
            });
        }

        [Fact]
        public void GetServerInfo_ShouldPopulateCorrectServerInfo()
        {
            var (responsePacket, responseObject) = ResponseHelper.GetValidResponse(ResponseHelper.ServerInfo);
            var expectedObject = (ServerInfo)responseObject;

            byte[][] requestPackets = { RequestHelpers.PrepareAS2_INFO_Request() };
            var responsePackets = new[] { responsePacket };

            var udpClientMock = SetupReceiveResponse(responsePackets);
            SetupRequestCompare(requestPackets, udpClientMock);

            using (var sq = new ServerQuery(udpClientMock.Object, _localIpEndpoint))
            {
                Assert.Equal(JsonConvert.SerializeObject(expectedObject), JsonConvert.SerializeObject(sq.GetServerInfo()));
            }
        }

        [Fact]
        public void GetPlayers_ShouldPopulateCorrectPlayers()
        {
            var (playersPacket, responseObject) = ResponseHelper.GetValidResponse(ResponseHelper.GetPlayers);
            var expectedObject = (List<Player>)responseObject;

            var challengePacket = RequestHelpers.PrepareAS2_RENEW_CHALLENGE_Request();

            // Both requests will be executed on AS2_PLAYER since thats how you refresh challenges.
            var requestPackets = new[] { challengePacket, challengePacket };

            // First response is the Challenge renewal response and the second 
            var responsePackets = new[] { challengePacket, playersPacket };

            var udpClientMock = SetupReceiveResponse(responsePackets);
            SetupRequestCompare(requestPackets, udpClientMock);

            // Ayylmao it looks ugly as hell but we will improve it later on.
            using (var sq = new ServerQuery(udpClientMock.Object, _localIpEndpoint))
            {
                Assert.Equal(JsonConvert.SerializeObject(expectedObject), JsonConvert.SerializeObject(sq.GetPlayers()));
            }
        }

        /*
         * We keep this test here to be able to have us a notifier when the Rules API becomes available.
         * So, this is more like an integration test than a unit test.
         * If this test starts to fail, we'll know that the Rules API started to respond.
         */
        [Fact]
        public void GetRules_ShouldThrowTimeoutException()
        {
            // Surf Heaven rulez.
            const string trustedServer = "steam://connect/54.37.111.217:27015";

            using (var sq = new ServerQuery())
            {
                sq.Connect(trustedServer);

                // Make sure that the server is still alive.
                Assert.True(sq.IsConnected);
                var responded = Task.WaitAll(new Task[] { sq.GetRulesAsync() }, 2000);
                Assert.False(responded);
            }
        }

        private void SetupRequestCompare(IEnumerable<byte[]> requestPackets, Mock<IUdpClient> udpClientMock)
        {
            udpClientMock
                .Setup(x => x.SendAsync(It.IsAny<byte[]>(), It.IsAny<int>()))
                .Callback<byte[], int>((request, _) =>
                {
                    Assert.True(TestValidators.CompareBytes(requestPackets.ElementAt(_packetCount), request));
                    ++_packetCount;
                });
        }

        private Mock<IUdpClient> SetupReceiveResponse(IEnumerable<byte[]> udpPackets)
        {
            var udpClientMock = new Mock<IUdpClient>();
            var setupSequence = udpClientMock.SetupSequence(x => x.ReceiveAsync());
            foreach (var packet in udpPackets)
            {
                setupSequence = setupSequence.ReturnsAsync(new UdpReceiveResult(packet, _localIpEndpoint));
            }

            return udpClientMock;
        }
    }
}
