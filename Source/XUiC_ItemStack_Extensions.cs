using HarmonyLib;

namespace QuickStackExtensions {
    static partial class XUiC_ItemStack_Extensions  {
        public static Traverse LockType(this XUiC_ItemStack itemStackContoller)
        {
            return Traverse.Create(itemStackContoller).Field("lockType");
        }

        public static bool IsUnlocked(this XUiC_ItemStack itemStackController)
        {
            return !itemStackController.StackLock && itemStackController.LockType().GetValue<XUiC_ItemStack.LockTypes>() == XUiC_ItemStack.LockTypes.None;
        }
    }
}
