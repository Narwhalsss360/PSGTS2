using System.IO.Ports;

namespace Connector
{
    public class PSGTS2
    {
        public struct State()
        {
            public static readonly UInt32 MAGIC = 0xABC0FFEE;

            public enum DIO
            {
                Down,
                Up,
                Green,
                Red,
                Yellow,
                Blue,
                Orange,
                Select,
                Start,
                Left,
                Right,
                Tilt,
                Home
            }

            public enum AIO
            {
                Whammy,
                PickupSelector
            }

            public static readonly int DIO_COUNT = Enum.GetNames<DIO>().Length;

            public static readonly int AIO_COUNT = Enum.GetNames<AIO>().Length;

            public static readonly int SERIALIZED_SIZE =
                sizeof(UInt32) +
                sizeof(UInt64) +
                sizeof(bool) * DIO_COUNT +
                sizeof(float) * AIO_COUNT;

            public UInt64 Uptime = UInt64.MinValue;

            public bool[] Dio = new bool[DIO_COUNT];

            public float[] Aio = new float[AIO_COUNT];

            public bool Get(DIO dio)
                => Dio[(int)dio];

            public float Get(AIO aio)
                => Aio[(int)aio];

            public static State FromBytes(byte[] bytes)
            {
                if (bytes.Length < SERIALIZED_SIZE)
                    throw new ArgumentException("Not enough bytes", nameof(bytes));

                int offset = 0;
                if (BitConverter.ToUInt32(bytes, offset) != MAGIC)
                    throw new ArgumentException("Invalid bytes", nameof(bytes));
                offset += sizeof(UInt32);


                State state = new();
                state.Uptime = BitConverter.ToUInt64(bytes, offset);
                offset += sizeof(UInt64);

                for (int i = 0; i < DIO_COUNT; i++)
                {
                    state.Dio[i] = bytes[offset] > 0;
                    offset += sizeof(bool);
                }

                for (int i = 0; i < AIO_COUNT; i++)
                {
                    state.Aio[i] = BitConverter.ToSingle(bytes, offset);
                    offset += sizeof(float);
                }

                return state;
            }
        }

        public class StateReadyEventArgs(PSGTS2 psgts2, bool flushed)
        {
            public PSGTS2 PSGTS2 { get; init; } = psgts2;

            public bool Flushed { get; init; } = flushed;
        }

        const int BAUDRATE = 1000000;

        const bool DTR_ENABLE = true;

        const bool RTS_ENABLE = true;

        public static readonly TimeSpan FLUSH_THRESHOLD = TimeSpan.FromMilliseconds(100);

        public SerialPort Port = new()
        {
            BaudRate = BAUDRATE,
            DtrEnable = DTR_ENABLE,
            RtsEnable = RTS_ENABLE
        };

        public event EventHandler<StateReadyEventArgs>? StateReady = null;

        public State CurrentState { get; private set; } = new();

        private List<byte> _fragmented = new();

        private DateTime _startTime = DateTime.MinValue;

        public PSGTS2()
        {
            Port.DataReceived += DataReceived;
        }

        public PSGTS2(string portName)
            : this()
        {
            Open(portName);
        }

        public static PSGTS2? Discover(TimeSpan? timeout = null, string[]? ignorePorts = null)
        {
            ignorePorts ??= Array.Empty<string>();
            timeout ??= TimeSpan.FromMilliseconds(1500);
            const int openTimeout = 1000;
            List<Task<PSGTS2?>> tasks = new();
            foreach (string portName in SerialPort.GetPortNames())
            {
                if (ignorePorts.Contains(portName))
                    continue;

                tasks.Add(Task.Run(() =>
                {
                    PSGTS2 psgts2 = new();
                    Task opener = Task.Run(() => psgts2.Open(portName));
                    if (Task.WaitAny([opener], openTimeout) == -1)
                    {
                        psgts2.Port.Close();
                        psgts2.Port.Dispose();
                        return null;
                    }

                    if (opener.IsFaulted)
                        return null;

                    DateTime start = DateTime.Now;
                    while (DateTime.Now - start < timeout)
                    {
                        if (psgts2.CurrentState.Uptime != UInt64.MinValue)
                            return psgts2;
                        Thread.Sleep(10);
                    }
                    return null;
                }));
            }

            while (tasks.Count > 0)
            {
                int completed = Task.WaitAny(tasks.ToArray());
                if (tasks[completed].IsFaulted)
                    tasks.RemoveAt(completed);
                else if (tasks[completed].Result is null)
                    tasks.RemoveAt(completed);
                else
                    return tasks[completed].Result;
            }

            return null;
        }

        public void Open(string portName)
        {
            Port.PortName = portName;
            Port.Open();
        }

        public void Close()
            => Port.Close();

        private void OnStateReady(byte[] bytes)
        {
            _fragmented.AddRange(bytes[..(State.SERIALIZED_SIZE - _fragmented.Count)]);
            CurrentState = State.FromBytes(_fragmented.ToArray());
            _fragmented.Clear();

            bool flushed = false;
            if (_startTime == DateTime.MinValue)
            {
                _startTime = DateTime.Now - TimeSpan.FromMicroseconds(CurrentState.Uptime);
            }
            else if (DateTime.Now - (_startTime + TimeSpan.FromMicroseconds(CurrentState.Uptime)) < FLUSH_THRESHOLD)
            {
                flushed = false;
                Port.ReadExisting();
            }

            StateReady?.Invoke(this, new(this, flushed));

        }

        private void DataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            if (e.EventType == SerialData.Eof)
                return;

            int toRead = Port.BytesToRead;
            if (toRead - _fragmented.Count < State.SERIALIZED_SIZE)
                return;

            byte[] bytes = new byte[toRead];
            Port.Read(bytes, 0, bytes.Length);

            if (_fragmented.Count == 0)
            {
                int i = 0;
                for (i = bytes.Length - 1 - sizeof(UInt32); i >= 0; i--)
                {
                    if (BitConverter.ToUInt32(bytes, i) == State.MAGIC)
                        break;
                }
                if (i < 0)
                    return;

                if (bytes.Length - i < State.SERIALIZED_SIZE)
                {
                    _fragmented.AddRange(bytes[i..]);
                    return;
                }

                OnStateReady(bytes[i..]);
                return;
            }

            OnStateReady(_fragmented.ToArray());
        }
    }
}
