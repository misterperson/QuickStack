using HarmonyLib;
using QuickStackExtensions;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

public enum QuickStackType : byte
{
    Stack = 0,
    Restock,
    Count
}

internal class QuickStack
{
    public static float[] lastClickTimes = new float[(int)QuickStackType.Count];
    public static readonly int stackRadius = 7;
    public static XUiC_Backpack playerBackpack;
    public static XUiC_BackpackWindow backpackWindow;
    public static XUiC_ContainerStandardControls playerControls;
    public static readonly int customLockEnum = (int)XUiC_ItemStack.LockTypes.Burning + 1; //XUiC_ItemStack.LockTypes - Last used is Burning with value 5, so we use 6 for our custom locked slots
    public static KeyCode[] quickLockHotkeys;
    public static KeyCode[] quickStackHotkeys;
    public static KeyCode[] quickRestockHotkeys;




    public static bool IsStorageOpen => backpackWindow.xui.lootContainer != null && backpackWindow.xui.lootContainer.entityId == -1;

    public static string LockedSlotsFile()
    {
        return Path.Combine(GameIO.GetPlayerDataDir(), GameManager.Instance.persistentLocalPlayer.UserIdentifier + ".qsls");
    }

    public static Dictionary<TileEntity, int> GetOpenedTiles()
    {
        return Traverse.Create(GameManager.Instance).Field("lockedTileEntities").GetValue<Dictionary<TileEntity, int>>();
    }

    // Checks if a loot container is openable by a player
    // HOST OR SERVER ONLY
    public static bool UserCanOpen(PlatformUserIdentifierAbs _userId, TileEntity _tileEntity)
    {
        if (ConnectionManager.Instance.IsClient)
        {
            Log.Error("[QuickStack] Calling IsContainerUnlocked as Client");
            return false;
        }

        if ((_tileEntity is TileEntitySecureLootContainer lootContainer) &&
            lootContainer.IsLocked() && !lootContainer.IsUserAllowed(_userId))
        {
            return false;
        }

        return true;
    }

    public static bool IsValidLoot(TileEntityLootContainer _tileEntity)
    {
        return (_tileEntity.GetTileEntityType() == TileEntityType.Loot ||
                _tileEntity.GetTileEntityType() == TileEntityType.SecureLoot ||
                _tileEntity.GetTileEntityType() == TileEntityType.SecureLootSigned);
    }

    // Server/Single-Player: Checks if a loot container is currently open
    internal static bool ContainerIsNotInUse(TileEntity _tileEntity)
    {
        var openTileEntities = GetOpenedTiles();

        // Handle in-use containers
        if (openTileEntities != null && openTileEntities.TryGetValue(_tileEntity, out int tileEntityId) &&
            (GameManager.Instance.World.GetEntity(tileEntityId) is EntityAlive entityAlive) &&
            !entityAlive.IsDead())
        {
            return false;
        }

        return true;
    }

    // Yields all openable loot containers in a cubic radius about a point
    internal static IEnumerable<TileEntityLootContainer> FindNearbyLootContainers(Vector3i _center)
    {
        int stackRadius = Mathf.Clamp(QuickStack.stackRadius, 0, sbyte.MaxValue);
        for (int i = -stackRadius; i <= stackRadius; i++)
        {
            for (int j = -stackRadius; j <= stackRadius; j++)
            {
                for (int k = -stackRadius; k <= stackRadius; k++)
                {
                    var offset = new Vector3i(i, j, k);
                    if (!(GameManager.Instance.World.GetTileEntity(0, _center + offset) is TileEntityLootContainer tileEntity))
                    {
                        continue;
                    }

                    if (IsValidLoot(tileEntity) && ContainerIsNotInUse(tileEntity))
                    {
                        yield return tileEntity;
                    }
                }
            }
        }
    }

