namespace AgentLoop.Goals.Projects;


public sealed record ValidationObservation(bool Passed, string Observations, string Environment);
