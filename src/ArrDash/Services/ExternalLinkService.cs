using ArrDash.Models;
using Microsoft.JSInterop;

namespace ArrDash.Services;

public sealed class ExternalLinkService(IJSRuntime js)
{
    public static string TargetAttribute(ExternalLinkTarget target) =>
        target == ExternalLinkTarget.NewTab ? "_blank" : "_self";

    public ValueTask OpenAsync(string url, ExternalLinkTarget target) =>
        js.InvokeVoidAsync("arrdashLinks.open", url, TargetAttribute(target));
}
