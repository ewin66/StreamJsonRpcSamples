using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace TcpSample.Server
{
    public class JsonRpcService : BackgroundService
    {
        private const int _port = 6000;

        private IConnectionListener _connectionListener;
        private readonly GreeterServer _greeterServer;
        private readonly ILogger<JsonRpcService> _logger;
        private readonly IConnectionListenerFactory _connectionListenerFactory;

        private readonly ConcurrentDictionary<string, (ConnectionContext Context, Task ExecutionTask)> 
            _connections = new ConcurrentDictionary<string, (ConnectionContext, Task)>();

        public JsonRpcService(
            ILogger<JsonRpcService> logger,
            GreeterServer greeterServer,
            IConnectionListenerFactory connectionListenerFactory)
        {
            _logger = logger;
            _greeterServer = greeterServer;
            _connectionListenerFactory = connectionListenerFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var endPoint = new IPEndPoint(IPAddress.Loopback, _port);
            _connectionListener = await _connectionListenerFactory.BindAsync(endPoint, stoppingToken);
            _logger.LogInformation($"RPC �����Ѱ󶨶˿ڣ�{_port}");

            while (true)
            {
                _logger.LogInformation("�ȴ��ͻ�������...");
                var connectionContext = await _connectionListener.AcceptAsync(stoppingToken);
                if (connectionContext == null)
                {
                    break;
                }
                _logger.LogInformation($"����ͻ��˽������� {connectionContext.ConnectionId}");

                _connections[connectionContext.ConnectionId] = (connectionContext, AcceptAsync(connectionContext));
            }

            _logger.LogInformation("���ڽ��� RPC ����...");

            var connectionsExecutionTasks = new List<Task>(_connections.Count);

            foreach (var connection in _connections)
            {
                _logger.LogWarning($"����ȡ�� {connection.Key} �����ϵ�����...");
                connectionsExecutionTasks.Add(connection.Value.ExecutionTask);
                connection.Value.Context.Abort();
            }

            _logger.LogInformation("�ȴ�����ִ�е��������...");
            await Task.WhenAll(connectionsExecutionTasks);
            _logger.LogInformation("���������ѽ���");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _connectionListener.DisposeAsync();
        }

        private async Task AcceptAsync(ConnectionContext connectionContext)
        {
            try
            {
                await Task.Yield();
                var messageFormatter = new JsonMessageFormatter(Encoding.UTF8);
                var messageHandler = new LengthHeaderMessageHandler(connectionContext.Transport, messageFormatter);

                using (var jsonRpc = new JsonRpc(messageHandler, _greeterServer))
                {
                    _logger.LogInformation($"��ʼ�������� {connectionContext.ConnectionId} �ϵ���Ϣ...");
                    jsonRpc.StartListening();

                    await jsonRpc.Completion;
                }

                await connectionContext.ConnectionClosed.WaitAsync();
            }
            catch (ConnectionResetException)
            { }
            catch (ConnectionAbortedException)
            { }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"���� {connectionContext.ConnectionId} �����쳣");
            }
            finally
            {
                await connectionContext.DisposeAsync();
                _connections.TryRemove(connectionContext.ConnectionId, out _);
                _logger.LogInformation($"���� {connectionContext.ConnectionId} �ѶϿ�");
            }
        }
    }
}
