using System.Collections.Generic;
using System.Linq;

// Client => Server
// Requests a list of containers that are safe to modify
class NetPackageFindOpenableContainers : NetPackage
{
    public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;
    public override bool AllowedBeforeAuth => false;

    public NetPackageFindOpenableContainers Setup(QuickStackType _type)
    {
        type = _type;
        return this;
    }

    public override int GetLength()
    {
        return 1;
    }

    public override void ProcessPackage(World _world, GameManager _callbacks)
    {
        if (type >= QuickStackType.Count)
        {
            return;
        }

        if (_world == null || !_world.Players.dict.TryGetValue(Sender.entityId, out var playerEntity) || playerEntity == null)
        {
            return;
        }

        var lockedTileEntities = QuickStack.GetOpenedTiles();
        if (lockedTileEntities == null)
        {
            return;
        }

        List<Vector3i> offsets = new List<Vector3i>(256);
        var center = new Vector3i(playerEntity.position);
        var unlockedContainers = QuickStack.FindNearbyLootContainers(center)
            .Where(pair => QuickStack.UserCanOpen(Sender.CrossplatformId, pair.Item2));

        foreach ((Vector3i offset, TileEntity entity) in unlockedContainers)
        {
            offsets.Add(offset);
            lockedTileEntities.Add(entity, Sender.entityId);
        }

        if(offsets.Count == 0) { return; }

        Sender.SendPackage(NetPackageManager.GetPackage<NetPackageDoQuickStack>().Setup(center, offsets, type));
    }

    public override void read(PooledBinaryReader _reader)
    {
        type = (QuickStackType)_reader.ReadByte();
    }

    public override void write(PooledBinaryWriter _writer)
    {
        base.write(_writer);
        _writer.Write((byte)type);
    }

    protected QuickStackType type;
}
