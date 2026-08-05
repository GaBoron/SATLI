using System.Runtime.InteropServices;
using System.Text;

namespace Satl_Gui.Services;

internal sealed class ApplicationDistributionService
{
    private const int ErrorInsufficientBuffer = 122;
    private readonly Lazy<bool> _hasPackageIdentity;

    public ApplicationDistributionService(Func<bool>? packageIdentityProbe = null)
    {
        _hasPackageIdentity = new Lazy<bool>(
            packageIdentityProbe ?? DetectPackageIdentity,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool UsesStoreManagedUpdates => _hasPackageIdentity.Value;

    private static bool DetectPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) == ErrorInsufficientBuffer;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(
        ref uint packageFullNameLength,
        StringBuilder? packageFullName);
}
