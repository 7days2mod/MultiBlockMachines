using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MultiBlockMachines
{
  public class API : ModApiAbstract
  {
    public static readonly string ModDir = $"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}{Path.DirectorySeparatorChar}";
    public static readonly string ConfigDir = $"{ModDir}Machines{Path.DirectorySeparatorChar}";

    private static readonly FileSystemWatcher _fsw = new FileSystemWatcher($"{ModDir}Machines", "*.json");
    private static void ProcessChangedConfig(object item, FileSystemEventArgs args)
    {
      Log.Out("(MBM) Reloading machines:");
      Machine.Machines.Clear();
      Machine.LoadConfig();
    }

    private static void AddFsWatch()
    {
      _fsw.Changed += ProcessChangedConfig;
      _fsw.Created += ProcessChangedConfig;
      _fsw.Deleted += ProcessChangedConfig;
      _fsw.EnableRaisingEvents = true;
    }

    public override void GameStartDone()
    {
      var world = GameManager.Instance.World;
      if (world == null) return;

      Machine.LoadConfig();
      AddFsWatch();
      world.ChunkCache.OnBlockChangedDelegates += Machine.OnBlockChanged;
    }
  }
}
