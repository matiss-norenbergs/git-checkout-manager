using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    public interface ISubmoduleService
    {
        /// <summary>
        /// Lists every submodule recorded in the checkout's tree with its state. Purely local: it never
        /// touches the network and never changes the checkout.
        /// </summary>
        Task<List<SubmoduleInfo>> ListAsync(string root, CancellationToken ct = default);
    }
}
