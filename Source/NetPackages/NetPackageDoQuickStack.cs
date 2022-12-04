using System.Collections.Generic;
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

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var lootContainers = offsets
            .Select(offset => _callbacks.World.GetTileEntity(0, center + offset) as TileEntityLootContainer)
            .Where(container => container != null)
            .ToArray();

        switch (type)
        {
            case QuickStackType.Stack:
                QuickStack.MoveQuickStack(lootContainers);
                break;

            case QuickStackType.Restock:
                QuickStack.MoveQuickRestock(lootContainers);
                break;

            default:
                Log.Warning($"[QuickStack] Unknown QuickStack type { (int)type }");
                break;
        }

        ConnectionManager.Instance.SendToServer(
            NetPackageManager.GetPackage<NetPackageUnlockContainers>()
                .Setup(center, offsets)
        );

        Log.Out($"[QuickStack] Processed { type } in { stopwatch.ElapsedMilliseconds } ms");
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
