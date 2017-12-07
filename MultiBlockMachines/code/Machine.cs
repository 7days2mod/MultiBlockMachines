using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Random = System.Random;

namespace MultiBlockMachines
{
  [Serializable]
  public class Machine
  {
    public static Dictionary<int, Machine> Machines = new Dictionary<int, Machine>();

    public string SeederBlock;
    public string Blueprint;
    public string LayerDelay;
    public string WorkStationBlock;

    private int seederType;
    private Prefab blueprintPrefab;
    private int delay;
    private int workstationType;
    private Vector3i position;

    public int SeederType { get => seederType; set => seederType = value; }
    public Prefab BlueprintPrefab { get => blueprintPrefab; set => blueprintPrefab = value; }
    public int Delay { get => delay; set => delay = value; }
    public int WorkstationType { get => workstationType; set => workstationType = value; }
    public Vector3i Position { get => position; set => position = value; }

    public Machine Clone() => new Machine
    {
      SeederBlock = SeederBlock,
      Blueprint = Blueprint,
      LayerDelay = LayerDelay,
      WorkStationBlock = WorkStationBlock,
      SeederType = SeederType,
      BlueprintPrefab = BlueprintPrefab.Clone(),
      Delay = Delay,
      WorkstationType = WorkstationType
    };

    public override string ToString()
    {
      return $"{SeederBlock}:{Blueprint}";
    }

    public static void LoadConfig()
    {
      var files = GetFiles(API.ConfigDir);
      if (files != null)
      {
        foreach (var file in files)
        {
          if (file.Extension != ".json") continue;

          if (!File.Exists(file.FullName)) return;

          var machine = JsonUtility.FromJson<Machine>(File.ReadAllText(file.FullName));
          if (!machine.Init()) return;

          if (Machines.ContainsKey(machine.SeederType))
          {
            Log.Out($"(MBM) Machine already configured for seeder block {machine.SeederBlock} in {file.Name}");

            return;
          }
          Machines.Add(machine.SeederType, machine);
          Log.Out($"(MBM) Machine config parsed from {file.Name}: {machine}");
        }
      }
    }

    private static FileSystemInfo[] GetFiles(string path)
    {
      var root = new DirectoryInfo(path);
      if (!root.Exists) { return null; }
      var files = root.GetFileSystemInfos();
      return files;
    }

    public static void OnBlockChanged(Vector3i _blockpos, BlockValue _blockvalueold, BlockValue _blockvaluenew)
    {
      if (_blockvaluenew.type == 0 || !Machines.ContainsKey(_blockvaluenew.type)) return;

      SpawnPrefab(_blockpos, _blockvaluenew);
    }

    public bool Init()
    {
      SeederType = Block.GetBlockValue(SeederBlock).type;
      if (SeederType == 0)
      {
        Log.Out($"(MBM) Unable to parse SeederBlock value for {SeederBlock}");

        return false;
      }

      BlueprintPrefab = new Prefab();
      if (!BlueprintPrefab.Load(Blueprint))
      {
        Log.Out($"(MBM) Unable to parse Blueprint value for {Blueprint}");

        return false;
      }

      if (LayerDelay.Contains("ms"))
      {
        if (!int.TryParse(LayerDelay.Substring(0, LayerDelay.Length - 2), out delay))
        {
          Log.Out($"(MBM) Unable to parse LayerDelay value for {LayerDelay}");

          return false;
        }
      }
      else
      {
        if (!int.TryParse(LayerDelay.Contains("s") ? LayerDelay.Substring(0, LayerDelay.Length - 1) : LayerDelay, out delay))
        {
          Log.Out($"(MBM) Unable to parse LayerDelay value for {LayerDelay}");

          return false;
        }
        delay *= 1000;
      }

      WorkstationType = WorkStationBlock == null ? 0 : Block.GetBlockValue(WorkStationBlock).type;

      return true;
    }

    public void BlockPlaced(Vector3 pos)
    {
      Position = new Vector3i((int)Math.Floor(pos.x), (int)Math.Floor(pos.y), (int)Math.Floor(pos.z));
    }

    public static void SpawnPrefab(Vector3i pos, BlockValue bv)
    {
      var m = Machines[bv.type].Clone();

      //Parse Placeholders
      var map = LootContainer.lootPlaceholderMap;
      for (var px = 0; px < m.blueprintPrefab.size.x; px++)
      {
        for (var py = 0; py < m.blueprintPrefab.size.y; py++)
        {
          for (var pz = 0; pz < m.blueprintPrefab.size.z; pz++)
          {
            var bv1 = m.blueprintPrefab.GetBlock(px, py, pz);
            // LOOT PLACEHOLDERS
            if (bv1.type == 0) continue;

            var bvr = new BlockValue(map.Replace(bv1, new Random(Guid.NewGuid().GetHashCode())).rawData);
            if (bv1.type == bvr.type) continue;

            m.blueprintPrefab.SetBlock(px, py, pz, bvr);
          }
        }
      }

      //var m = Machines[bv.type].MemberwiseClone() as Machine;
      //todo: check if multidim and remove from parent if parent pos not == pos
      GameManager.Instance.World.SetBlockRPC(pos, BlockValue.Air);

      pos.y += m.BlueprintPrefab.yOffset;
      //todo: apply a location offset from config, or spawn in middle of facing edge (rather than centered)
      m.Position = new Vector3i(pos.x - m.BlueprintPrefab.size.x * 0.5f, pos.y, pos.z - m.BlueprintPrefab.size.z * 0.5f);
      m.BlueprintPrefab.RotateY(false, (m.BlueprintPrefab.rotationToFaceNorth + bv.rotation) % 4);

      //todo: add chunk observer so that if players leave area it will finish building
      ThreadManager.AddSingleTask(info =>
      {
        ClearBlocks(m.Position, m.BlueprintPrefab);
        for (var layer = 0; layer < m.BlueprintPrefab.size.y; layer++)
        {
          CopyIntoWorld(m.Position, m.BlueprintPrefab, layer);
          Thread.Sleep(m.Delay);
        }
      });
    }

