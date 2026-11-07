using UnityEngine;

/// <summary>
/// Switches a hole between its normal (terrible) green and the flat "pool table" green.
/// Each version has its own cup, CupTrigger and flagstick, so only the active one counts.
///
/// For the cheat, find this component and call:
///     FindFirstObjectByType&lt;GreenSwap&gt;().SetPoolTable(true);
/// </summary>
public class GreenSwap : MonoBehaviour
{
    [Tooltip("The normal, comically bad green (with its own cup and flag).")]
    public GameObject terribleGreen;

    [Tooltip("The perfectly flat pool table version (with its own cup and flag).")]
    public GameObject poolTable;

    [Tooltip("Start the hole with the pool table already active.")]
    public bool startAsPoolTable;

    public bool IsPoolTable => poolTable != null && poolTable.activeSelf;

    void Awake()
    {
        SetPoolTable(startAsPoolTable);
    }

    /// <summary>true = flat pool table, false = the terrible green.</summary>
    public void SetPoolTable(bool on)
    {
        if (terribleGreen != null) terribleGreen.SetActive(!on);
        if (poolTable != null) poolTable.SetActive(on);
    }

    public void TogglePoolTable() => SetPoolTable(!IsPoolTable);
}
