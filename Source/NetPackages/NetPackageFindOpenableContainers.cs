using System.Diagnostics;
using System.Linq;

// Client => Server
// Requests a list of containers that are safe to modify
class NetPackageFindOpenableContainers : NetPackage
{
    public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;
    public override bool AllowedBeforeAuth => false;

    public NetPackageFindOpenableContainers Setup(int _playerEntityId, QuickStackType _type)
    {
        playerEntityId = _playerEntityId;
        type = _type;
        return this;
    }

    public override int GetLength()
    {
        return 5;
    }

    public override void ProcessPackage(World _world, GameManager _callbacks)
    {
        if (_world == null)
        {
            return;
        }

        if (type >= QuickStackType.Count || type < QuickStackType.Stack)
        {
            Log.Warning($"[QuickStack] Invalid Quickstack type { (int)type }");
            return;
        }
        if (!_world.Players.dict.TryGetValue(playerEntityId, out var playerEntity) || playerEntity == null)
        {
            Log.Warning("[QuickStack] Unable to find Sender player entity");
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        var lockedTileEntities = QuickStack.GetOpenedTiles();
        if (lockedTileEntities == null)
        {
            return;
        }

        var center = new Vector3i(playerEntity.position);
        var unlockedContainers = QuickStack.FindNearbyLootContainers(center)
            .Where(container => QuickStack.UserCanOpen(Sender.CrossplatformId, container))
            .ToList();

        var offsets = unlockedContainers
            .Select(container => container.ToWorldPos() - center)
            .ToList();

        if (offsets.Count > 0)
        {
            for (int i = 0; i < unlockedContainers.Count; ++i)
            {
                lockedTileEntities.Add(unlockedContainers[i], Sender.entityId);
            }

            Sender.SendPackage(
                NetPackageManager.GetPackage<NetPackageDoQuickStack>()
                    .Setup(center, offsets, type)
            );
        }

        Log.Out($"[QuickStack] Found containers for Client { Sender.CrossplatformId } in { stopwatch.ElapsedMilliseconds } ms");
    }

    public override void read(PooledBinaryReader _reader)
    {
        // ignore entity ID sent by client
        _ = _reader.ReadInt32();
        // use the NetPackage-provided one instead
        playerEntityId = Sender.entityId;
        type = (QuickStackType)_reader.ReadByte();
    }

    public override void write(PooledBinaryWriter _writer)
    {
        base.write(_writer);
        _writer.Write(playerEntityId);
        _writer.Write((byte)type);
    }

    protected int playerEntityId;
    protected QuickStackType type;
}
