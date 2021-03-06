using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using RedHatX.AspNetCore.Server.Kestrel.Transport.Linux;

namespace Tests
{
    public delegate void TestServerConnectionHandler(IPipeReader input, IPipeWriter output);

    class TestServerOptions
    {
        public int ThreadCount { get; set; } = 1;
        public bool DeferAccept { get; set; } = false;
        public TestServerConnectionHandler ConnectionHandler { get; set; } = TestServer.Echo;
    }

    class TestServer : IConnectionHandler, IDisposable
    {
        class ConnectionContext : IConnectionContext
        {
            public ConnectionContext(string connectionId, IPipeWriter input, IPipeReader output)
            {
                ConnectionId = connectionId;
                Input = input;
                Output = output;
            }
            public string ConnectionId { get; }
            public IPipeWriter Input { get; }
            public IPipeReader Output { get; }

            // TODO: Remove these (Use Pipes instead?)
            void IConnectionContext.Abort(Exception ex) { }
            void IConnectionContext.OnConnectionClosed(Exception ex) { }
        }

        private Transport _transport;
        private IPEndPoint _serverAddress;
        private TestServerConnectionHandler _connectionHandler;

        public TestServer(TestServerOptions options = null)
        {
            options = options ?? new TestServerOptions();
            _connectionHandler = options.ConnectionHandler;
            _serverAddress = new IPEndPoint(IPAddress.Loopback, 0);
            var transportOptions = new LinuxTransportOptions()
            {
                ThreadCount = options.ThreadCount,
                DeferAccept = options.DeferAccept
            };
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddConsole((n, l) => false);
            _transport = new Transport(_serverAddress, this, transportOptions, loggerFactory);
        }

        public TestServer(TestServerConnectionHandler connectionHandler) :
            this(new TestServerOptions() { ConnectionHandler = connectionHandler })
        {}

        public Task BindAsync()
        {
            return _transport.BindAsync();
        }

        public Task UnbindAsync()
        {
            return _transport.UnbindAsync();
        }

        public Task StopAsync()
        {
            return _transport.StopAsync();
        }

        public IConnectionContext OnConnection(IConnectionInformation connectionInfo)
        {
            var factory = connectionInfo.PipeFactory;
            var input = factory.Create(GetInputPipeOptions(connectionInfo.InputWriterScheduler));
            var output = factory.Create(GetOutputPipeOptions(connectionInfo.OutputReaderScheduler));

            _connectionHandler(input.Reader, output.Writer);

            return new ConnectionContext(string.Empty, input.Writer, output.Reader);
        }

        // copied from Kestrel
        private const long _maxRequestBufferSize = 1024 * 1024;
        private const long _maxResponseBufferSize = 64 * 1024;

        private PipeOptions GetInputPipeOptions(IScheduler writerScheduler) => new PipeOptions
        {
            ReaderScheduler = InlineScheduler.Default, // _serviceContext.ThreadPool,
            WriterScheduler = writerScheduler,
            MaximumSizeHigh = _maxRequestBufferSize,
            MaximumSizeLow = _maxRequestBufferSize
        };

        private PipeOptions GetOutputPipeOptions(IScheduler readerScheduler) => new PipeOptions
        {
            ReaderScheduler = readerScheduler,
            WriterScheduler = InlineScheduler.Default, // _serviceContext.ThreadPool,
            MaximumSizeHigh = _maxResponseBufferSize,
            MaximumSizeLow = _maxResponseBufferSize
        };

        public void Dispose()
        {
            _transport.Dispose(); 
        }

        public static async void Echo(IPipeReader input, IPipeWriter output)
        {
            try
            {
                while (true)
                {
                    var result = await input.ReadAsync();
                    var request = result.Buffer;

                    if (request.IsEmpty && result.IsCompleted)
                    {
                        input.Advance(request.End);
                        break;
                    }

                    int len = request.Length;
                    var response = output.Alloc();
                    response.Append(request);
                    await response.FlushAsync();
                    input.Advance(request.End);
                }
            }
            catch
            {
                input.Complete();
                output.Complete();
            }
        }

        public Socket ConnectTo()
        {
            var client = Socket.Create(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, blocking: true);
            client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, 1);
            client.Connect(_serverAddress);
            return client;
        }
    }
}
