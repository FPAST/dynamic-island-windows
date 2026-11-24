using System.IO;
using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

/// <summary>The administrator steps refuse quietly (an exit code and a log line) instead of failing.</summary>
[Collection("settings file")]
public class GrantTests
{
    private static int WithTempLog(Func<int> run, out string log)
    {
        var was = Log.FilePath;
        Log.FilePath = Path.Combine(Path.GetTempPath(), "atoll-grant-" + Guid.NewGuid().ToString("N") + ".txt");
        try { int r = run(); log = File.Exists(Log.FilePath) ? File.ReadAllText(Log.FilePath) : ""; return r; }
        finally { try { File.Delete(Log.FilePath); } catch (IOException) { } Log.FilePath = was; }
    }

    [Fact]
    public void ANonsenseSidIsRefused()
    {
        Assert.Equal(1, WithTempLog(() => FpsMeter.RunGrant(["--perf-log-users", "not-a-sid"]), out var log));
        Assert.Contains("fps: grant access", log);
        Assert.NotEqual(0, WithTempLog(() => EqualizerApo.RunGrant(["--eq-access", "not-a-sid"]), out _));
        Assert.NotEqual(0, WithTempLog(() => EqualizerApo.RunGrant(["--eq-access"]), out _));
    }

    [Fact]
    public void WithoutAdministratorRightsTheGroupIsNotChangedAndNothingThrows()
    {
        if (new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) return;   // elevated (CI): not this case
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        int r = WithTempLog(() => FpsMeter.RunGrant(["--perf-log-users", me]), out var log);
        // 0 only if this user already is in the group (1378 is taken as success); otherwise access denied, logged
        Assert.True(r == 0 || log.Contains("Win32Exception"), log);
    }
}
