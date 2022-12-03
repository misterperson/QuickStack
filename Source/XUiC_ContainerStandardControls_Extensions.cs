using HarmonyLib;

namespace QuickStackExtensions
{
    static partial class XUiC_ContainerStandardControls_Extensions
    {
        public static int StashLockedSlots(this XUiC_ContainerStandardControls controls)
        {
            return Traverse.Create(controls).Field("stashLockedSlots").GetValue<int>();
        }
    }
}
