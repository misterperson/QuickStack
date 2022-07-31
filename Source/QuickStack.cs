using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using System.Linq;

internal enum QuickStackType : byte 
{
    Stack = 0,
    Restock,
    Count
}

internal class QuickStack 
{
    public static float[] lastClickTimes = new float[(int)QuickStackType.Count];

    private static int stackRadius = 7;

    public static XUiC_Backpack playerBackpack;
    public static XUiC_BackpackWindow backpackWindow;
    public static XUiC_ContainerStandardControls playerControls;
    public static readonly int customLockEnum = (int)XUiC_ItemStack.LockTypes.Burning + 1; //XUiC_ItemStack.LockTypes - Last used is Burning with value 5, so we use 6 for our custom locked slots

    internal static Dictionary<TileEntity, int> GetOpenedTiles()
    {
        return Traverse.Create(GameManager.Instance).Field("lockedTileEntities").GetValue<Dictionary<TileEntity, int>>();
    }

    // Checks if a loot container is openable by a player
    // Callable only by Dedicated Server/Host
    internal static bool UserCanOpen(PlatformUserIdentifierAbs _userId, TileEntity _tileEntity)
    {
        if (ConnectionManager.Instance.IsClient ||
            _userId == null || _tileEntity == null)
        {
            return false;
        }

        if ((_tileEntity is TileEntitySecureLootContainer lootContainer) && 
            lootContainer.IsLocked() && !lootContainer.IsUserAllowed(_userId))
        {
            return false;
        }

        return true;
    }

    internal static bool IsValidLoot(TileEntityLootContainer _tileEntity)
    {
        return (_tileEntity.GetTileEntityType() == TileEntityType.Loot ||
            _tileEntity.GetTileEntityType() == TileEntityType.SecureLoot ||
            _tileEntity.GetTileEntityType() == TileEntityType.SecureLootSigned);
    }

    // Checks if a loot container is currently open
    internal static bool ContainerIsNotInUse(TileEntity _tileEntity)
    {
        var openTileEntities = GetOpenedTiles();

        // Handle in-use containers
        if (openTileEntities != null && openTileEntities.TryGetValue(_tileEntity, out int playerEntityId) &&
            (GameManager.Instance.World.GetEntity(playerEntityId) is EntityAlive entityAlive) &&
            !entityAlive.IsDead())
        {
            return false;
        }

        return true;
    }

    // Yields all openable loot containers in a cubic radius about a point
    internal static IEnumerable<(Vector3i, TileEntityLootContainer)> FindNearbyLootContainers(Vector3i _center)
    {
        stackRadius = Mathf.Clamp(stackRadius, 0, sbyte.MaxValue);
        for (int i = -stackRadius; i <= stackRadius; i++)
        {
            for (int j = -stackRadius; j <= stackRadius; j++)
            {
                for (int k = -stackRadius; k <= stackRadius; k++)
                {
                    var offset = new Vector3i(i, j, k);
                    if (!(GameManager.Instance.World.GetTileEntity(0, _center + offset) is TileEntityLootContainer tileEntity) || tileEntity == null)
                    {
                        continue;
                    }

                    if (IsValidLoot(tileEntity) && ContainerIsNotInUse(tileEntity))
                    {
                        yield return (offset, tileEntity);
                    }
                }
            }
        }
    }

    // Gets the EItemMoveKind for the current move type based on the last time that move type was requested
    internal static XUiM_LootContainer.EItemMoveKind GetMoveKind(QuickStackType _type)
    {
        if (_type >= QuickStackType.Count)
        {
            throw new ArgumentOutOfRangeException();
        }

        // Assigns a value to a location, returning the old value at the location
        float Exchange(ref float location, float value)
        {
            float old = location;
            location = value;
            return old;
        }

        int idx = (int)_type;
        float lastClickTime = Exchange(ref lastClickTimes[idx], Time.unscaledTime);

        // if we last tried the same stash type and
        // we tried it within the last 2 seconds
        if (lastClickTimes[1 - idx] < lastClickTime && Time.unscaledTime - lastClickTime < 2.0f)
        {
            return XUiM_LootContainer.EItemMoveKind.FillAndCreate;
        } else
        {
            return XUiM_LootContainer.EItemMoveKind.FillOnly;
        }
    }

    public static void MoveQuickStack(IEnumerable<TileEntityLootContainer> _entityContainers)
    {
        if (backpackWindow.xui.lootContainer != null && backpackWindow.xui.lootContainer.entityId == -1)
        {
            return;
        }
        if (_entityContainers == null)
        {
            return;
        }

        var moveKind = GetMoveKind(QuickStackType.Stack);

        int lockedSlots = Traverse.Create(playerControls).Field("stashLockedSlots").GetValue<int>();

        foreach (var tileEntity in _entityContainers)
        {
            var (_, modified) = StashItems(playerBackpack, tileEntity, lockedSlots, moveKind, playerControls.MoveStartBottomRight);
            if (modified)
            {
                tileEntity.SetModified();
            }
        }
    }

