using UnityEngine;
using UnityEngine.InputSystem;

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

            var kb = Keyboard.current;
            if (kb == null) return;
            if (!kb[Key.G].wasPressedThisFrame) return;

            Patches.WalletReader.ReadWallet();
        }
        catch { }
    }
}
