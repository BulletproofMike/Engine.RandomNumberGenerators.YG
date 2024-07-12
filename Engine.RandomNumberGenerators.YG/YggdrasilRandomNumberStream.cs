using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Engine.GameServer.RandomNumbers;

namespace Engine.RandomNumberGenerators.YG
{
    public sealed class YggdrasilRandomNumberStream : IRandomNumberStream, IDisposable
    {
        private readonly HttpClient _client;
        private Task _refillCacheTask;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly AsyncAutoResetEvent _numberRequestedEvent = new();
        private readonly AsyncManualResetEvent _numbersRefilledEvent = new();
        private readonly ConcurrentQueue<uint> _randomNumberQueue;
        private const int TargetCacheLength = 100000;
        private const int MinimumCacheLength = 50000;
        private const int RandomNumbersToRequest = 1000;

        public YggdrasilRandomNumberStream(
            HttpMessageHandler handler,
            string baseAddress,
            int gameId)
        {
            _randomNumberQueue = new ConcurrentQueue<uint>();
            _client = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseAddress),
                Timeout = TimeSpan.FromSeconds(5)
            };
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client.DefaultRequestHeaders.Add("X-Game-ID", $"{gameId}");
        }

        public int BitsPerNumber { get; } = 31;

        public void Dispose()
        {
            _client?.Dispose();
            _cancellationTokenSource?.Dispose();
        }

        public Task StartAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _refillCacheTask = RefillCache(_cancellationTokenSource.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            try
            {
                _cancellationTokenSource.Cancel();

                await _refillCacheTask;
            }
            finally
            {
                _cancellationTokenSource.Dispose();
            }
        }

        public async ValueTask<uint> GetNext()
        {
            if (TryDequeueNumber(out var number))
                return number;

            using var cancellationTokenSource = new CancellationTokenSource(5000);

            while (true)
            {
                try
                {
                    await _numbersRefilledEvent.WaitAsync(cancellationTokenSource.Token);

                    if (TryDequeueNumber(out number))
                        return number;
                }
                catch (OperationCanceledException)
                {
                    throw new RandomNumberException("random number generator not available");
                }
            }
        }

        private bool TryDequeueNumber(out uint number)
        {
            if (_randomNumberQueue.Count < MinimumCacheLength)
            {
                _numberRequestedEvent.Set();
            }
            return _randomNumberQueue.TryDequeue(out number);
        }

        private async Task RefillCache(CancellationToken cancellationToken)
        {
            for (; ; )
            {
                try
                {
                    while (_randomNumberQueue.Count >= TargetCacheLength)
                    {
                        await _numberRequestedEvent.WaitAsync(cancellationToken);
                    }

                    using var request =
                        new HttpRequestMessage(HttpMethod.Get, $"numbers/?size={RandomNumbersToRequest}");

                    using var response = await _client.SendAsync(request, cancellationToken);
                    var content = await response.Content.ReadAsStreamAsync(cancellationToken);

                    var numbers =
                        await JsonSerializer.DeserializeAsync<uint[]>(content, cancellationToken: cancellationToken);

                    if (numbers != null)
                    {
                        for (var i = 0; i < numbers.Length; i++)
                        {
                            _randomNumberQueue.Enqueue(numbers[i]);
                        }

                        if (_randomNumberQueue.Count > MinimumCacheLength)
                        {
                            _numbersRefilledEvent.Set();
                            _numbersRefilledEvent.Reset();
                        }
                    }
                }
                catch { /* ignore */ }

                if (cancellationToken.IsCancellationRequested)
                    return;
            }
        }
    }
}