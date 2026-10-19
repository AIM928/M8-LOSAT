namespace M8LOSATM2.LosatCore
{
    internal static class LockViewRules
    {
        // A null slot is the same weapon's normal exterior view after closing
        // its optic; a non-owned head/optic slot is a genuine viewpoint change.
        internal static bool KeepOwner(bool requireOptic,bool hasSlot,bool ownOptic,bool opticChild,bool mirrorReturn=false)
        {return requireOptic ? hasSlot && (ownOptic || opticChild) : !hasSlot || ownOptic || opticChild || mirrorReturn;}
    }
}