    // Gets the EItemMoveKind for the current move type based on the last time that move type was requested
    internal static XUiM_LootContainer.EItemMoveKind GetMoveKind(QuickStackType _type = QuickStackType.Stack)
    {
        float unscaledTime = Time.unscaledTime;
        float lastClickTime = lastClickTimes[(int)_type];
        lastClickTimes[(int)_type] = unscaledTime;

        if (unscaledTime - lastClickTime < 2.0f)
        {
            return XUiM_LootContainer.EItemMoveKind.FillAndCreate;
        }
        else
        {
            return XUiM_LootContainer.EItemMoveKind.FillOnly;
        }
    }

    //Quickstack functionality
    // SINGLEPLAYER ONLY
    public static void MoveQuickStack()
    {
        if (backpackWindow.xui.lootContainer != null && backpackWindow.xui.lootContainer.entityId == -1)
        {
            return;
        }

        var moveKind = GetMoveKind();

        EntityPlayerLocal primaryPlayer = GameManager.Instance.World.GetPrimaryPlayer();
        int lockedSlots = playerControls.StashLockedSlots();

        //returns tile entities opened by other players
        Dictionary<TileEntity, int> openedTileEntities = GetOpenedTiles();

        for (int i = -stackRadius; i <= stackRadius; i++)
        {
            for (int j = -stackRadius; j <= stackRadius; j++)
            {
                for (int k = -stackRadius; k <= stackRadius; k++)
                {
                    Vector3i blockPos = new Vector3i((int)primaryPlayer.position.x + i, (int)primaryPlayer.position.y + j, (int)primaryPlayer.position.z + k);

                    if (!(GameManager.Instance.World.GetTileEntity(0, blockPos) is TileEntityLootContainer tileEntity))
                    {
                        continue;
                    }

                    //TODO: !tileEntity.IsUserAccessing() && !openedTileEntities.ContainsKey(tileEntity) does not work on multiplayer
                    if (IsValidLoot(tileEntity) && !tileEntity.IsUserAccessing() && !openedTileEntities.ContainsKey(tileEntity))
                    {
                        StashItems(playerBackpack, tileEntity, lockedSlots, moveKind, playerControls.MoveStartBottomRight);
                        tileEntity.SetModified();
                    }
                }
            }
        }
    }

    public static void MoveQuickStack(TileEntityLootContainer[] _entityContainers)
    {
        if (_entityContainers == null)
        {
            return;
        }

        var moveKind = GetMoveKind(QuickStackType.Stack);

        int lockedSlots = playerControls.StashLockedSlots();

        for (int i = 0; i < _entityContainers.Length; ++i)
        {
            var tileEntity = _entityContainers[i];
            StashItems(playerBackpack, tileEntity, lockedSlots, moveKind, playerControls.MoveStartBottomRight);
            tileEntity.SetModified();
        }
    }

    public static void MoveQuickRestock(TileEntityLootContainer[] _entityContainers)
    {
        if (_entityContainers == null)
        {
            return;
        }

        XUiM_LootContainer.EItemMoveKind moveKind = GetMoveKind(QuickStackType.Restock);
        EntityPlayerLocal primaryPlayer = GameManager.Instance.World.GetPrimaryPlayer();
        LocalPlayerUI playerUI = LocalPlayerUI.GetUIForPlayer(primaryPlayer);
        int lockedSlots = playerControls.StashLockedSlots();
        XUiC_LootWindowGroup lootWindowGroup = (XUiC_LootWindowGroup)((XUiWindowGroup)playerUI.windowManager.GetWindow("looting")).Controller;
        XUiC_LootWindow lootWindow = Traverse.Create(lootWindowGroup).Field("lootWindow").GetValue<XUiC_LootWindow>();
        XUiC_LootContainer lootContainer = Traverse.Create(lootWindow).Field("lootContainer").GetValue<XUiC_LootContainer>();

        for (int i = 0; i <_entityContainers.Length; ++i)
        {
            var tileEntity = _entityContainers[i];
            if (!tileEntity.IsEmpty())
            {
                lootWindowGroup.SetTileEntityChest("QUICKSTACK", tileEntity);
                StashItems(lootContainer, primaryPlayer.bag, lockedSlots, moveKind, playerControls.MoveStartBottomRight);
                tileEntity.SetModified();
            }
        }
    }

