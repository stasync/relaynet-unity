namespace Keel.RelayNet.Debug
{
    /// <summary>
    /// Turns a session's cumulative counters into what the debug tools show: totals since the last reset, rates over
    /// the last interval, and a minute of rate history for the traffic graph. The counters only grow, so the rates
    /// come from sampling them a few times a second.
    /// </summary>
    internal sealed class NetStatisticsSampler
    {
        /// <summary>
        /// Short enough to react, long enough not to turn the rates into noise.
        /// </summary>
        private const double SAMPLE_INTERVAL_SECONDS = 0.5d;

        /// <summary>
        /// A minute of samples.
        /// </summary>
        public const int HISTORY_LENGTH = 120;

        public NetSession Session { get; private set; }

        /// <summary>
        /// The traffic since the session started, or since the last <see cref="Reset"/>.
        /// </summary>
        public NetTransferStatistics TransferTotals => Session == null ? default : Session.Statistics - _transferBaseline;
        public NetPayloadStatistics PayloadTotals => Session == null ? default : Session.PayloadStatistics - _payloadBaseline;

        /// <summary>
        /// The traffic of the last interval, and how long it was. Divide one by the other for a rate, see <see cref="PerSecond"/>.
        /// </summary>
        public NetTransferStatistics LastTransfer { get; private set; }
        public NetPayloadStatistics LastPayload { get; private set; }
        public double LastIntervalSeconds { get; private set; }

        /// <summary>
        /// How many samples of history there are, up to <see cref="HISTORY_LENGTH"/>, and the highest rate among them.
        /// </summary>
        public int HistoryCount { get; private set; }
        public float HistoryMaxRate { get; private set; }

        private readonly float[] _sentRates = new float[HISTORY_LENGTH];
        private readonly float[] _receivedRates = new float[HISTORY_LENGTH];
        private int _historyStart;

        private NetTransferStatistics _transferBaseline;
        private NetPayloadStatistics _payloadBaseline;
        private NetTransferStatistics _lastTransferSample;
        private NetPayloadStatistics _lastPayloadSample;
        private double _lastSampleTime = -1d;

        /// <summary>
        /// Samples the session once the interval has passed. Returns true when it did, so the caller knows the numbers changed.
        /// </summary>
        public bool Sample(NetSession session, double time)
        {
            // A new session starts its own counters: nothing sampled from the previous one means anything now.
            if (!ReferenceEquals(session, Session))
            {
                Session = session;
                Clear();
            }

            if (session == null || (_lastSampleTime >= 0d && time - _lastSampleTime < SAMPLE_INTERVAL_SECONDS))
                return false;

            var transfer = session.Statistics;
            var payload = session.PayloadStatistics;

            // The first sample has nothing to compare against: it only sets the starting point.
            if (_lastSampleTime >= 0d)
            {
                LastTransfer = transfer - _lastTransferSample;
                LastPayload = payload - _lastPayloadSample;
                LastIntervalSeconds = time - _lastSampleTime;
                AddToHistory(PerSecond(LastTransfer.BytesSent), PerSecond(LastTransfer.BytesReceived));
            }

            _lastTransferSample = transfer;
            _lastPayloadSample = payload;
            _lastSampleTime = time;
            return true;
        }

        /// <summary>
        /// Starts the totals over from now, to measure a single action. The rates and their history go on.
        /// </summary>
        public void Reset()
        {
            if (Session == null)
                return;

            _transferBaseline = Session.Statistics;
            _payloadBaseline = Session.PayloadStatistics;
        }

        /// <summary>
        /// A count from the last interval, per second, or 0 before there's an interval.
        /// </summary>
        public float PerSecond(int count) =>
            LastIntervalSeconds <= 0d ? 0f : (float)(count / LastIntervalSeconds);

        /// <summary>
        /// The bytes sent and received per second, <paramref name="index"/> samples after the oldest one kept.
        /// </summary>
        public void GetHistory(int index, out float sent, out float received)
        {
            var slot = (_historyStart + index) % HISTORY_LENGTH;
            sent = _sentRates[slot];
            received = _receivedRates[slot];
        }

        private void AddToHistory(float sent, float received)
        {
            var slot = (_historyStart + HistoryCount) % HISTORY_LENGTH;
            if (HistoryCount < HISTORY_LENGTH)
                HistoryCount++;
            else
                _historyStart = (_historyStart + 1) % HISTORY_LENGTH;

            _sentRates[slot] = sent;
            _receivedRates[slot] = received;

            HistoryMaxRate = 0f;
            for (var i = 0; i < HistoryCount; i++)
            {
                GetHistory(i, out var sentRate, out var receivedRate);
                if (sentRate > HistoryMaxRate)
                    HistoryMaxRate = sentRate;
                if (receivedRate > HistoryMaxRate)
                    HistoryMaxRate = receivedRate;
            }
        }

        private void Clear()
        {
            _transferBaseline = default;
            _payloadBaseline = default;
            _lastTransferSample = default;
            _lastPayloadSample = default;
            _lastSampleTime = -1d;
            LastTransfer = default;
            LastPayload = default;
            LastIntervalSeconds = 0d;
            HistoryCount = 0;
            HistoryMaxRate = 0f;
            _historyStart = 0;
        }
    }
}
