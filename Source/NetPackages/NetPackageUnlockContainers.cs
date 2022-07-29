using System.Collections.Generic;
using System.Linq;

// Client => Server
// Notifies server that containers are no longer in-use
class NetPackageUnlockContainers : NetPackageInvManageAction
{
    public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

    public new NetPackageUnlockContainers Setup(Vector3i _center, List<Vector3i> _containerEntities)
    {
        _ = base.Setup(_center, _containerEntities);
        return this;
    }

    public override void ProcessPackage(World _world, GameManager _callbacks)
    {
        if (offsets == null || _world == null)
        {
            return;
        }

        var openContainers = QuickStack.GetOpenedTiles();
        if (openContainers == null)
        {
            return;
        }

        var containersToClose =
            from offset in offsets
            select _world.GetTileEntity(0, center + offset) into container where container != null
            where openContainers.TryGetValue(container, out int playerEntityId) && playerEntityId == Sender.entityId
            select container;

        foreach (var container in containersToClose)
        {
            openContainers.Remove(container);
        }
    }

}
