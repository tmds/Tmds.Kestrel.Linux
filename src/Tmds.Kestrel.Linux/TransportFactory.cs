using System;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tmds.Kestrel.Linux
{
    public class TransportFactory : ITransportFactory
    {
        private TransportOptions _options;
        private ILoggerFactory _loggerFactory;
        public TransportFactory(IOptions<TransportOptions> options,
            ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }
            _options = options.Value;
            _loggerFactory = loggerFactory;
        }
        public ITransport Create(IEndPointInformation IEndPointInformation, IConnectionHandler handler)
        {
            return new Transport(IEndPointInformation, handler, _options, _loggerFactory);
        }
    }
}