    public static void MoveQuickRestock(IEnumerable<TileEntityLootContainer> _entityContainers)
    {
        if (backpackWindow.xui.lootContainer != null && backpackWindow.xui.lootContainer.entityId == -1)
        {
            return;
        }
        if (_entityContainers == null)
        {
            return;
        }

        XUiM_LootContainer.EItemMoveKind moveKind = GetMoveKind(QuickStackType.Restock);
        EntityPlayerLocal primaryPlayer = GameManager.Instance.World.GetPrimaryPlayer();
        LocalPlayerUI playerUI = LocalPlayerUI.GetUIForPlayer(primaryPlayer);
        int lockedSlots = Traverse.Create(playerControls).Field("stashLockedSlots").GetValue<int>();
        XUiC_LootWindowGroup lootWindowGroup = (XUiC_LootWindowGroup)((XUiWindowGroup)playerUI.windowManager.GetWindow("looting")).Controller;
        XUiC_LootWindow lootWindow = Traverse.Create(lootWindowGroup).Field("lootWindow").GetValue<XUiC_LootWindow>();
        XUiC_LootContainer lootContainer = Traverse.Create(lootWindow).Field("lootContainer").GetValue<XUiC_LootContainer>();

        foreach (var tileEntity in _entityContainers)
        {
            lootWindowGroup.SetTileEntityChest("QUICKSTACK", tileEntity);
            var (_, modified) = StashItems(lootContainer, primaryPlayer.bag, lockedSlots, moveKind, playerControls.MoveStartBottomRight);
            if (modified)
            {
                tileEntity.SetModified();
            }
        }
    }

    
    // Attempts to transfer all stackable items from the source grid to the destination inventory
    // _moveKind indicates the criteria for filling new inventory slots in the destination
    // first flag is true if the source container was emptied
    // second flag is true if any item transfer took place
    public static (bool, bool) StashItems(XUiC_ItemStackGrid _srcGrid, IInventory _dstInventory, int _ignoredSlots, XUiM_LootContainer.EItemMoveKind _moveKind, bool _startBottomRight)
    {
        if (_srcGrid == null || _dstInventory == null)
        {
            return (false, false);
        }

        XUiController[] itemStackControllers = _srcGrid.GetItemStackControllers();

        bool sourceEmptied = true;
        bool containersModified = false;

        int num = _startBottomRight ? (itemStackControllers.Length - 1) : _ignoredSlots;
        while (_startBottomRight ? (num >= _ignoredSlots) : (num < itemStackControllers.Length))
        {
            XUiC_ItemStack xuiC_ItemStack = (XUiC_ItemStack)itemStackControllers[num];
            if (!xuiC_ItemStack.StackLock && Traverse.Create(xuiC_ItemStack).Field("lockType").GetValue<int>() != customLockEnum)
            {
                ItemStack itemStack = xuiC_ItemStack.ItemStack;
                if (!xuiC_ItemStack.ItemStack.IsEmpty())
                {
                    int count = itemStack.count;
                    _dstInventory.TryStackItem(0, itemStack);
                    if (itemStack.count > 0 && (_moveKind == XUiM_LootContainer.EItemMoveKind.All || (_moveKind == XUiM_LootContainer.EItemMoveKind.FillAndCreate && _dstInventory.HasItem(itemStack.itemValue))) && _dstInventory.AddItem(itemStack))
                    {
                        itemStack = ItemStack.Empty.Clone();
                    }
                    if (itemStack.count == 0)
                    {
                        itemStack = ItemStack.Empty.Clone();
                    } else
                    {
                        sourceEmptied = false;
                    }

                    if (count != itemStack.count)
                    {
                        xuiC_ItemStack.ForceSetItemStack(itemStack);
                        containersModified = true;
                    }
                }
            }
            num = (_startBottomRight ? (num - 1) : (num + 1));
        }

        return (sourceEmptied, containersModified);
    }

    // UI Delegate
    public static void QuickInventoryOnClick(QuickStackType _type)
    {
        if(_type >= QuickStackType.Count)
        {
            throw new ArgumentOutOfRangeException();
        }
        
        // Multiplayer Client
        if (ConnectionManager.Instance.IsClient)
        {
            // Sets off a chain of NetPackages:
            // Client => Server       Server => Client Client => Server
            // FindOpenableContainers   DoQuickStack   UnlockContainers
            ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageFindOpenableContainers>().Setup(_type));
        }
        // Multiplayer Host or SinglePlayer
        else if (!GameManager.IsDedicatedServer)
        {
            var center = new Vector3i(GameManager.Instance.World.GetPrimaryPlayer().position);

            var lootContainers = FindNearbyLootContainers(center)
                .Select(pair => pair.Item2);

            if (!ConnectionManager.Instance.IsSinglePlayer)
            {
                lootContainers = lootContainers
                    .Where(container => UserCanOpen(GameManager.Instance.persistentLocalPlayer.UserIdentifier, container));
            }

            if (_type == QuickStackType.Restock)
            {
                MoveQuickRestock(lootContainers);
            } else
            {
                MoveQuickStack(lootContainers);
            }
        } 
    }
}
