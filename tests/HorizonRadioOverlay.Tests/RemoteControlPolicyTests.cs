using HorizonRadioOverlay.Services;
using System.Net;

namespace HorizonRadioOverlay.Tests;

public sealed class RemoteControlPolicyTests
{
    [Theory]
    [InlineData("previous")]
    [InlineData("next")]
    [InlineData("togglePlayPause")]
    [InlineData("toggleOverlay")]
    public void IsAllowedAction_accepts_known_actions(string action)
    {
        Assert.True(RemoteControlPolicy.IsAllowedAction(action));
    }

    [Theory]
    [InlineData("")]
    [InlineData("seek")]
    [InlineData("volumeUp")]
    [InlineData("shutdown")]
    public void IsAllowedAction_rejects_unknown_actions(string action)
    {
        Assert.False(RemoteControlPolicy.IsAllowedAction(action));
    }

    [Fact]
    public void IsAuthorized_requires_matching_non_empty_token()
    {
        Assert.True(RemoteControlPolicy.IsAuthorized("abc123", "abc123"));
        Assert.False(RemoteControlPolicy.IsAuthorized(null, "abc123"));
        Assert.False(RemoteControlPolicy.IsAuthorized("", "abc123"));
        Assert.False(RemoteControlPolicy.IsAuthorized("wrong", "abc123"));
        Assert.False(RemoteControlPolicy.IsAuthorized("abc123", ""));
    }

    [Theory]
    [InlineData("192.168.1.23", true)]
    [InlineData("10.0.0.8", true)]
    [InlineData("172.16.4.9", true)]
    [InlineData("172.31.4.9", true)]
    [InlineData("172.32.4.9", false)]
    [InlineData("8.8.8.8", false)]
    public void IsPrivateIpv4_detects_private_lan_ranges(string address, bool expected)
    {
        Assert.Equal(expected, RemoteControlPolicy.IsPrivateIpv4(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("192.168.1.23", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("0.0.0.0", false)]
    public void IsUsableLanAddress_rejects_loopback_and_link_local_addresses(string address, bool expected)
    {
        Assert.Equal(expected, RemoteControlPolicy.IsUsableLanAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void RemoteControlService_Can_Start_And_Toggle_Lan()
    {
        var lanAddresses = RemoteControlService.GetLanAddresses();
        using var service = new RemoteControlService();

        var settings = new HorizonRadioOverlay.Models.OverlaySettings
        {
            EnableRemoteControl = true,
            RemoteControlPort = 39991,
            RemoteControlAllowLan = false
        };

        service.ApplySettings(settings);
        Assert.True(service.IsRunning, $"Service failed to start: {service.LastError}");
        Assert.Contains("127.0.0.1", service.LocalUrl);

        // Toggle LAN
        settings.RemoteControlAllowLan = true;
        service.ApplySettings(settings);
        Assert.True(service.IsRunning, $"Service failed to toggle LAN: {service.LastError}");
        Assert.NotEmpty(lanAddresses);
        Assert.Contains("http://", service.LanUrl);
    }
}
