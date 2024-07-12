using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Engine.RandomNumberGenerators.YG
{
    public class YggdrasilRandomNumberStreamAspnetHostedService : IHostedService
    {
        private readonly YggdrasilRandomNumberStream _randomNumberStream;

        public YggdrasilRandomNumberStreamAspnetHostedService(YggdrasilRandomNumberStream randomNumberStream)
        {
            _randomNumberStream = randomNumberStream;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _randomNumberStream.StartAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _randomNumberStream.StopAsync();
        }
    }
}