using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Unpark.Core.Startup;

/// <summary>
/// Planificateur de tâches via son interface COM documentée (Schedule.Service).
/// Ne liste que les tâches déclenchées à l'ouverture de session (TASK_TRIGGER_LOGON), hors dossier \Microsoft.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsLogonTaskApi : ILogonTaskApi
{
    private const int TaskEnumHidden = 1;
    private const int TriggerLogon = 9;
    private const int ActionExec = 0;

    public IReadOnlyList<LogonTask> List()
    {
        var result = new List<LogonTask>();
        var service = Connect();
        try { Walk(service.GetFolder("\\"), result); }
        finally { Marshal.FinalReleaseComObject(service); }
        return result;
    }

    public void SetEnabled(string path, bool enabled)
    {
        var service = Connect();
        try
        {
            var lastSlash = path.LastIndexOf('\\');
            var folder = service.GetFolder(lastSlash <= 0 ? "\\" : path[..lastSlash]);
            var task = folder.GetTask(path[(lastSlash + 1)..]);
            task.Enabled = enabled;
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }

    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Planificateur de tâches indisponible.");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }

    private static void Walk(dynamic folder, List<LogonTask> result)
    {
        string folderPath = folder.Path;
        if (folderPath.StartsWith("\\Microsoft", StringComparison.OrdinalIgnoreCase)) return;

        foreach (dynamic task in folder.GetTasks(TaskEnumHidden))
        {
            try
            {
                dynamic definition = task.Definition;
                var atLogon = false;
                foreach (dynamic trigger in definition.Triggers)
                    if ((int)trigger.Type == TriggerLogon) atLogon = true;
                if (!atLogon) continue;

                var command = "";
                foreach (dynamic action in definition.Actions)
                {
                    if ((int)action.Type != ActionExec) continue;
                    string args = action.Arguments ?? "";
                    command = $"{(string)action.Path} {args}".Trim();
                    break;
                }
                result.Add(new LogonTask((string)task.Path, (string)task.Name, command, (bool)task.Enabled));
            }
            catch
            {
                // Tâche illisible (droits, définition abîmée) : ignorée.
            }
        }
        foreach (dynamic sub in folder.GetFolders(0)) Walk(sub, result);
    }
}
