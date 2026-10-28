using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>iOS USB 소켓과 Android AOA 브리지가 접속할 루프백 리스너를 관리한다.</summary>
    internal sealed class UsbConnectionListener : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Action<TcpClient> _accepted;
        private readonly Action<Exception> _faulted;
        private readonly Queue<TcpClient> _pending = new Queue<TcpClient>();
        private volatile bool _running;

        /// <summary>외부 네트워크에 노출하지 않는 USB 연결 수신기를 구성한다.</summary>
        internal UsbConnectionListener(int port, Action<TcpClient> accepted, Action<Exception> faulted)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _accepted = accepted;
            _faulted = faulted;
        }

        /// <summary>백그라운드에서 USB로 전달된 TCP 접속을 수락한다.</summary>
        internal void Start()
        {
            try
            {
                _listener.Start(4);
                _running = true;
                new Thread(AcceptLoop) { IsBackground = true, }.Start();
            }
            catch (Exception exception)
            {
                _faulted(exception);
            }
        }

        /// <summary>수락한 연결을 Tick을 호출한 엔진 스레드의 세션 처리에 전달한다.</summary>
        internal void Tick()
        {
            while (_running)
            {
                TcpClient client;
                lock (_pending)
                {
                    if (_pending.Count == 0)
                    {
                        return;
                    }
                    client = _pending.Dequeue();
                }
                _accepted(client);
            }
        }

        /// <summary>종료 중 접속과 과도한 미처리 접속을 닫고 정상 연결을 대기열에 넣는다.</summary>
        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    lock (_pending)
                    {
                        if (!_running || _pending.Count >= 4)
                        {
                            client.Close();
                        }
                        else
                        {
                            _pending.Enqueue(client);
                        }
                    }
                }
                catch (Exception exception)
                {
                    if (_running)
                    {
                        _faulted(exception);
                    }
                    break;
                }
            }
        }

        /// <summary>수신 대기를 해제하고 아직 서비스에 전달되지 않은 소켓을 정리한다.</summary>
        public void Dispose()
        {
            lock (_pending)
            {
                _running = false;
                _listener.Stop();
                while (_pending.Count > 0)
                {
                    _pending.Dequeue().Close();
                }
            }
        }
    }
}
