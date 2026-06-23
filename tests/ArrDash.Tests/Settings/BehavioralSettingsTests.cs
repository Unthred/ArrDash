using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Settings;

public class ExternalLinkServiceTests
{
    [Theory]
    [InlineData(ExternalLinkTarget.NewTab, "_blank")]
    [InlineData(ExternalLinkTarget.SameTab, "_self")]
    public void TargetAttribute_maps_preferences_to_html_target(ExternalLinkTarget target, string expected)
    {
        Assert.Equal(expected, ExternalLinkService.TargetAttribute(target));
    }
}

public class QualityDisplayHelperTests
{
    [Fact]
    public void Format_reorders_webdl_quality_to_friendly_label()
    {
        Assert.Equal("1080p · Web-DL", QualityDisplayHelper.Format("WEBDL-1080p"));
    }
}

public class RootCssClassTests
{
    [Fact]
    public void BackgroundStyle_emits_css_class_used_by_stylesheet()
    {
        var service = CreateService(new UserLayoutPreferences { BackgroundStyle = BackgroundStyle.Minimal });

        Assert.Contains("bg-minimal", service.GetRootCssClasses(kiosk: false));
    }

    private static LayoutPreferencesService CreateService(UserLayoutPreferences prefs)
    {
        var service = new LayoutPreferencesService(
            new Infrastructure.FakeWebHostEnvironment(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LayoutPreferencesService>.Instance);
        service.SetPreview(prefs);
        return service;
    }
}
