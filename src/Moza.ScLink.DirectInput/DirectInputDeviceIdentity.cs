using Moza.ScLink.Core.Models;

namespace Moza.ScLink.DirectInput;

/// <summary>
/// Identity bundle passed to the <see cref="VorticeDirectInputDevice"/> constructor. Bundles the DirectInput
/// device handle (instance GUID), the human-facing names, and the classified <see cref="DeviceModel"/>.
/// </summary>
/// <param name="InstanceGuid">DirectInput instance GUID — stable identifier for this device on this machine.</param>
/// <param name="ProductName">Raw product name from DirectInput enumeration, e.g. <c>"MOZA AB9 Base"</c>.</param>
/// <param name="DisplayName">Operator-facing display name. At T-07 layer this equals <paramref name="ProductName"/>; T-08's allowlist JSON may differentiate.</param>
/// <param name="Model">Classified MOZA model. <see cref="VorticeDirectInputDevice"/> refuses construction when this is <see cref="DeviceModel.Unknown"/>.</param>
public sealed record DirectInputDeviceIdentity(
    Guid InstanceGuid,
    string ProductName,
    string DisplayName,
    DeviceModel Model);
