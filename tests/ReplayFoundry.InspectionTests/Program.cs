namespace ReplayFoundry.InspectionTests;

internal static class Program
{
    private static int Main()
    {
        IReadOnlyList<TestCase> tests =
        [
            .. MediaRationalTests.GetTests(),
            .. FfprobeJsonDeserializationTests.GetTests(),
            .. FfprobeMediaProbeExecutionTests.GetTests(),
            .. FfprobeResultMapperTests.GetTests(),
        ];

        return TestRunner.Run("Replay Foundry Inspection Tests", tests);
    }
}
