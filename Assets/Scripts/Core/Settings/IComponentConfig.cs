namespace TheWaningBorder.Core.Settings
{
    /// <summary>
    /// Marks a ScriptableObject as the config for exactly one component class.
    ///
    /// The naming rule is part of the contract and the editor tooling enforces
    /// it: a config type is named &lt;Component&gt;Config, its asset is named
    /// &lt;Component&gt;.asset, and that asset sits in the same folder as
    /// &lt;Component&gt;.cs — so the numbers are always one click from the code
    /// that reads them.
    ///
    /// This exists so the catalog rebuild can FIND configs by type rather than
    /// guessing from file names, which would quietly miss a renamed asset.
    /// </summary>
    public interface IComponentConfig
    {
    }
}