    public static void CopyIntoWorld(Vector3i dest, Prefab prefab, int ylayer, int count = 1)
    {
      var world = GameManager.Instance.World;

      var _changes = new List<BlockChangeInfo>();
      for (var y = ylayer; y < ylayer + count; ++y)
      {
        for (var x = 0; x < prefab.size.x; ++x)
        {
          for (var z = 0; z < prefab.size.z; ++z)
          {
            var x1 = dest.x + x;
            var z1 = dest.z + z;
            var y1 = dest.y + y;

            var pBlockValue = prefab.GetBlock(x, y, z);
            var pBlock = Block.list[pBlockValue.type];
            if (pBlock == null || pBlockValue.ischild || pBlockValue.type == 0 && !prefab.bCopyAirBlocks) continue;

            //Terrain Filler
            if (Constants.cTerrainFillerBlockValue.type != 0 && pBlockValue.type == Constants.cTerrainFillerBlockValue.type)
            {
              var chunkSync = world.ChunkCache.GetChunkSync(World.toChunkXZ(dest.x), World.toChunkXZ(dest.z));

              var chunkX = World.toChunkXZ(x1);
              var chunkZ = World.toChunkXZ(z1);
              if (chunkSync == null || chunkSync.X != chunkX || chunkSync.Z != chunkZ)
              {
                chunkSync = world.ChunkCache.GetChunkSync(chunkX, chunkZ);
              }
              if (chunkSync == null) continue;

              var chunkBlock = chunkSync.GetBlock(World.toBlockXZ(x1), World.toBlockY(y1), World.toBlockXZ(z1));
              if (chunkBlock.type == 0 || Block.list[chunkBlock.type] == null || !Block.list[chunkBlock.type].shape.IsTerrain()) continue;

              pBlockValue = chunkBlock;
            }

            //Sleepers
            if (pBlockValue.Block.IsSleeperBlock)
            {
              pBlockValue = BlockValue.Air;
            }

            var density = prefab.GetDensity(x, y, z);
            _changes.Add(new BlockChangeInfo(x1, y1, z1, pBlockValue, pBlock.GetLightValue(pBlockValue) != 0)
            {
              density = density == 0 ? (!Block.list[pBlockValue.type].shape.IsTerrain() ? MarchingCubes.DensityAir : MarchingCubes.DensityTerrain) : density,
              bChangeDensity = true
            });
          }
        }
      }
      GameManager.Instance.SetBlocksRPC(_changes);
    }

    public static void ClearBlocks(Vector3i dest, Prefab prefab)
    {
      var world = GameManager.Instance.World;

      var chunkSync = world.ChunkCache.GetChunkSync(World.toChunkXZ(dest.x), World.toChunkXZ(dest.z));

      for (var x = 0; x < prefab.size.x; ++x)
      {
        for (var z = 0; z < prefab.size.z; ++z)
        {
          for (var y = 0; y < prefab.size.y; ++y)
          {
            var x1 = dest.x + x;
            var z1 = dest.z + z;
            var y1 = dest.y + y;

            var chunkX = World.toChunkXZ(x1);
            var chunkZ = World.toChunkXZ(z1);
            if (chunkSync == null || chunkSync.X != chunkX || chunkSync.Z != chunkZ)
            {
              chunkSync = world.ChunkCache.GetChunkSync(chunkX, chunkZ);
            }

            if (chunkSync == null) continue;

            var chunkBlock = chunkSync.GetBlock(World.toBlockXZ(x1), World.toBlockY(y1), World.toBlockXZ(z1));

            //CLEAR CLAIMSTONES
            if (chunkBlock.Block.IndexName == "lpblock")
            {
              GameManager.Instance.persistentPlayers.RemoveLandProtectionBlock(new Vector3i(x1, y1, z1));
            }

            //REMOVE PARENT OF MULTIDIM - IF NOT FROM PREFAB
            if (!chunkBlock.Block.isMultiBlock || !chunkBlock.ischild) continue;

            var parentPos = chunkBlock.Block.multiBlockPos.GetParentPos(new Vector3i(x1, y1, z1), chunkBlock);

            var parent = world.ChunkClusters[0].GetBlock(parentPos);
            if (parent.ischild || parent.type != chunkBlock.type) continue;

            world.ChunkClusters[0].SetBlock(parentPos, BlockValue.Air, false, false);
          }
        }
      }
    }
  }
}
