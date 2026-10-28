using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ECommons.DalamudServices;

namespace GoldTyper.Game;

public static class AutomationLog
{
    private static readonly Queue<string> entries = new();
    public static string FilePath => Path.Combine(Plugin.P.PluginInterface.GetPluginConfigDirectory(), "automation.log");
    public static string Recent => string.Join(Environment.NewLine, entries);
    public static string WriteError { get; private set; } = string.Empty;

    public static void Write(string message, bool error = false)
    {
        if (!error && !Plugin.Configuration.调试日志)
            return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{(error ? "ERROR" : "DEBUG")}] {message}";
        entries.Enqueue(line);
        while (entries.Count > 200)
            entries.Dequeue();
        if (error)
            Svc.Log.Error(message);
        else
            Svc.Log.Debug(message);
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                File.Move(path, path + ".previous", true);
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            WriteError = string.Empty;
        }
        catch (Exception ex)
        {
            WriteError = ex.Message;
        }
    }
}
