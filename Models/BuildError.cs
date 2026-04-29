namespace SI360.GateRunner.Models;

public sealed record BuildError(
    string FilePath,
    int Line,
    int Column,
    string Code,
    string Message);
