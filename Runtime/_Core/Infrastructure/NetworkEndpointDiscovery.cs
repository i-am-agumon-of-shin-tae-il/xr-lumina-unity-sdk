using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using XRLumina._Core.Model;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>
    /// 고정 호스트를 사용하거나 UDP 브로드캐스트로 TCP 연결 대상을 찾는다.
    /// 요청 문자열과 응답 해석은 외부에서 주입받아 특정 제품 프로토콜에 의존하지 않는다.
    /// </summary>
    internal sealed class NetworkEndpointDiscovery
    {
        private readonly string _fixedHost;
        private readonly int _port;
        private readonly int _discoveryPort;
        private readonly int _timeoutMs;
        private readonly string _requestMessage;
        private readonly Func<string, DiscoveryAdvertisement> _parseResponse;
        private string _acceptedInstanceId;

        /// <summary>고정 주소와 UDP 탐색 설정 및 응답 해석기를 구성한다.</summary>
        public NetworkEndpointDiscovery(
            string fixedHost,
            int port,
            int discoveryPort,
            int timeoutMs,
            string requestMessage,
            Func<string, DiscoveryAdvertisement> parseResponse
        )
        {
            _fixedHost = fixedHost;
            _port = port;
            _discoveryPort = discoveryPort;
            _timeoutMs = timeoutMs;
            _requestMessage = requestMessage;
            _parseResponse = parseResponse ?? throw new ArgumentNullException(nameof(parseResponse));
        }

        /// <summary>현재 설정에 따라 TCP 연결 대상을 반환한다.</summary>
        public NetworkEndpoint Resolve()
        {
            if (!string.IsNullOrWhiteSpace(_fixedHost))
            {
                return new NetworkEndpoint(_fixedHost, _port);
            }

            using (var udp = new UdpClient())
            {
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = _timeoutMs;
                var request = Encoding.UTF8.GetBytes(_requestMessage);
                SocketException sendError = null;
                var sent = 0;
                foreach (var broadcastAddress in GetBroadcastAddresses())
                {
                    try
                    {
                        udp.Send(request, request.Length, new IPEndPoint(broadcastAddress, _discoveryPort));
                        sent++;
                    }
                    catch (SocketException exception)
                    {
                        sendError = exception;
                    }
                }
                if (sent == 0)
                {
                    throw sendError ?? new SocketException((int)SocketError.NetworkUnreachable);
                }

                var expiresAt = DateTime.UtcNow.AddMilliseconds(_timeoutMs);
                while (true)
                {
                    var remainingMs = (int)(expiresAt - DateTime.UtcNow).TotalMilliseconds;
                    if (remainingMs <= 0)
                    {
                        throw new SocketException((int)SocketError.TimedOut);
                    }

                    udp.Client.ReceiveTimeout = remainingMs;
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var response = Encoding.UTF8.GetString(udp.Receive(ref remote));
                    var result = _parseResponse(response);
                    if (!AcceptInstance(result.InstanceId))
                    {
                        continue;
                    }

                    return new NetworkEndpoint(
                        remote.Address.ToString(),
                        result.Port > 0 ? result.Port : _port
                    );
                }
            }
        }

        /// <summary>최초 연결한 서버 인스턴스만 재연결 대상으로 허용한다.</summary>
        private bool AcceptInstance(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return string.IsNullOrWhiteSpace(_acceptedInstanceId);
            }
            if (string.IsNullOrWhiteSpace(_acceptedInstanceId))
            {
                _acceptedInstanceId = instanceId;
                return true;
            }
            return _acceptedInstanceId == instanceId;
        }

        /// <summary>제한 브로드캐스트와 활성 IPv4 인터페이스의 directed broadcast 주소를 반환한다.</summary>
        private static IReadOnlyCollection<IPAddress> GetBroadcastAddresses()
        {
            var result = new List<IPAddress> { IPAddress.Broadcast, };
            var addresses = new HashSet<string>();
            addresses.Add(IPAddress.Broadcast.ToString());

            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                        networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                            unicast.IPv4Mask == null)
                        {
                            continue;
                        }

                        var addressBytes = unicast.Address.GetAddressBytes();
                        var maskBytes = unicast.IPv4Mask.GetAddressBytes();
                        var broadcastBytes = new byte[addressBytes.Length];
                        for (var index = 0; index < broadcastBytes.Length; index++)
                        {
                            broadcastBytes[index] = (byte)(addressBytes[index] | ~maskBytes[index]);
                        }

                        var broadcast = new IPAddress(broadcastBytes);
                        if (addresses.Add(broadcast.ToString()))
                        {
                            result.Add(broadcast);
                        }
                    }
                }
            }
            catch (NetworkInformationException) {}
            catch (NotImplementedException) {}

            return result;
        }
    }
}
