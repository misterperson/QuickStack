using System;
using System.Linq;
using Audio;
using HarmonyLib;
using UnityEngine;

internal class Patches
{
    //This patch is used to initialize the UI functionallity for the quickstack and restock buttons.
    [HarmonyPatch(typeof(XUiC_ContainerStandardControls), "Init")]
    private class QS_01
    {
        public static void Postfix(XUiC_ContainerStandardControls __instance)
        {
            if (__instance.Parent.Parent is XUiC_BackpackWindow)
            {
                QuickStack.playerControls = __instance;

                XUiController childById = __instance.GetChildById("btnMoveQuickStack");
                if (childById != null)
                {
                    childById.OnPress += delegate (XUiController _sender, int _args)
                    {
                        QuickStack.QuickInventoryOnClick(QuickStackType.Stack);
                    };
                }

                childById = __instance.GetChildById("btnMoveQuickRestock");
                if (childById != null)
                {
                    childById.OnPress += delegate (XUiController _sender, int _args)
                    {
                        QuickStack.QuickInventoryOnClick(QuickStackType.Restock);
                    };
                }
            }
        }
    }

    //This patch overrides the original one to accommodate the locked slots.
    [HarmonyPatch(typeof(XUiC_ContainerStandardControls), "MoveAll")]
    private class QS_02
    {
        public static bool Prefix(XUiC_ContainerStandardControls __instance)
        {
            if (__instance.Parent.Parent.GetType() != typeof(XUiC_BackpackWindow))
                return true;

            if (__instance.MoveAllowed(out var srcGrid, out var dstInventory))
            {
                var (emptied, modified) = QuickStack.StashItems(srcGrid, dstInventory, Traverse.Create(__instance).Field("stashLockedSlots").GetValue<int>(), XUiM_LootContainer.EItemMoveKind.All, __instance.MoveStartBottomRight);
                var moveAllDone = __instance.MoveAllDone;
                if (moveAllDone == null)
                {
                    return false;
                }
                moveAllDone(emptied, modified);
            }

            return false;
        }
    }

    //This patch overrides the original one to accommodate the locked slots. 
    [HarmonyPatch(typeof(XUiC_ContainerStandardControls), "MoveFillAndSmart")]
    private class QS_03
    {
        public static bool Prefix(XUiC_ContainerStandardControls __instance)
        {
            if (__instance.Parent.Parent.GetType() != typeof(XUiC_BackpackWindow))
                return true;

            var moveKind = QuickStack.GetMoveKind(QuickStackType.Stack);
            if (moveKind == XUiM_LootContainer.EItemMoveKind.FillOnly)
            {
                moveKind = XUiM_LootContainer.EItemMoveKind.FillOnlyFirstCreateSecond;
            }

            if (__instance.MoveAllowed(out var srcGrid, out var dstInventory))
            {
                QuickStack.StashItems(srcGrid, dstInventory, Traverse.Create(__instance).Field("stashLockedSlots").GetValue<int>(), moveKind, __instance.MoveStartBottomRight);
            }

            return false;
        }
    }

    //This patch overrides the original one to accommodate the locked slots. 
    [HarmonyPatch(typeof(XUiC_ContainerStandardControls), "MoveFillStacks")]
    private class QS_04
    {
        public static bool Prefix(XUiC_ContainerStandardControls __instance)
        {
            if (__instance.Parent.Parent.GetType() != typeof(XUiC_BackpackWindow))
                return true;

            if (__instance.MoveAllowed(out var srcGrid, out var dstInventory))
            {
                QuickStack.StashItems(srcGrid, dstInventory, Traverse.Create(__instance).Field("stashLockedSlots").GetValue<int>(), XUiM_LootContainer.EItemMoveKind.FillOnly, __instance.MoveStartBottomRight);
            }

            return false;
        }
    }

    //This patch overrides the original one to accommodate the locked slots. 
    [HarmonyPatch(typeof(XUiC_ContainerStandardControls), "MoveSmart")]
    private class QS_05
    {
        public static bool Prefix(XUiC_ContainerStandardControls __instance)
        {
            if (__instance.Parent.Parent.GetType() != typeof(XUiC_BackpackWindow))
                return true;

            if (__instance.MoveAllowed(out var srcGrid, out var dstInventory))
            {
                QuickStack.StashItems(srcGrid, dstInventory, Traverse.Create(__instance).Field("stashLockedSlots").GetValue<int>(), XUiM_LootContainer.EItemMoveKind.FillAndCreate, __instance.MoveStartBottomRight);
            }

            return false;
        }
    }

