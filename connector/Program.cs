using Connector;
using vJoyInterfaceWrap;

Console.Write("Discovering...\r");
if (PSGTS2.Discover() is not PSGTS2 psgts2)
{
    Console.Error.WriteLine("PSGTS2 Guitar not found.");
    return;
}
Console.WriteLine($"PSGTS2 Guitar on port {psgts2.Port.PortName}");
vJoy vJoy = new();

if (!vJoy.vJoyEnabled())
{
    Console.Error.WriteLine("vJoy not installed, which is required.");
    return;
}

uint joyId = 1;

if (args.Length > 1 && !uint.TryParse(args[1], out joyId))
{
    Console.Error.WriteLine("Argument must be joy id (1-16)");
    Environment.Exit(1);
    return;
}

uint libraryVersion = 0;
uint driverVersion = 0;
if (!vJoy.DriverMatch(ref libraryVersion, ref driverVersion))
{
    Console.Error.WriteLine($"\x1B[33mWARNING\x1B[0m: Library-Driver mismatch.");
}

switch (vJoy.GetVJDStatus(joyId))
{
    case VjdStat.VJD_STAT_OWN:
    case VjdStat.VJD_STAT_FREE:
        break;
    case VjdStat.VJD_STAT_BUSY:
        Console.Error.WriteLine($"vJoy error: {joyId} is already owned by another feeder.");
        Environment.Exit(1);
        return;
    case VjdStat.VJD_STAT_MISS:
        Console.Error.WriteLine($"vJoy error: {joyId} is disabled.");
        Environment.Exit(1);
        return;
    case VjdStat.VJD_STAT_UNKN:
        Console.Error.WriteLine($"vJoy unknown error for {joyId}.");
        Environment.Exit(1);
        return;
    default:
        break;
}

if (!vJoy.GetVJDAxisExist(joyId, HID_USAGES.HID_USAGE_X))
{
    Console.Error.WriteLine("Re-configure vJoy, x-axis is required.");
    Environment.Exit(1);
    return;
}

if (vJoy.GetVJDButtonNumber(joyId) < 13)
{
    Console.Error.WriteLine("Re-configure vJoy, 13 buttons are required.");
    Environment.Exit(1);
    return;
}

if (!vJoy.AcquireVJD(joyId))
{
    Console.Error.WriteLine($"Failed to aquire device {joyId}.");
    Environment.Exit(1);
    return;
}

long xMin = 0;
long xMax = 0;
vJoy.GetVJDAxisMin(joyId, HID_USAGES.HID_USAGE_X, ref xMin);
vJoy.GetVJDAxisMax(joyId, HID_USAGES.HID_USAGE_X, ref xMax);

vJoy.JoystickState joyState = new();
TimeSpan consoleRefreshRate = TimeSpan.FromMilliseconds(30);
string[] dioNames = Enum.GetNames<PSGTS2.State.DIO>();
string[] aioNames = Enum.GetNames<PSGTS2.State.AIO>();
List<string> lines = new();
DateTime lastPrint = DateTime.MinValue;
DateTime lastFlush = DateTime.MinValue;
psgts2.StateReady += (sender, e) =>
{
    joyState.AxisX = (int)((xMax - xMin) * psgts2.CurrentState.Get(PSGTS2.State.AIO.Whammy) + xMin);
    joyState.Buttons = 0;

    for (int i = 0; i < PSGTS2.State.DIO_COUNT; i++)
    {
        if (!psgts2.CurrentState.Dio[i])
            continue;
        joyState.Buttons |= (uint)(1 << i);
    }

    if (!vJoy.UpdateVJD(joyId, ref joyState))
    {
        Console.Error.WriteLine($"\x1B[31mERROR\x1B[0m: Joystick update error.");
        Environment.Exit(1);
        return;
    }

    if (DateTime.Now - lastPrint < consoleRefreshRate || e.Flushed)
        return;

    List<string> oldLines = lines.ToList();
    lines.Clear();

    lines.Add($"Uptime: {psgts2.CurrentState.Uptime}");
    for (int i = 0; i < dioNames.Length; i++)
        lines.Add($"DIO {dioNames[i]}: {(psgts2.CurrentState.Dio[i] ? "Active" : "Inactive")}");
    for (int i = 0; i < aioNames.Length; i++)
        lines.Add($"AIO {aioNames[i]}: {psgts2.CurrentState.Aio[i]:F4}");
    if (e.Flushed)
        lastFlush = DateTime.Now;

    if (lastFlush != DateTime.MinValue)
        lines.Add($"Last Flush: {lastFlush}");

    for (int i = 0; i < oldLines.Count; i++)
    {
        if (i == lines.Count)
            break;
        if (oldLines[i].Length <= lines[i].Length)
            continue;
        for (int j = lines[i].Length; j < oldLines[i].Length; j++)
            lines[i] += ' ';
    }

    string output = "";
    if (oldLines.Count > 0)
        output += $"\x1B[{lines.Count}A";
    foreach (var line in lines)
        output += $"{line}{Environment.NewLine}";

    Console.Write(output);
    lastPrint = DateTime.Now;
};

while (psgts2.Port.IsOpen)
    Thread.Sleep(125);
