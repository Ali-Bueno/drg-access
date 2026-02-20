using UnityEngine;
using drgAccess.Helpers;
using drgAccess.Patches;

namespace drgAccess.Components;

/// <summary>
/// Listens for G key to read wallet balance and T key to read equipped gear.
/// Only active when relevant forms are open.
/// </summary>
public class WalletReaderComponent : MonoBehaviour
{
    void Update()
    {
        try
        {
            if (WalletReader.IsWalletReadable)
            {
                if (InputHelper.ReadWallet())
                    WalletReader.ReadWallet();
            }

            if (UIFormPatches.GearInventoryOpen && InputHelper.ReadEquippedGear())
                EquippedGearReader.ReadEquipped();
        }
        catch { }
    }
}
