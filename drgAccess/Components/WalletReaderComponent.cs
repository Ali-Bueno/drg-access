using UnityEngine;
using drgAccess.Helpers;
using drgAccess.Patches;

namespace drgAccess.Components;

/// <summary>
/// Listens for G key to read wallet balance and T key to read equipped gear.
/// Outside gameplay, G also reports the player rank — it used to be readable only on
/// the save-slot screen at launch.
/// </summary>
public class WalletReaderComponent : MonoBehaviour
{
    void Update()
    {
        try
        {
            if (InputHelper.ReadWallet())
            {
                if (WalletReader.IsWalletReadable)
                    WalletReader.ReadWallet();
                else if (!GameStateHelper.IsInActiveGameplay() && PlayerRankReader.IsAvailable)
                    PlayerRankReader.ReadRank();
            }

            if (UIFormPatches.GearInventoryOpen && InputHelper.ReadEquippedGear())
                EquippedGearReader.ReadEquipped();
        }
        catch { }
    }
}
