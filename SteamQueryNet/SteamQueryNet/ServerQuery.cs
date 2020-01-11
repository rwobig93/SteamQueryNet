﻿using SteamQueryNet.Models;
using SteamQueryNet.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SteamQueryNet
{
    // This is not really required but imma be a good guy and create this for them people that wants to mock the ServerQuery.
    public interface IServerQuery
    {
        /// <summary>
        /// Renews the server challenge code of the ServerQuery instance in order to be able to execute further operations.
        /// </summary>
        /// <returns>The new created challenge.</returns>
        int RenewChallenge();

        /// <summary>
        /// Renews the server challenge code of the ServerQuery instance in order to be able to execute further operations.
        /// </summary>
        /// <returns>The new created challenge.</returns>
        Task<int> RenewChallengeAsync();

        /// <summary>
        /// Configures and Connects the created instance of SteamQuery UDP socket for Steam Server Query Operations.
        /// </summary>
        /// <param name="serverAddress">IPAddress or HostName of the server that queries will be sent.</param>
        /// <param name="port">Port of the server that queries will be sent.</param>
        /// <returns>Connected instance of ServerQuery.</returns>
        IServerQuery Connect(string serverAddress, int port);

        /// <summary>
        /// Requests and serializes the server information.
        /// </summary>
        /// <returns>Serialized ServerInfo instance.</returns>
        ServerInfo GetServerInfo();

        /// <summary>
        /// Requests and serializes the server information.
        /// </summary>
        /// <returns>Serialized ServerInfo instance.</returns>
        Task<ServerInfo> GetServerInfoAsync();

        /// <summary>
        /// Requests and serializes the list of player information. 
        /// </summary>
        /// <returns>Serialized list of Player instances.</returns>
        List<Player> GetPlayers();

        /// <summary>
        /// Requests and serializes the list of player information. 
        /// </summary>
        /// <returns>Serialized list of Player instances.</returns>
        Task<List<Player>> GetPlayersAsync();

        /// <summary>
        /// Requests and serializes the list of rules defined by the server.
        /// Warning: CS:GO Rules reply is broken since update CSGO 1.32.3.0 (Feb 21, 2014). 
        /// Before the update rules got truncated when exceeding MTU, after the update rules reply is not sent at all.
        /// </summary>
        /// <returns>Serialized list of Rule instances.</returns>
        List<Rule> GetRules();

        /// <summary>
        /// Requests and serializes the list of rules defined by the server.
        /// Warning: CS:GO Rules reply is broken since update CSGO 1.32.3.0 (Feb 21, 2014). 
        /// Before the update rules got truncated when exceeding MTU, after the update rules reply is not sent at all.
        /// </summary>
        /// <returns>Serialized list of Rule instances.</returns>
        Task<List<Rule>> GetRulesAsync();
    }

    public class ServerQuery : IServerQuery, IDisposable
    {
        private const int RESPONSE_HEADER_COUNT = 5;
        private const int RESPONSE_CODE_INDEX = 5;

        private readonly UdpClient _client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        private IPEndPoint _ipEndpoint;

        private int _port;
        private int _currentChallenge;

        /// <summary>
        /// Reflects the udp client connection state.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                return _client.Client.Connected;
            }
        }

        /// <summary>
        /// Amount of time in miliseconds to terminate send operation if the server won't respond.
        /// </summary>
        public int SendTimeout { get; set; }

        /// <summary>
        /// Amount of time in miliseconds to terminate receive operation if the server won't respond.
        /// </summary>
        public int ReceiveTimeout { get; set; }

        /// <summary>
        /// Creates a new instance of ServerQuery without UDP socket connection.
        /// </summary>
        public ServerQuery() { }

        /// <summary>
        /// Creates a new ServerQuery instance for Steam Server Query Operations.
        /// </summary>
        /// <param name="serverAddress">IPAddress or HostName of the server that queries will be sent.</param>
        /// <param name="port">Port of the server that queries will be sent.</param>
        public ServerQuery(string serverAddress, int port)
        {
            PrepareAndConnect(serverAddress, port);
        }

        /// <inheritdoc/>
        public IServerQuery Connect(string serverAddress, int port)
        {
            PrepareAndConnect(serverAddress, port);
            return this;
        }

        /// <inheritdoc/>
        public async Task<ServerInfo> GetServerInfoAsync()
        {
            const string requestPayload = "Source Engine Query\0";
            var sInfo = new ServerInfo
            {
                Ping = new Ping().Send(_ipEndpoint.Address).RoundtripTime
            };

            byte[] response = await SendRequestAsync(RequestHeaders.A2S_INFO, Encoding.UTF8.GetBytes(requestPayload));
            if (response.Length > 0)
            {
                ExtractData(sInfo, response, nameof(sInfo.EDF), true);
            }

            return sInfo;
        }

        /// <inheritdoc/>
        public ServerInfo GetServerInfo()
        {
            return RunSync(GetServerInfoAsync);
        }

        /// <inheritdoc/>
        public async Task<int> RenewChallengeAsync()
        {
            byte[] response = await SendRequestAsync(RequestHeaders.A2S_PLAYER, BitConverter.GetBytes(-1));
            if (response.Length > 0)
            {
                _currentChallenge = BitConverter.ToInt32(response.Skip(RESPONSE_CODE_INDEX).Take(sizeof(int)).ToArray(), 0);
            }

            return _currentChallenge;
        }

        /// <inheritdoc/>
        public int RenewChallenge()
        {
            return RunSync(RenewChallengeAsync);
        }

        /// <inheritdoc/>
        public async Task<List<Player>> GetPlayersAsync()
        {
            if (_currentChallenge == 0)
            {
                await RenewChallengeAsync();
            }

            byte[] response = await SendRequestAsync(RequestHeaders.A2S_PLAYER, BitConverter.GetBytes(_currentChallenge));
            if (response.Length > 0)
            {
                return ExtractListData<Player>(response);
            }
            else
            {
                throw new InvalidOperationException("Server did not response the query");
            }
        }

        /// <inheritdoc/>
        public List<Player> GetPlayers()
        {
            return RunSync(GetPlayersAsync);
        }

        /// <inheritdoc/>
        public async Task<List<Rule>> GetRulesAsync()
        {
            if (_currentChallenge == 0)
            {
                await RenewChallengeAsync();
            }

            byte[] response = await SendRequestAsync(RequestHeaders.A2S_RULES, BitConverter.GetBytes(_currentChallenge));
            if (response.Length > 0)
            {
                return ExtractListData<Rule>(response);
            }
            else
            {
                throw new InvalidOperationException("Server did not response the query");
            }
        }

        /// <inheritdoc/>
        public List<Rule> GetRules()
        {
            return RunSync(GetRulesAsync);
        }

        /// <summary>
        /// Disposes the object and its disposables.
        /// </summary>
        public void Dispose()
        {
            _client.Close();
            _client.Dispose();
        }

        private void PrepareAndConnect(string serverAddress, int port)
        {
            _port = port;

            // Check the port range
            if (_port < IPEndPoint.MinPort || _port > IPEndPoint.MaxPort)
            {
                throw new ArgumentException($"Port should be between {IPEndPoint.MinPort} and {IPEndPoint.MaxPort}");
            }

            // Try to parse the serverAddress as IP first
            if (IPAddress.TryParse(serverAddress, out IPAddress parsedIpAddress))
            {
                // Yep its an IP.
                _ipEndpoint = new IPEndPoint(parsedIpAddress, _port);
            }
            else
            {
                // Nope it might be a hostname.
                try
                {
                    IPAddress[] addresslist = Dns.GetHostAddresses(serverAddress);
                    if (addresslist.Length > 0)
                    {
                        // We get the first address.
                        _ipEndpoint = new IPEndPoint(addresslist[0], _port);
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

            _client.Client.SendTimeout = SendTimeout;
            _client.Client.ReceiveTimeout = ReceiveTimeout;
            _client.Connect(_ipEndpoint);
        }

        private List<TObject> ExtractListData<TObject>(byte[] rawSource)
            where TObject : class
        {
            // Create a list to contain the serialized data.
            var objectList = new List<TObject>();

            // Skip the response headers.
            byte player_count = rawSource[RESPONSE_CODE_INDEX];

            // Skip +1 for player_count
            IEnumerable<byte> dataSource = rawSource.Skip(RESPONSE_HEADER_COUNT + 1);

            // Iterate amount of times that the server said.
            for (byte i = 0; i < player_count; i++)
            {
                // Activate a new instance of the object.
                var objectInstance = Activator.CreateInstance<TObject>();

                // Extract the data.
                dataSource = ExtractData(objectInstance, dataSource.ToArray());

                // Add it into the list.
                objectList.Add(objectInstance);
            }

            return objectList;
        }

        private async Task<byte[]> SendRequestAsync(byte requestHeader, byte[] payload = null)
        {
            var request = BuildRequest(requestHeader, payload);
            await _client.SendAsync(request, request.Length);
            UdpReceiveResult result = await _client.ReceiveAsync();
            return result.Buffer;
        }

        private byte[] BuildRequest(byte headerCode, byte[] extraParams = null)
        {
            /* All requests consists 4 FF's and a header code to execute the request.
             * Check here: https://developer.valvesoftware.com/wiki/Server_queries#Protocol for further information about the protocol. */
            var request = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, headerCode };

            // If we have any extra payload, concatenate those into our requestHeaders and return;
            return extraParams != null
                ? request.Concat(extraParams).ToArray()
                : request;
        }

        private IEnumerable<byte> ExtractData<TObject>(TObject objectRef, byte[] dataSource, string edfPropName = "", bool stripHeaders = false)
            where TObject : class
        {
            IEnumerable<byte> takenBytes = new List<byte>();

            // We can be a good guy and ask for any extra jobs :)
            IEnumerable<byte> enumerableSource = stripHeaders
                ? dataSource.Skip(RESPONSE_HEADER_COUNT)
                : dataSource;

            // We get every property that does not contain ParseCustom and NotParsable attributes on them to iterate through all and parse/assign their values.
            IEnumerable<PropertyInfo> propsOfObject = typeof(TObject).GetProperties()
                .Where(x => x.CustomAttributes.Count(y => y.AttributeType == typeof(ParseCustomAttribute)
                                                       || y.AttributeType == typeof(NotParsableAttribute)) == 0);

            foreach (PropertyInfo property in propsOfObject)
            {
                /* Check for EDF property name, if it was provided then it mean that we have EDF properties in the model.
                 * You can check here: https://developer.valvesoftware.com/wiki/Server_queries#A2S_INFO to get more info about EDF's. */
                if (!string.IsNullOrEmpty(edfPropName))
                {
                    // Does the property have an EDFAttribute assigned ?
                    CustomAttributeData edfInfo = property.CustomAttributes.FirstOrDefault(x => x.AttributeType == typeof(EDFAttribute));
                    if (edfInfo != null)
                    {
                        // Get the EDF value that was returned by the server.
                        byte edfValue = (byte)typeof(TObject).GetProperty(edfPropName).GetValue(objectRef);

                        // Get the EDF condition value that was provided in the model.
                        byte edfPropertyConditionValue = (byte)edfInfo.ConstructorArguments[0].Value;

                        // Continue if the condition does not pass because it means that the server did not include any information about this property.
                        if ((edfValue & edfPropertyConditionValue) <= 0) { continue; }
                    }
                }

                /* Basic explanation of what is going of from here;
                 * Get the type of the property and get amount of bytes of its size from the response array,
                 * Convert the parsed value to its type and assign it.
                 */

                /* We have to handle strings separately since their size is unknown and they are also null terminated.
                 * Check here: https://developer.valvesoftware.com/wiki/String for further information about Strings in the protocol.
                 */
                if (property.PropertyType == typeof(string))
                {
                    // Clear the buffer first then take till the termination.
                    takenBytes = enumerableSource
                        .SkipWhile(x => x == 0)
                        .TakeWhile(x => x != 0);

                    // Parse it into a string.
                    property.SetValue(objectRef, Encoding.UTF8.GetString(takenBytes.ToArray()));

                    // Update the source by skipping the amount of bytes taken from the source and + 1 for termination byte.
                    enumerableSource = enumerableSource.Skip(takenBytes.Count() + 1);
                }
                else
                {
                    // Is the property an Enum ? if yes we should be getting the underlying type since it might differ.
                    Type typeOfProperty = property.PropertyType.IsEnum
                        ? property.PropertyType.GetEnumUnderlyingType()
                        : property.PropertyType;

                    // Extract the value and the size from the source.
                    (object result, int size) = ExtractMarshalType(enumerableSource, typeOfProperty);

                    /* If the property is an enum we should parse it first then assign its value,
                     * if not we can just give it to SetValue since it was converted by ExtractMarshalType already.*/
                    property.SetValue(objectRef, property.PropertyType.IsEnum
                        ? Enum.Parse(property.PropertyType, result.ToString())
                        : result);

                    // Update the source by skipping the amount of bytes taken from the source.
                    enumerableSource = enumerableSource.Skip(size);
                }
            }

            // We return the last state of the processed source.
            return enumerableSource;
        }

        private TResult RunSync<TResult>(Func<Task<TResult>> func)
        {
            var cultureUi = CultureInfo.CurrentUICulture;
            var culture = CultureInfo.CurrentCulture;
            return new TaskFactory().StartNew(() =>
            {
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = cultureUi;
                return func();
            }).Unwrap().GetAwaiter().GetResult();
        }

        private (object, int) ExtractMarshalType(IEnumerable<byte> source, Type type)
        {
            // Get the size of the given type.
            int sizeOfType = Marshal.SizeOf(type);

            // Take amount of bytes from the source array.
            IEnumerable<byte> takenBytes = source.Take(sizeOfType);

            // We actually need to go into an unsafe block here since as far as i know, this is the only way to convert a byte[] source into its given type on runtime.
            unsafe
            {
                fixed (byte* sourcePtr = takenBytes.ToArray())
                {
                    return (Marshal.PtrToStructure(new IntPtr(sourcePtr), type), sizeOfType);
                }
            }
        }
    }
}
