using System;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Tmds.Kestrel.Linux;
using Tmds.Posix;
using Xunit;

namespace Tests
{
    public class TransportTests
    {
        [Fact]
        public async Task Echo()
        {
            using (var testServer = new TestServer(TestServer.Echo))
            {
                await testServer.BindAsync();
                using (var client = testServer.ConnectTo())
                {
                    // Send some bytes
                    byte[] sendBuffer = new byte[] { 1, 2, 3 };
                    client.Send(new ArraySegment<byte>(sendBuffer));

                    // Read the echo
                    byte[] receiveBuffer = new byte[10];
                    var received = client.Receive(new ArraySegment<byte>(receiveBuffer));
                    Assert.Equal(sendBuffer.Length, received);
                }
            }
        }

        [Fact]
        public async Task MultiThread()
        {
            using (var testServer = new TestServer(connectionHandler: null, threadCount: 2))
            {
                await testServer.BindAsync();
                await testServer.UnbindAsync();
                await testServer.StopAsync();
            }
        }

        [Fact]
        public async Task Unbind()
        {
            using (var testServer = new TestServer(TestServer.Echo))
            {
                await testServer.BindAsync();
                await testServer.UnbindAsync();
                var exception = Assert.Throws<PosixException>(() => testServer.ConnectTo());
                Assert.Equal(PosixResult.ECONNREFUSED, exception.Error);
            }
        }

