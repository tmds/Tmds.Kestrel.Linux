using System;
using System.IO.Pipelines;
using System.Text;
using System.Text.Formatting;
using Kestrel;

namespace ConsoleApplication
{
    class HttpServer : IConnectionHandler
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
        }

        private PipeFactory _pipeFactory;
        public HttpServer()
        {
            _pipeFactory = new PipeFactory();
        }

        
        public IConnectionContext OnConnection(IConnectionInformation connectionInfo, PipeOptions inputOptions, PipeOptions outputOptions)
        {
            const int maxSize = 2000;
            inputOptions.MaximumSizeHigh = maxSize;
            inputOptions.MaximumSizeLow = maxSize;
            outputOptions.MaximumSizeHigh = maxSize;
            outputOptions.MaximumSizeLow = maxSize;

            var input = _pipeFactory.Create(inputOptions);
            var output = _pipeFactory.Create(outputOptions);

            HandleConnection(input.Reader, output.Writer);

            return new ConnectionContext(string.Empty, input.Writer, output.Reader);
        }

        private async void HandleConnection(IPipeReader reader, IPipeWriter writer)
        {
            try
            {
                bool complete = false;
                while (!complete)
                {
                    var result = await reader.ReadAsync();
                    ReadableBuffer input = result.Buffer;
                    complete = result.IsCompleted;
                    if (input.IsEmpty && result.IsCompleted)
                    {
                        // No more data
                        reader.Advance(input.End, input.End);
                        break;
                    }

                    ReadOnlySpan<byte> bytes = input.First.Span;

                    // Parse RequestLine
                    int requestLineParsed;
                    HttpRequestLine requestLine;
                    if (!HttpRequestParser.TryParseRequestLine(bytes, out requestLine, out requestLineParsed))
                    {
                        complete = input.Length > 1000;
                        reader.Advance(input.Start, input.End);
                        continue;
                    }
                    bytes = bytes.Slice(requestLineParsed);

                    // Parse Headers
                    int headerParsed;
                    HttpHeadersSingleSegment headers;
                    if (!HttpRequestParser.TryParseHeaders(bytes, out headers, out headerParsed))
                    {
                        complete = input.Length > 1000;
                        reader.Advance(input.Start, input.End);
                        continue;
                    }

                    // We don't support a Body

                    // Succesfully parsed RequestLine and Header
                    reader.Advance(input.Move(input.Start, requestLineParsed + headerParsed));

                    // Respond
                    var output = writer.Alloc();
                    var formatter = new OutputFormatter<WritableBuffer>(output, TextEncoder.Utf8);
                    formatter.Append("HTTP/1.1 200 OK");
                    formatter.Append("\r\nContent-Length: 13");
                    formatter.Append("\r\nContent-Type: text/plain");
                    formatter.Format("\r\nDate: {0:R}", DateTime.UtcNow);
                    formatter.Append("\r\nServer: System.IO.Pipelines");
                    formatter.Append("\r\n\r\n");
                    // write body
                    formatter.Append("Hello, World!");

                    await output.FlushAsync();
                }
            }
            catch
            {}
            finally
            {
                reader.Complete();
                writer.Complete();
            }
        }
    }
}