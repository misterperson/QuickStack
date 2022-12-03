﻿using Audio;
using HarmonyLib;
using QuickStackExtensions;
using System;
using System.IO;
using System.Linq;
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
                        QuickStack.QuickStackOnClick();
                    };
                }

                childById = __instance.GetChildById("btnMoveQuickRestock");
                if (childById != null)
                {
                    childById.OnPress += delegate (XUiController _sender, int _args)
                    {
                        QuickStack.QuickRestockOnClick();
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
            {
                return true;
            }

            if (__instance.MoveAllowed(out var srcGrid, out var dstInventory))
            {
                ValueTuple<bool, bool> valueTuple = QuickStack.StashItems(srcGrid, dstInventory, __instance.StashLockedSlots(), XUiM_LootContainer.EItemMoveKind.All, __instance.MoveStartBottomRight);
                Action<bool, bool> moveAllDone = __instance.MoveAllDone;
                if (moveAllDone == null)
                {
                    return false;
                }
                moveAllDone(valueTuple.Item1, valueTuple.Item2);
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
            {
                return true;
            }

            var moveKind = QuickStack.GetMoveKind();
            if (moveKind == XUiM_LootContainer.EItemMoveKind.FillOnly)
            {
                moveKind = XUiM_LootContainer.EItemMoveKind.FillOnlyFirstCreateSecond;
            }

            if (__instance.MoveAllowed(out var srcGrid, out var dstInventory))
            {
                QuickStack.StashItems(srcGrid, dstInventory, __instance.StashLockedSlots(), moveKind, __instance.MoveStartBottomRight);
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
            {
                return true;
            }

            if (__instance.MoveAllowed(out var srcGrid, out var dstInventory))
            {
                QuickStack.StashItems(srcGrid, dstInventory, __instance.StashLockedSlots(), XUiM_LootContainer.EItemMoveKind.FillOnly, __instance.MoveStartBottomRight);
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
            {
                return true;
            }

            if (__instance.MoveAllowed(out var srcGrid, out var dstInventory))
            {
                QuickStack.StashItems(srcGrid, dstInventory, __instance.StashLockedSlots(), XUiM_LootContainer.EItemMoveKind.FillAndCreate, __instance.MoveStartBottomRight);
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
            if ((__instance.Parent.Parent is XUiC_BackpackWindow) == false)
            {
                return true;
            }

            int lockedSlots = QuickStack.playerControls.StashLockedSlots();

            __instance.MoveAllowed(out var srcGrid, out var dstInventory);

            //Get the unlocked slots
            XUiC_ItemStack[] unlockedSlots = srcGrid.GetItemStackControllers()
                .Select(stackController => stackController as XUiC_ItemStack)
                .Where(itemStack => itemStack.IsUnlocked())
                .ToArray();

            //Combine and sort itemstacks using original code
            ItemStack[] items = StackSortUtil.CombineAndSortStacks(unlockedSlots.Select(slot => slot.ItemStack).ToArray(), 0);

            //Add back itemstack in sorted order, skipping through the lock slots.
            for (int i = 0; i < items.Length; ++i)
            {
                unlockedSlots[i].ItemStack = items[i];
            }

            return false;
        }
    }

    //This patch is used to initialize the functionallity for the slot locking mechanism and load saved locked slots.
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
                slots[i].OnPress += (XUiController _sender, int _mouseButton) =>
                {
                    for (int j = 0; j < QuickStack.quickStackHotkeys.Length - 1; j++)
                    {
                        if (!UICamera.GetKey(QuickStack.quickLockHotkeys[j]))
                        {
                            return;
                        }
                    }

                    XUiC_ItemStack itemStack = _sender as XUiC_ItemStack;

                    Traverse lockType = itemStack.LockType();

                    if (lockType.GetValue<XUiC_ItemStack.LockTypes>() == XUiC_ItemStack.LockTypes.None)
                    {
                        lockType.SetValue(QuickStack.customLockEnum);
                        itemStack.RefreshBindings();
                    }
                    else if (lockType.GetValue<int>() == QuickStack.customLockEnum)
                    {
                        lockType.SetValue(XUiC_ItemStack.LockTypes.None);
                        itemStack.RefreshBindings();
                    }

                    //Manager.PlayInsidePlayerHead(StrAudioClip.UITab, -1, 0f, false);
                    //Manager.PlayXUiSound(audio, 1);
                    Manager.PlayButtonClick();
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
            if (__instance.LockType().GetValue<int>() == QuickStack.customLockEnum)
            {
                Traverse.Create(__instance).Field("selectionBorderColor").SetValue(new Color32(128, 0, 0, 255));
            }
        }
    }

    //QuickStack and Restock functionallity by pressing hotkeys (useful if other mods remove the UI buttons)
    [HarmonyPatch(typeof(GameManager), "UpdateTick")]
    private class QS_10
    {
        public static void Postfix(EntityPlayerLocal __instance)
        {
            if (UICamera.GetKeyDown(QuickStack.quickStackHotkeys[QuickStack.quickStackHotkeys.Length - 1]))
            {
                for (int i = 0; i < QuickStack.quickStackHotkeys.Length - 1; i++)
                {
                    if (!UICamera.GetKey(QuickStack.quickStackHotkeys[i]))
                    {
                        return;
                    }
                }

                QuickStack.QuickStackOnClick();
                Manager.PlayButtonClick();
            }
            else if (UICamera.GetKeyDown(QuickStack.quickRestockHotkeys[QuickStack.quickRestockHotkeys.Length - 1]))
            {
                for (int i = 0; i < QuickStack.quickRestockHotkeys.Length - 1; i++)
                {
                    if (!UICamera.GetKey(QuickStack.quickRestockHotkeys[i]))
                    {
                        return;
                    }
                }

                QuickStack.QuickRestockOnClick();
                Manager.PlayButtonClick();
            }
        }
    }


    /* 
     * Binary format:
     * [int32] locked slots - mod compatibility
     * [int32] array count (N) of locked slots by us
     * [N bytes] boolean array indicating locked slots
     */

    //Save locked slots
    [HarmonyPatch(typeof(GameManager), "SaveLocalPlayerData")]
    private class QS_11
    {
        public static void Postfix()
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                using (BinaryWriter binWriter = new BinaryWriter(File.Open(QuickStack.LockedSlotsFile(), FileMode.Create)))
                {
                    XUiController[] slots = QuickStack.playerBackpack.GetItemStackControllers();
                    binWriter.Write(QuickStack.playerControls.StashLockedSlots());

                    binWriter.Write(slots.Length);
                    for (int i = 0; i < slots.Length; i++)
                    {
                        binWriter.Write((slots[i] as XUiC_ItemStack).LockType().GetValue<int>() == QuickStack.customLockEnum);
                    }
                }

                Log.Out($"[QuickStack] Saved locked slots config in { stopwatch.ElapsedMilliseconds } ms");
            }
            catch (Exception e)
            {
                Log.Error($"[QuickStack] Failed to write locked slots file: { e.Message }. Slot states will not be saved!");
            }
        }
    }

    //Load locked slots
    [HarmonyPatch(typeof(GameManager), "setLocalPlayerEntity")]
    private class QS_12
    {
        public static void Postfix()
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                string path = QuickStack.LockedSlotsFile();
                if (!File.Exists(path))
                {
                    Log.Warning("[QuickStack] No locked slots config detected. Slots will default to unlocked");
                    return;
                }

                // reported number of locked slots
                long reportedLength = new FileInfo(path).Length - sizeof(int) * 2;
                if (reportedLength < 0)
                {
                    // file is too small to process
                    Log.Error("[QuickStack] locked slots config appears corrupted. Slots will be defaulted to unlocked");
                    return;
                }

                using (BinaryReader binReader = new BinaryReader(File.Open(path, FileMode.Open)))
                {
                    // locked slots saved by the unused combobox some mods may enable
                    int comboLockedSlots = Math.Max(0, binReader.ReadInt32());

                    // locked slots saved by us
                    int quickStackLockedSlots = binReader.ReadInt32();
                    if (reportedLength != quickStackLockedSlots * sizeof(bool))
                    {
                        Log.Error("[QuickStack] locked slots config appears corrupted. Slots will be defaulted to unlocked");
                        return;
                    }

                    // KHA20-LockableInvSlots compatibility
                    if (QuickStack.playerControls.GetChildById("cbxLockedSlots") is XUiC_ComboBoxInt comboBox)
                    {
                        comboBox.Value = comboLockedSlots;
                    }

                    XUiController[] slots = QuickStack.playerBackpack.GetItemStackControllers();
                    for (int i = 0; i < Math.Min(quickStackLockedSlots, slots.Length); i++)
                    {
                        if (binReader.ReadBoolean())
                        {
                            (slots[i] as XUiC_ItemStack).LockType().SetValue(QuickStack.customLockEnum);
                        }
                    }
                }
                Log.Out($"[QuickStack] Loaded locked slots config in { stopwatch.ElapsedMilliseconds } ms");
            }
            catch (Exception e)
            {
                Log.Error($"[QuickStack] Failed to read locked slots config:  { e.Message }. Slots will default to unlocked");
            }
        }
    }
}