        [Fact]
        public async Task StopDisconnectsClient()
        {
            using (var testServer = new TestServer(TestServer.Echo))
            {
                await testServer.BindAsync();

                using (var client = testServer.ConnectTo())
                {
                    await testServer.UnbindAsync();
                    await testServer.StopAsync();

                    // receive returns EOF                
                    byte[] receiveBuffer = new byte[10];
                    var received = client.Receive(new ArraySegment<byte>(receiveBuffer));
                    Assert.Equal(0, received);

                    // send returns EPIPE
                    var exception = Assert.Throws<PosixException>(() =>
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            byte[] sendBuffer = new byte[] { 1, 2, 3 };
                            client.Send(new ArraySegment<byte>(sendBuffer));
                        }                    
                    });
                    Assert.Equal(PosixResult.EPIPE, exception.Error);
                }
            }
        }

        [Fact]
        public async Task Writable()
        {
            const int bufferSize = 2048;
            int bytesWritten = 0;
            var waitingForWritable = new TaskCompletionSource<object>();
            TestServer.ConnectionHandler connectionHandler = async (input, output) =>
            {
                Timer writeTimeout = new Timer(
                    // timeout -> we are waiting for the socket to become writable
                    o => waitingForWritable.SetResult(null),
                    null, Timeout.Infinite, Timeout.Infinite
                );

                do
                {
                    var buffer = output.Alloc(bufferSize);
                    buffer.Advance(bufferSize);
                    bytesWritten += bufferSize;

                    // If it takes 1 second to write, assume the socket
                    // is no longer writable
                    writeTimeout.Change(1000, Timeout.Infinite);
                    await buffer.FlushAsync();
                    // cancel the timeout
                    writeTimeout.Change(Timeout.Infinite, Timeout.Infinite);

                } while (!waitingForWritable.Task.IsCompleted);

                writeTimeout.Dispose();
                output.Complete();
                input.Complete();
            };

            using (var testServer = new TestServer(connectionHandler))
            {
                await testServer.BindAsync();
                using (var client = testServer.ConnectTo())
                {
                    // wait for the server to have sent so much data
                    // so it waiting for us to read some
                    await waitingForWritable.Task;

                    // read all the data
                    int receivedBytes = 0;
                    byte[] receiveBuffer = new byte[bufferSize];
                    while (receivedBytes < bytesWritten)
                    {
                        var received = client.Receive(new ArraySegment<byte>(receiveBuffer));
                        receivedBytes += received;
                    }
                }
            }
        }

        [Fact]
        public async Task Receive()
        {
            // client send 1M bytes which are an int counter
            // server receives and checkes the counting
            const int receiveLength = 1000000;
            TestServer.ConnectionHandler connectionHandler = async (input, output) =>
            {
                int bytesReceived = 0;
                while (true)
                {
                    var readResult = await input.ReadAsync();
                    var buffer = readResult.Buffer;
                    if (buffer.IsEmpty && readResult.IsCompleted)
                    {
                        input.Advance(buffer.End);
                        break;
                    }
                    foreach (var memory in buffer)
                    {
                        ArraySegment<byte> segment;
                        Assert.True(memory.TryGetArray(out segment));
                        // test assumes memory.Length is a multiple of 4
                        CheckMemory(segment, startValue: bytesReceived / 4);
                        bytesReceived += memory.Length;
                    }
                    input.Advance(buffer.End);
                }
                Assert.Equal(receiveLength, bytesReceived);
                output.Complete();
                input.Complete();
            };

            using (var testServer = new TestServer(connectionHandler))
            {
                await testServer.BindAsync();
                using (var client = testServer.ConnectTo())
                {
                    var buffer = new byte[1000000];
                    FillBuffer(new ArraySegment<byte>(buffer), startValue: 0);
                    var result = client.Send(new ArraySegment<byte>(buffer));
                    Assert.Equal(receiveLength, result);

                    // wait for the server to stop
                    client.Shutdown(SocketShutdown.Send);
                    var receiveBuffer = new byte[1];
                    client.Receive(new ArraySegment<byte>(receiveBuffer));
                }
            }
        }

        [Fact]
        public async Task Send()
        {
            // server send 1M+ bytes which are an int counter
            // client receives and checkes the counting
            const int minSendLength = 1000000;
            TestServer.ConnectionHandler connectionHandler = async (input, output) =>
            {
                int bytesSent = 0;
                var buffer = output.Alloc(0);
                do
                {
                    buffer.Ensure(1);
                    var memory = buffer.Memory;
                    ArraySegment<byte> segment;
                    Assert.True(memory.TryGetArray(out segment));
                    // test assumes memory.Length is a multiple of 4
                    FillBuffer(segment, startValue: bytesSent / 4);
                    buffer.Advance(memory.Length);
                    bytesSent += memory.Length;
                } while (bytesSent < minSendLength);
                // single flush
                await buffer.FlushAsync();
                output.Complete();
                input.Complete();
            };

            using (var testServer = new TestServer(connectionHandler))
            {
                await testServer.BindAsync();
                using (var client = testServer.ConnectTo())
                {
                    int totalReceived = 0;
                    var receiveBuffer = new byte[4000];
                    do
                    {
                        int received = client.Receive(new ArraySegment<byte>(receiveBuffer));
                        if (received == 0)
                        {
                            break;
                        }
                        // test assumes memory.Length is a multiple of 4
                        CheckMemory(new ArraySegment<byte>(receiveBuffer, 0, received), startValue: totalReceived / 4);
                        totalReceived += received;
                    } while (true);
                    Assert.True(totalReceived >= minSendLength);
                }
            }
        }

        private unsafe static void FillBuffer(ArraySegment<byte> segment, int startValue)
        {
            fixed (byte* bytePtr = segment.Array)
            {
                int* intPtr = (int*)(bytePtr + segment.Offset);
                int currentValue = startValue;
                for (int i = 0; i < segment.Count / 4; i++)
                {
                    *intPtr = currentValue;
                    intPtr++;
                    currentValue++;
                }
            }
        }

        private unsafe static void CheckMemory(ArraySegment<byte> segment, int startValue)
        {
            fixed (byte* bytePtr = segment.Array)
            {
                int* intPtr = (int*)(bytePtr + segment.Offset);
                int currentValue = startValue;
                for (int i = 0; i < segment.Count / 4; i++)
                {
                    Assert.Equal(currentValue, *intPtr);
                    currentValue++;
                    intPtr++;
                }
            }
        }
    }
}