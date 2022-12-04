using System.Collections.Generic;

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

        var lockedTileEntities = QuickStack.GetOpenedTiles();
        if (lockedTileEntities == null)
        {
            return;
        }

        List<Vector3i> entityOffsets = new List<Vector3i>(256);

        var center = new Vector3i(playerEntity.position);
        foreach (var (offset, entity) in QuickStack.FindNearbyLootContainers(center, playerEntityId))
        {
            if (entity != null)
            {
                entityOffsets.Add(offset);
                lockedTileEntities.Add(entity, playerEntityId);
            }
        }

        if (entityOffsets.Count > 0)
        {
            Sender.SendPackage(NetPackageManager.GetPackage<NetPackageDoQuickStack>().Setup(center, entityOffsets, type));
        }
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
