using Connector;

Console.Write("Discovering...\r");
if (PSGTS2.Discover() is not PSGTS2 psgts2)
{
    Console.Error.WriteLine("PSGTS2 Guitar not found.");
    return;
}
Console.WriteLine($"PSGTS2 Guitar on port {psgts2.Port.PortName}");

TimeSpan consoleRefreshRate = TimeSpan.FromMilliseconds(30);
string[] dioNames = Enum.GetNames<PSGTS2.State.DIO>();
string[] aioNames = Enum.GetNames<PSGTS2.State.AIO>();
List<string> lines = new();
DateTime lastPrint = DateTime.MinValue;
DateTime lastFlush = DateTime.MinValue;
psgts2.StateReady += (sender, e) =>
{
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

while (psgts2.Port.IsOpen);