    //Refactored from the original code to remove stash time due to quick stack/restock and check for custom locks
    public static (bool, bool) StashItems(XUiC_ItemStackGrid _srcGrid, IInventory _dstInventory, int _ignoredSlots, XUiM_LootContainer.EItemMoveKind _moveKind, bool _startBottomRight)
    {
        if (_srcGrid == null || _dstInventory == null)
        {
            return (false, false);
        }

        XUiController[] itemStackControllers = _srcGrid.GetItemStackControllers();

        bool sourceEmptied = true;
        bool inventoriesModified = false;

        int num = _startBottomRight ? (itemStackControllers.Length - 1) : _ignoredSlots;
        while (_startBottomRight ? (num >= _ignoredSlots) : (num < itemStackControllers.Length))
        {
            XUiC_ItemStack xuiC_ItemStack = itemStackControllers[num] as XUiC_ItemStack;
            if (!xuiC_ItemStack.StackLock && xuiC_ItemStack.LockType().GetValue<int>() != customLockEnum)
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
                    }
                    else
                    {
                        sourceEmptied = false;
                    }
                    if (count != itemStack.count)
                    {
                        xuiC_ItemStack.ForceSetItemStack(itemStack);
                        inventoriesModified = true;
                    }
                }
            }
            num = (_startBottomRight ? (num - 1) : (num + 1));
        }

        return (sourceEmptied, inventoriesModified);
    }

    // UI Delegates
    // UI Delegate
    public static void QuickInventoryOnClick(QuickStackType _type)
    {
        if ((int)_type < 0 || _type >= QuickStackType.Count)
        {
            Log.Error($"[QuickStack] Invalid QuickStack Type { (int)_type }");
            return;
        }

        if (GameManager.IsDedicatedServer)
        {
            Log.Error("[QuickStack] attempted QuickStack operation as Dedicated Server.");
            return;
        }

        if (IsStorageOpen)
        {
            Log.Warning("[QuickStack] QuickStack operation while in loot UI");
            return;
        }

        // Multiplayer Client
        if (ConnectionManager.Instance.IsClient)
        {
            // Sets off a chain of NetPackages:
            // Client FindOpenableContainer--> Server
            // Client          <--DoQuickStack Server
            // Client UnlockContainers-->      Server
            // Ultimately, same as-if doing the Singleplayer code path
            ConnectionManager.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageFindOpenableContainers>()
                    .Setup(GameManager.Instance.persistentLocalPlayer.EntityId, _type)
            );
        }
        // Multiplayer Host or SinglePlayer
        else
        {
            var stopwatch = Stopwatch.StartNew();
            var center = new Vector3i(GameManager.Instance.World.GetPrimaryPlayer().position);

            var lootContainers = FindNearbyLootContainers(center);

            if (!ConnectionManager.Instance.IsSinglePlayer)
            {
                lootContainers = lootContainers
                    .Where(container => UserCanOpen(GameManager.Instance.persistentLocalPlayer.UserIdentifier, container));
            }

            if (_type == QuickStackType.Stack)
            {
                MoveQuickStack(lootContainers.ToArray());
            }
            else
            {
                MoveQuickRestock(lootContainers.ToArray());
            }

            Log.Out($"[QuickStack] { _type } performed in { stopwatch.ElapsedMilliseconds } ms");
        }
    }

    internal static void QuickStackOnClick() => QuickInventoryOnClick(QuickStackType.Stack);
    internal static void QuickRestockOnClick() => QuickInventoryOnClick(QuickStackType.Restock);
}
