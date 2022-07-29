﻿using System.Collections.Generic;
using System.Linq;

// Server => Client
// Informs client it is safe to quick stack/restock
// To a list of containers
class NetPackageDoQuickStack : NetPackageInvManageAction
{
    public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

    public NetPackageDoQuickStack Setup(Vector3i _center, List<Vector3i> _containerEntities, QuickStackType _type)
    {
        Setup(_center, _containerEntities);
        type = _type;
        return this;
    }

    public override int GetLength()
    {
        return base.GetLength() + 1;
    }

    public override void ProcessPackage(World _world, GameManager _callbacks)
    {
        if (offsets == null || _world == null || offsets.Count == 0)
        {
            return;
        }

        var lootContainers =
            from offset in offsets
            select _callbacks.World.GetTileEntity(0, center + offset) into tileEntity
            where tileEntity != null && tileEntity is TileEntityLootContainer
            select (TileEntityLootContainer)tileEntity;

        switch (type)
        {
            case QuickStackType.Stack:
                QuickStack.ClientMoveQuickStack(lootContainers);
                break;

            case QuickStackType.Restock:
                QuickStack.ClientMoveQuickRestock(lootContainers);
                break;

            default:
                break;
        }

        ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageUnlockContainers>().Setup(center, offsets));
    }

    public override void read(PooledBinaryReader _reader)
    {
        base.read(_reader);
        type = (QuickStackType)_reader.ReadByte();
    }

    public override void write(PooledBinaryWriter _writer)
    {
        base.write(_writer);
        _writer.Write((byte)type);
    }

    protected QuickStackType type;
}
