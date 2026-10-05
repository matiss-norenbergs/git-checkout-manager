namespace GitCheckoutManager.Models
{
    /// <summary>Which commit "Initialize selected" brings a submodule to.</summary>
    public enum SubmoduleTarget
    {
        /// <summary>The commit the main repo records.</summary>
        Pinned,
        /// <summary>The tip of the submodule's tracked branch (<c>--remote</c>).</summary>
        LatestFromBranch,
    }
}
