using UnityEngine;
using drgAccess.Helpers;

namespace drgAccess.Components;

/// <summary>
/// Listens for G key to read wallet balance via screen reader.
/// Only active when the stat upgrades form is open.
/// </summary>
public class WalletReaderComponent : MonoBehaviour
{
    void Update()
    {
        try
        {
            if (!Patches.WalletReader.IsWalletReadable) return;
            if (!InputHelper.ReadWallet()) return;

            Patches.WalletReader.ReadWallet();
        }
        catch { }
    }
}
