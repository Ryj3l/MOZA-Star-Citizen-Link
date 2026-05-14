using Moza.ScLink.Core.Models;

namespace Moza.ScLink.DirectInput;

/// <summary>
/// Substring-based product-name classifier for MOZA flight bases. Maps a raw DirectInput product name
/// to a <see cref="DeviceModel"/> using AB9-before-AB6 specificity ordering.
/// </summary>
/// <remarks>
/// Transitional. T-08 replaces this with a JSON-driven <c>DeviceAllowlist</c> + <c>DeviceDetector</c>
/// loaded from <c>device-allowlist.json</c>. Deletion is tracked alongside <c>LegacyForceFeedbackDeviceAdapter</c>
/// in GitHub issue #15 (single issue, two file paths).
/// </remarks>
[Obsolete("Transitional classifier. T-08 replaces this with the device-allowlist.json driven matching system. Tracked in the same deletion issue as LegacyForceFeedbackDeviceAdapter (issue #15).")]
public static class DeviceClassifier
{
    /// <summary>
    /// Classifies a DirectInput product name into a <see cref="DeviceModel"/>. Case-insensitive ordinal substring
    /// match against <c>"AB9"</c> first (specificity ordering), then <c>"AB6"</c>. The <c>"MOZA"</c> substring
    /// alone does NOT auto-classify — future flight bases not in <c>{AB6, AB9}</c> stay <see cref="DeviceModel.Unknown"/>.
    /// </summary>
    /// <param name="productName">Product name from <see cref="DirectInputDeviceInfo.ProductName"/>; may be <see langword="null"/> or empty.</param>
    /// <returns><see cref="DeviceModel.MozaAb9"/>, <see cref="DeviceModel.MozaAb6"/>, or <see cref="DeviceModel.Unknown"/>.</returns>
    public static DeviceModel ClassifyByProductName(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return DeviceModel.Unknown;
        }

        var upper = productName.ToUpperInvariant();

        if (upper.Contains("AB9", StringComparison.Ordinal))
        {
            return DeviceModel.MozaAb9;
        }

        if (upper.Contains("AB6", StringComparison.Ordinal))
        {
            return DeviceModel.MozaAb6;
        }

        return DeviceModel.Unknown;
    }
}
