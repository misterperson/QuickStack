using System.Collections.Generic;
using System.Linq;

// Client => Server
// Notifies server that containers are no longer in-use
class NetPackageUnlockContainers : NetPackageInvManageAction
{
    public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

    public new NetPackageUnlockContainers Setup(Vector3i _center, List<Vector3i> _offsets)
    {
        _ = base.Setup(_center, _offsets);
        return this;
    }

    public override void ProcessPackage(World _world, GameManager _callbacks)
    {
        var openContainers = QuickStack.GetOpenedTiles();

        if(openContainers == null) { return; }

        var toRemove = offsets
            .Select(offset => _world.GetTileEntity(0, center + offset))
            .Where(entity => openContainers.TryGetValue(entity, out int openedBy) && openedBy == Sender.entityId);

        foreach (var container in toRemove)
        {
            openContainers.Remove(container);
        }
    }
}
