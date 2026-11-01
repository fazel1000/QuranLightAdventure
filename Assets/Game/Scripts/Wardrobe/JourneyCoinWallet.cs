using System;
using System.Globalization;
using UnityEngine;

// The HUD, puzzle rewards and equipment shop use this one saved balance.
public static class JourneyCoinWallet
{
    private const string BalanceKey = "JourneyOfLight.Coins";
    private const string PurchaseEpochKey = "JourneyOfLight.Equipment.PurchaseEpoch";
    private static string purchaseEpoch;
    private static bool loaded;
    private static int balance;
    public static event Action Changed;
    public static int Balance { get { Load(); return balance; } }
    private static void Load()
    { if (loaded) return; balance = Math.Max(0, PlayerPrefs.GetInt(BalanceKey, 0)); loaded = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { loaded = false; balance = 0; purchaseEpoch = null; Changed = null; }
    public static void Add(int amount)
    {
        if (amount <= 0) return;
        Load(); balance = (int)Math.Min(int.MaxValue, (long)balance + amount);
        PlayerPrefs.SetInt(BalanceKey, balance); PlayerPrefs.Save(); Changed?.Invoke();
    }
    public static bool IsOwned(string id, int price)
    { return !string.IsNullOrEmpty(id) && (price <= 0 || PlayerPrefs.GetInt(OwnershipKey(id), 0) == 1); }
    public static bool TryPurchase(string id, int price)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (IsOwned(id, price)) return true;
        Load(); price = Math.Max(0, price);
        if (balance < price) return false;
        // Write the balance and ownership together, then notify UI after both changed.
        balance -= price; PlayerPrefs.SetInt(BalanceKey, balance);
        PlayerPrefs.SetInt(OwnershipKey(id), 1); PlayerPrefs.Save(); Changed?.Invoke(); return true;
    }
    private static string OwnershipKey(string id)
    {
        if (purchaseEpoch == null) purchaseEpoch = PlayerPrefs.GetString(PurchaseEpochKey, "");
        // An empty epoch reads the existing purchases without migrating or changing them.
        return "JourneyOfLight.Equipment.Owned." + (purchaseEpoch.Length == 0 ? "" : purchaseEpoch + ".") + id;
    }
#if UNITY_EDITOR
    public static void ResetAllPurchasesForEditor()
    {
        // Invalidate every old purchase, including items no longer in the scene's catalog.
        purchaseEpoch = Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(PurchaseEpochKey, purchaseEpoch);
        PlayerPrefs.Save(); Changed?.Invoke();
    }
#endif
    public static string Format(int number)
    {
        string text = Math.Max(0, number).ToString(CultureInfo.InvariantCulture);
        if (JourneyLocalization.Language == 1) return text;
        char zero = JourneyLocalization.Language == 2 ? '٠' : '۰';
        for (int i = 0; i < 10; i++) text = text.Replace((char)('0' + i), (char)(zero + i));
        return text;
    }
}