    //This patch overrides the original one to accommodate the locked slots. 
    [HarmonyPatch(typeof(XUiC_ContainerStandardControls), "Sort")]
    private class QS_06
    {
        public static bool Prefix(XUiC_ContainerStandardControls __instance)
        {
            if (__instance.Parent.Parent.GetType() != typeof(XUiC_BackpackWindow))
                return true;

            int lockedSlots = Traverse.Create(QuickStack.playerControls).Field("stashLockedSlots").GetValue<int>();

            _ = __instance.MoveAllowed(out var srcGrid, out _);

            bool SlotIsUnlocked(XUiC_ItemStack controller)
            {
                return !(controller).StackLock &&
                    Traverse.Create(controller).Field("lockType").GetValue<XUiC_ItemStack.LockTypes>() == XUiC_ItemStack.LockTypes.None;
            }

            //Count the number of unlocked slots
            //We do this so we don't convert back and forth between List<ItemStack> and ItemStack[] since original code uses arrays.
            XUiC_ItemStack[] unlockedSlots = srcGrid.GetItemStackControllers()
                .Select(stackController => stackController as XUiC_ItemStack)
                .Where(SlotIsUnlocked)
                .ToArray();

            //Combine and sort itemstacks using original code
            ItemStack[] items = StackSortUtil.CombineAndSortStacks(unlockedSlots.Select(slot => slot.ItemStack).ToArray(), 0);

            //Add back itemstack in sorted order, skipping through the lock slots.
            for(int i = 0; i < items.Length; ++i)
            {
                unlockedSlots[i].ItemStack = items[i];
            }

            return false;
        }
    }

    //This patch is used to initialize the functionallity for the slot locking mechanism.
    [HarmonyPatch(typeof(XUiC_BackpackWindow), "Init")]
    private class QS_07
    {
        public static void Postfix(XUiC_BackpackWindow __instance)
        {
            QuickStack.backpackWindow = __instance;
            QuickStack.playerBackpack = Traverse.Create(__instance).Field("backpackGrid").GetValue() as XUiC_Backpack;
            XUiController[] slots = QuickStack.playerBackpack.GetItemStackControllers();

            QuickStack.lastClickTimes.Fill(0.0f);

            for (int i = 0; i < slots.Length; ++i)
            {
                int copy = i;
                slots[i].OnPress += (XUiController _sender, int _mouseButton) =>
                {
                    XUiC_ItemStack itemStack = _sender as XUiC_ItemStack;

                    if (UICamera.GetKey(KeyCode.LeftAlt))
                    {
                        if (Traverse.Create(itemStack).Field("lockType").GetValue<XUiC_ItemStack.LockTypes>() == XUiC_ItemStack.LockTypes.None)
                        {
                            Traverse.Create(itemStack).Field("lockType").SetValue(QuickStack.customLockEnum);
                            itemStack.RefreshBindings();
                        }
                        else if (Traverse.Create(itemStack).Field("lockType").GetValue<int>() == QuickStack.customLockEnum)
                        {
                            Traverse.Create(itemStack).Field("lockType").SetValue(XUiC_ItemStack.LockTypes.None);
                            itemStack.RefreshBindings();
                        }

                        //Manager.PlayInsidePlayerHead(StrAudioClip.UITab, -1, 0f, false);
                        //Manager.PlayXUiSound(audio, 1);
                        Manager.PlayButtonClick();
                    }
                };
            }
        }
    }

    //This patch is used to add a binding to know whether the player is not accessing other loot container inventories with some exceptions like workstations.
    //This is used in the xml file to make the quickstack icon visible only when the player inventory is open.
    [HarmonyPatch(typeof(XUiC_BackpackWindow), "GetBindingValue")]
    private class QS_08
    {
        public static void Postfix(ref bool __result, XUiC_BackpackWindow __instance, ref string value, string bindingName)
        {
            if (__result == false)
            {
                if (bindingName != null)
                {
                    if (bindingName == "notlootingorvehiclestorage")
                    {
                        bool flag1 = __instance.xui.vehicle != null && __instance.xui.vehicle.GetVehicle().HasStorage();
                        bool flag2 = __instance.xui.lootContainer != null && __instance.xui.lootContainer.entityId == -1;
                        bool flag3 = __instance.xui.lootContainer != null && GameManager.Instance.World.GetEntity(__instance.xui.lootContainer.entityId) is EntityDrone;
                        value = (!flag1 && !flag2 && !flag3).ToString();
                        __result = true;
                    }
                }
            }
        }
    }

    //This patch is used to update the slot color in the backpack if the slot is locked by the player.
    [HarmonyPatch(typeof(XUiC_ItemStack), "updateBorderColor")]
    private class QS_09
    {
        [HarmonyPostfix]
        public static void Postfix(XUiC_ItemStack __instance)
        {
            if (Traverse.Create(__instance).Field("lockType").GetValue<int>() == QuickStack.customLockEnum)
                Traverse.Create(__instance).Field("selectionBorderColor").SetValue(new Color32(128, 0, 0, 255));
        }
    }

    //QuickStack and Restock functionallity by pressing hotkeys (useful if other mods remove the UI buttons)
    [HarmonyPatch(typeof(GameManager), "UpdateTick")]
    private class QS_10
    {
        public static void Postfix(EntityPlayerLocal __instance)
        {
            if (UICamera.GetKeyDown(KeyCode.Z) && UICamera.GetKey(KeyCode.LeftAlt))
            {
                QuickStack.QuickInventoryOnClick(QuickStackType.Restock);
                Manager.PlayButtonClick();
            }
            else if (UICamera.GetKeyDown(KeyCode.X) && UICamera.GetKey(KeyCode.LeftAlt))
            {
                QuickStack.QuickInventoryOnClick(QuickStackType.Stack);
                Manager.PlayButtonClick();
            }
        }
    }
}