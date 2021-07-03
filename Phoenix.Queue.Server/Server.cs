using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;

namespace Phoenix.Queue.Server
{
    public class Server : BaseScript
    {
        private const int QueueLimit = 200;
        private const int ServerLimit = 48;
        private readonly ConcurrentDictionary<string, int> _index = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, dynamic> _players = new ConcurrentDictionary<string, dynamic>();
        private readonly ConcurrentDictionary<string, int> _priorityIndex = new ConcurrentDictionary<string, int>();

        private readonly ConcurrentDictionary<string, dynamic> _priorityPlayers =
            new ConcurrentDictionary<string, dynamic>();

        private readonly ConcurrentQueue<string> _priorityQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private readonly ConcurrentDictionary<string, TimeSpan> _times = new ConcurrentDictionary<string, TimeSpan>();

        public Server()
        {
            EventHandlers["playerConnecting"] += new Action<Player, string, dynamic, dynamic>(PlayerConnecting);
            Tick += HandleQueue;
        }

        public async void PlayerConnecting([FromSource] Player source, string playerName, dynamic denyWithReason,
            dynamic deferrals)
        {
            try
            {
                // Set initial deferrals
                deferrals.defer();
                deferrals.update("Loading Queue...");
                await Delay(500);

                if (await CheckPriority(source.Identifiers["license"]))
                {
                    // Add to queue and various dictionaries
                    _priorityQueue.Enqueue(source.Identifiers["license"]);
                    _priorityIndex.TryAdd(source.Identifiers["license"], _priorityQueue.Count);
                    _priorityPlayers.TryAdd(source.Identifiers["license"], deferrals);

                    deferrals.update($"Priority - {_priorityQueue.Count}/{_priorityQueue.Count}... 0h 0m");
                }
                else
                {
                    // Check if queue is full, if so close deferral
                    if (_queue.Count >= QueueLimit)
                        deferrals.done("Queue is full, please try again later");
                    // Add to queue and various dictionaries
                    _queue.Enqueue(source.Identifiers["license"]);
                    _index.TryAdd(source.Identifiers["license"], _queue.Count);
                    _players.TryAdd(source.Identifiers["license"], deferrals);

                    deferrals.update($"{_queue.Count}/{_queue.Count}... 0h 0m");
                }

                _times.TryAdd(source.Identifiers["license"], new TimeSpan(0, 0, 0));
            }
            catch (Exception ex)
            {
                deferrals.done("An error has occurred");
                Debug.WriteLine($"Queue error: {ex}");
            }
        }

        public async Task HandleQueue()
        {
            try
            {
                // Ensure there's a slot open
                if (Players.Count() < ServerLimit)
                {
                    var priorityQueue = false;
                    // Check priority first
                    if (_priorityQueue.Count > 0)
                    {
                        // Try to get license
                        var dequeue = _priorityQueue.TryDequeue(out var license);
                        if (dequeue)
                        {
                            // Get deferrals
                            _priorityPlayers.TryGetValue(license, out var deferrals);
                            // Let player into server and update all other places
                            priorityQueue = true;
                            if (deferrals != null) deferrals.done();
                            _priorityIndex.TryRemove(license, out var priorityIndex);
                            _priorityPlayers.TryRemove(license, out var player);
                            _times.TryRemove(license, out var time);
                            foreach (var index in _priorityIndex) _priorityIndex[index.Key] = index.Value - 1;
                        }
                    }

                    if (!priorityQueue)
                    {
                        // Try to get license
                        var dequeue = _queue.TryDequeue(out var license);
                        if (dequeue)
                        {
                            // Get deferrals
                            _players.TryGetValue(license, out var deferrals);

                            // Let player into server and update all other places
                            if (deferrals != null) deferrals.done();
                            _index.TryRemove(license, out var priorityIndex);
                            _players.TryRemove(license, out var player);
                            _times.TryRemove(license, out var time);
                            foreach (var index in _index) _index[index.Key] = index.Value - 1;
                        }
                    }
                }

                UpdateTimes();
                UpdatePriorityDeferrals();
                UpdateNormalDeferrals();
                await Delay(1000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Queue error: {ex}");
            }
        }

        public void UpdateTimes()
        {
            // Update times
            foreach (var time in _times)
                _times[time.Key] = time.Value.Add(new TimeSpan(0, 0, 1));
        }

        public void UpdatePriorityDeferrals()
        {
            // Update deferral messages priority queue
            foreach (var deferral in _priorityPlayers)
            {
                _priorityIndex.TryGetValue(deferral.Key, out var number);
                _times.TryGetValue(deferral.Key, out var time);
                if (deferral.Value != null)
                    deferral.Value.update(
                        $"Priority - {number}/{_priorityQueue.Count}... {time.Hours}h {time.Minutes}m");
            }
        }

        public void UpdateNormalDeferrals()
        {
            // Update deferral messages normal queue
            foreach (var deferral in _players)
            {
                _index.TryGetValue(deferral.Key, out var number);
                _times.TryGetValue(deferral.Key, out var time);
                if (deferral.Value != null)
                    deferral.Value.update($"{number}/{_queue.Count}... {time.Hours}h {time.Minutes}m");
            }
        }

        public async Task<bool> CheckPriority(string license)
        {
            // todo setup some sort of store for priority
            return true;
        }
    }
}