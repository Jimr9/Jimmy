using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace WSJTX_Controller
{
    // Last-known reception report for one watched callsign.
    public class SpotInfo
    {
        public string Band;
        public string Mode;
        public DateTime UtcTime;
        public string SpotterCall;
        public string SpotterGrid;
    }

    // Watches the PSKReporter live-spot MQTT feed (mqtt.pskreporter.info, no auth/registration)
    // for a small, user-curated set of callsigns and keeps each one's most recent reception
    // report. Push-based (MQTT subscribe), not polled -- watching many callsigns at once
    // carries no per-request rate-limit risk, unlike PSKReporter's HTTP query API, which is
    // exactly why this exists instead of that (see project decision, 2026-07-07: DX Spot
    // Watch investigation, started from wanting "last spotted" info for 13 Colonies chasing).
    //
    // All MQTTnet callbacks run on a background thread. Updated fires there too -- subscribers
    // must marshal back to the UI thread (e.g. Control.BeginInvoke) before touching any control.
    public class DxSpotWatcher : IDisposable, ILookupProvider
    {
        private const string Broker = "mqtt.pskreporter.info";

        public string SourceName => "DX Spot Watch";
        public bool   IsEnabled  => true;

        private readonly IManagedMqttClient _client;
        private readonly Dictionary<string, SpotInfo> _lastSpots = new Dictionary<string, SpotInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _subscribedCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        // Raised whenever any watched call's last-seen data changes, or the watch list itself
        // changes. Fires on a background thread -- see class remarks above.
        public event Action Updated;

        public DxSpotWatcher()
        {
            _client = new MqttFactory().CreateManagedMqttClient();
            _client.ApplicationMessageReceivedAsync += OnMessageReceived;
        }

        // Reconciles the live MQTT subscriptions against the desired watch list: subscribes to
        // newly-added calls, unsubscribes removed ones, connects if not yet connected and the
        // list is non-empty, and fully disconnects when the list becomes empty (no connection
        // held open for nothing to watch). Safe to call repeatedly (e.g. every Options save).
        public async void UpdateWatchList(HashSet<string> calls)
        {
            try
            {
                var desired = calls ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (desired.Count == 0)
                {
                    if (_client.IsStarted) await _client.StopAsync();
                    lock (_lock) { _subscribedCalls.Clear(); _lastSpots.Clear(); }
                    Updated?.Invoke();
                    return;
                }

                if (!_client.IsStarted)
                {
                    var options = new ManagedMqttClientOptionsBuilder()
                        .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                        .WithClientOptions(new MqttClientOptionsBuilder()
                            .WithClientId(Guid.NewGuid().ToString())
                            .WithTcpServer(Broker)
                            .Build())
                        .Build();
                    await _client.StartAsync(options);
                }

                List<string> toAdd, toRemove;
                lock (_lock)
                {
                    toAdd    = desired.Except(_subscribedCalls, StringComparer.OrdinalIgnoreCase).ToList();
                    toRemove = _subscribedCalls.Except(desired, StringComparer.OrdinalIgnoreCase).ToList();
                }

                foreach (var call in toAdd)
                {
                    await _client.SubscribeAsync(new[] { new MqttTopicFilterBuilder().WithTopic(TopicFor(call)).Build() });
                    lock (_lock) { _subscribedCalls.Add(call); }
                }
                foreach (var call in toRemove)
                {
                    await _client.UnsubscribeAsync(TopicFor(call));
                    lock (_lock) { _subscribedCalls.Remove(call); _lastSpots.Remove(call); }
                }

                Updated?.Invoke();
            }
            catch
            {
                // Best-effort background feature -- a broker hiccup must never take down the
                // rest of Jimmy. UpdateWatchList will be retried on the next Options save, and
                // ManagedMqttClient's own auto-reconnect covers a mid-session drop.
            }
        }

        // Sender = watched call, any band/mode/receiver -- matches the documented PSKReporter
        // MQTT topic scheme (see M0LTE/pskr-mqtt-listener-example).
        private static string TopicFor(string call) => $"pskr/filter/v2/+/+/{call}/#";

        private Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs arg)
        {
            try
            {
                string json = arg.ApplicationMessage.ConvertPayloadToString();
                if (!(_json.DeserializeObject(json) is Dictionary<string, object> dict)) return Task.CompletedTask;

                string sender = dict.TryGetValue("sc", out var scObj) ? scObj as string : null;
                if (string.IsNullOrEmpty(sender)) return Task.CompletedTask;

                var spot = new SpotInfo
                {
                    Band        = dict.TryGetValue("b",  out var bObj)  ? bObj  as string : null,
                    Mode        = dict.TryGetValue("md", out var mdObj) ? mdObj as string : null,
                    SpotterCall = dict.TryGetValue("rc", out var rcObj) ? rcObj as string : null,
                    SpotterGrid = dict.TryGetValue("rl", out var rlObj) ? rlObj as string : null,
                    UtcTime     = dict.TryGetValue("t",  out var tObj)  ? UnixToUtc(Convert.ToInt64(tObj)) : DateTime.UtcNow,
                };

                bool changed;
                lock (_lock)
                {
                    // Only watched calls are ever subscribed to, but confirm before recording --
                    // a stray retained/late message for a just-unsubscribed call must not revive it.
                    if (!_subscribedCalls.Contains(sender)) return Task.CompletedTask;
                    changed = !_lastSpots.TryGetValue(sender, out var existing) || spot.UtcTime >= existing.UtcTime;
                    if (changed) _lastSpots[sender] = spot;
                }
                if (changed) Updated?.Invoke();
            }
            catch
            {
                // Malformed/unexpected payload -- skip this spot rather than crash the MQTT loop.
            }
            return Task.CompletedTask;
        }

        private static DateTime UnixToUtc(long unixSeconds) =>
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds);

        // Snapshot for rendering: one entry per currently-watched call, alphabetical -- a
        // screen-reader-navigated list should have a stable order, not reshuffle every time one
        // entry updates. SpotInfo is null for calls not yet seen this session.
        public List<KeyValuePair<string, SpotInfo>> Snapshot()
        {
            lock (_lock)
            {
                var calls = new List<string>(_subscribedCalls);
                calls.Sort(StringComparer.OrdinalIgnoreCase);
                return calls
                    .Select(c => new KeyValuePair<string, SpotInfo>(c, _lastSpots.TryGetValue(c, out var s) ? s : null))
                    .ToList();
            }
        }

        // Dictionary lookup only, already lock-protected -- safe for the
        // per-decode hot path. Only ever contributes for calls actually on the
        // watch list; a no-op for everything else.
        public void Contribute(LookupRecord record, string call)
        {
            SpotInfo spot;
            lock (_lock)
            {
                if (!_lastSpots.TryGetValue(call, out spot)) return;
            }
            record.LastSpot = spot;
            record.Sources.Add(SourceName);
        }

        public void Dispose()
        {
            _client.ApplicationMessageReceivedAsync -= OnMessageReceived;
            _client.Dispose();
        }
    }
}
