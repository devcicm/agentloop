namespace AgentLoop.Verification;
internal static class VerificationProgram
{
    public static async Task<int> Main(string[] args)
    {
        try { return await RunAsync(args); }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Console.Error.WriteLine(DataGuard.Display("ERROR verificacion: " + error.Message)); return 64; }
    }
    private static async Task<int> RunAsync(string[] args)
    {
        var command = args.FirstOrDefault() ?? "selftest";
        if (command == "project-run")
        {
            string Option(string name) { var index = Array.IndexOf(args,name); return index >= 0 && index+1 < args.Length ? args[index+1] : throw new InvalidOperationException("Falta " + name); }
            var contract = ProjectContractAdmission.Load(Option("--project-contract"),Array.IndexOf(args,"--cwd") is var cwdIndex && cwdIndex >= 0 ? args[cwdIndex+1] : Environment.CurrentDirectory);
            var profile = ProjectProfileLoader.Load(Option("--project-profile"),Path.Combine(Path.GetTempPath(),"agentloop-verification"));
            var fixture = new ProjectFixtureClient(contract,Path.GetFullPath(Option("--fixture-root")),profile.Profile.Policy.MaxArtifactBytes);
            return await AgentLoop.Program.ProjectRunAsync(args,CancellationToken.None,fixture);
        }
        return command switch
        {
            "selftest-release" => await ReleaseReadinessSelfTest.RunAsync(CancellationToken.None) ? 0 : 1,
            "selftest" => await BuiltInSelfTests.RunAsync(CancellationToken.None),
            "selftest-projects" => await ProjectSelfTest.RunAsync(CancellationToken.None) ? 0 : 1,
            "selftest-events" => await EventArchitectureSelfTest.RunAsync(CancellationToken.None) ? 0 : 1,
            "selftest-coordinator" => await EventCoordinatorSelfTest.RunAsync(CancellationToken.None) ? 0 : 1,
            _ => 64
        };
    }
}
